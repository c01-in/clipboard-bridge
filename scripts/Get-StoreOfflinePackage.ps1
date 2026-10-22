#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Full-package downloads run only on disposable GitHub Actions runners.'}
if($Version -notmatch '^0\.\d+\.\d+\.0$'){throw 'Expected a 0.x.y.0 Store version.'}
$taskDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(!$taskDirectory.StartsWith([IO.Path]::GetFullPath($env:RUNNER_TEMP)+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use disposable runner storage.'}
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$taskIdentity=Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'windows/Store/identity.json') -Raw | ConvertFrom-Json
$taskCatalog=Invoke-RestMethod ('https://displaycatalog.mp.microsoft.com/v7.0/products/'+$taskIdentity.storeId+'?fieldsTemplate=Details&market=US&languages=en-US,neutral')
$taskExpectedName=$taskIdentity.name+'_'+$Version+'_x64__'+$taskIdentity.packageFamilyName.Split('_')[-1]
$taskPackages=@($taskCatalog.Product.DisplaySkuAvailabilities.Sku.Properties.Packages | Where-Object {$_.PackageFullName -ceq $taskExpectedName -and $_.PackageFormat -eq 'Msix'})
if(!$taskPackages){throw 'The exact release version is not yet available in the public Microsoft Store catalog.'}
$taskCategory=$taskPackages[0].FulfillmentData.WuCategoryId
if($taskCategory -notmatch '^[0-9a-f-]{36}$'){throw 'Missing public Store update category.'}
# Protocol templates: StoreDev/StoreLib, MPL-2.0, pinned to an immutable commit.
# https://github.com/StoreDev/StoreLib/tree/3f2a98ffede0bf3f78321c194e884fd0aaf14c29
# Only XML templates are used in CI. No StoreLib binary or shared device/user token is used.
function Read-ProtocolTemplate([string]$Name){
    $taskUrl='https://raw.githubusercontent.com/StoreDev/StoreLib/3f2a98ffede0bf3f78321c194e884fd0aaf14c29/StoreLib/Xml/'+$Name
    $taskText=(Invoke-WebRequest $taskUrl).Content
    $taskNow=[DateTime]::UtcNow
    $taskText=$taskText -replace '<Created>[^<]+</Created>',('<Created>'+$taskNow.ToString('o')+'</Created>')
    $taskText=$taskText -replace '<Expires>[^<]+</Expires>',('<Expires>'+$taskNow.AddMinutes(10).ToString('o')+'</Expires>')
    $taskText -replace '<currentTime>[^<]+</currentTime>',('<currentTime>'+$taskNow.ToString('o')+'</currentTime>')
}
function Invoke-UpdateService([string]$Body,[switch]$FileLocation){
    $taskUrl='https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx'
    if($FileLocation){$taskUrl+='/secured'}
    (Invoke-WebRequest -Method Post -Uri $taskUrl -ContentType 'application/soap+xml; charset=utf-8' -Body $Body).Content
}
$taskCookieXml=[xml](Invoke-UpdateService (Read-ProtocolTemplate 'GetCookie.xml'))
$taskCookie=$taskCookieXml.SelectSingleNode('//*[local-name()="EncryptedData"]').InnerText
if(!$taskCookie){throw 'Public update-service metadata handshake failed.'}
$taskSyncBody=[string]::Format((Read-ProtocolTemplate 'WUIDRequest.xml'),$taskCookie,$taskCategory,'')
$taskMetadata=[xml][Net.WebUtility]::HtmlDecode((Invoke-UpdateService $taskSyncBody))
$taskPackageMetadata=@($taskMetadata.SelectNodes('//*[local-name()="AppxMetadata"]') | Where-Object {$_.PackageMoniker -ceq $taskExpectedName})
if($taskPackageMetadata.Count -ne 1){throw 'Public update metadata does not identify the exact Store package.'}
$taskFragments=@($taskMetadata.SelectNodes('//*[local-name()="SecuredFragment"]'))
if($taskFragments.Count -ne 1){throw 'Expected one unambiguous full-package download fragment.'}
$taskUpdate=$taskFragments[0].ParentNode.ParentNode.FirstChild
$taskFileBody=[string]::Format((Read-ProtocolTemplate 'FE3FileUrl.xml'),$taskUpdate.UpdateID,$taskUpdate.RevisionNumber,'')
$taskFiles=[xml](Invoke-UpdateService $taskFileBody -FileLocation)
$taskDownloads=@()
foreach($taskLocation in $taskFiles.SelectNodes('//*[local-name()="FileLocation"]')){
    $taskUri=[UriBuilder]::new($taskLocation.Url)
    if($taskUri.Host -notin @('tlu.dl.delivery.mp.microsoft.com','dl.delivery.mp.microsoft.com')){throw 'Unexpected download host in Microsoft update metadata.'}
    if($taskUri.Scheme -notin @('http','https')){throw 'Unexpected Microsoft delivery URL scheme.'}
    # Preserve Microsoft's delivery URL. Legacy Windows Update CDN hosts use HTTP;
    # authenticate bytes against the checksums in HTTPS catalog/update metadata.
    $taskHead=Invoke-WebRequest -Uri $taskUri.Uri -Method Head
    if([long]($taskHead.Headers['Content-Length'] -join '') -eq [long]$taskPackages[0].MaxDownloadSizeInBytes){$taskDownloads+=@{Uri=$taskUri.Uri;Sha1=$taskLocation.FileDigest}}
}
if($taskDownloads.Count -ne 1){throw 'Expected exactly one full-size package on Microsoft delivery servers.'}
$taskOutput=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'ClipboardBridge-Windows-x64.msix'
Invoke-WebRequest -Uri $taskDownloads[0].Uri -OutFile $taskOutput
if((Get-Item $taskOutput).Length -ne [long]$taskPackages[0].MaxDownloadSizeInBytes){throw 'Downloaded full package is truncated.'}
$taskDigest=[Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash $taskOutput -Algorithm SHA1).Hash))
if($taskDigest -cne $taskDownloads[0].Sha1){throw 'Downloaded bytes do not match Microsoft update metadata.'}
$taskSha256=[Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash $taskOutput -Algorithm SHA256).Hash))
if($taskPackages[0].HashAlgorithm -ne 'SHA256' -or $taskSha256 -cne $taskPackages[0].Hash){throw 'Downloaded full package does not match the SHA-256 in the HTTPS Store catalog.'}
@{sourceStoreId=$taskIdentity.storeId;version=$Version;packageFullName=$taskExpectedName;sha256=(Get-FileHash $taskOutput).Hash.ToLowerInvariant();expectedSha256=[Convert]::ToHexString([Convert]::FromBase64String($taskPackages[0].Hash)).ToLowerInvariant();sizeBytes=(Get-Item $taskOutput).Length} | ConvertTo-Json | Set-Content (Join-Path $taskDirectory 'download.json') -Encoding utf8NoBOM
'Downloaded the unmodified Microsoft Store package. Official catalog SHA-256 and update-service checksum match. No installation or execution test was performed.' | Tee-Object -Variable taskResult
$taskResult | Out-File $env:GITHUB_STEP_SUMMARY -Append
