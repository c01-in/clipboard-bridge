#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag,[Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Release asset publication runs only on GitHub Actions.'}
if($Tag -notmatch '^v0\.\d+\.\d+$'){throw 'An existing preview release is required.'}
function Invoke-GitHub([string[]]$Arguments){
    $taskResult=& gh @Arguments
    if($LASTEXITCODE -ne 0){throw ('GitHub operation failed: '+(($Arguments | Select-Object -First 3) -join ' '))}
    $taskResult
}
$taskPackage=Join-Path $PackageDirectory 'ClipboardBridge-Windows-x64.msix'
$taskEvidence=Get-Content (Join-Path $PackageDirectory 'download.json') -Raw | ConvertFrom-Json
if($taskEvidence.version -ne ($Tag.Substring(1)+'.0') -or $taskEvidence.sourceStoreId -cne '9PJT9PTSMCJQ' -or $taskEvidence.sha256 -cne $taskEvidence.expectedSha256){throw 'Matching version and official Microsoft checksum are required.'}
if((Get-FileHash $taskPackage).Hash.ToLowerInvariant() -cne $taskEvidence.expectedSha256 -or (Get-Item $taskPackage).Length -ne $taskEvidence.sizeBytes){throw 'Downloaded package changed before publication.'}
$taskRelease=(Invoke-GitHub -Arguments @('release','view',$Tag,'--json','tagName,assets')) -join "`n" | ConvertFrom-Json
$taskNames=@('ClipboardBridge.apk','ClipboardBridge.aab','ClipboardBridge-Windows-Store-Setup.exe','ClipboardBridge-Android-Library-Sources.zip')
$taskChecksumAsset=$taskRelease.assets | Where-Object name -eq 'SHA256SUMS.txt'
if(!$taskChecksumAsset){throw 'Original release checksums are missing.'}
$taskOriginalChecksums=(Invoke-WebRequest $taskChecksumAsset.url).Content
foreach($taskName in $taskNames){
    $taskAsset=$taskRelease.assets | Where-Object name -ceq $taskName
    if(!$taskAsset -or $taskAsset.digest -notmatch '^sha256:([a-f0-9]{64})$'){throw "Missing release asset or digest: $taskName"}
    $taskExpected=$Matches[1]+'  '+$taskName
    if($taskExpected -cnotin ($taskOriginalChecksums -split '\r?\n')){throw "Original release checksum mismatch: $taskName"}
}
$taskExisting=$taskRelease.assets | Where-Object name -ceq 'ClipboardBridge-Windows-x64.msix'
if($taskExisting){
    if($taskExisting.digest -cne ('sha256:'+$taskEvidence.sha256)){throw 'An existing full package has different bytes; refusing to overwrite it.'}
}else{
    Invoke-GitHub -Arguments @('release','upload',$Tag,$taskPackage) | Out-Host
}
# Keep every original binary and checksum; add only the verified Store-signed MSIX.
$taskChecksums=@($taskOriginalChecksums -split '\r?\n' | Where-Object {$_ -and $_ -notmatch '  ClipboardBridge-Windows-x64\.msix$'})
$taskChecksums+=($taskEvidence.sha256+'  ClipboardBridge-Windows-x64.msix')
$taskChecksumFile=Join-Path $PackageDirectory 'SHA256SUMS.txt'
[IO.File]::WriteAllLines($taskChecksumFile,$taskChecksums,[Text.UTF8Encoding]::new($false))
Invoke-GitHub -Arguments @('release','upload',$Tag,$taskChecksumFile,'--clobber') | Out-Host
$taskNotes=Join-Path (Split-Path $PSScriptRoot -Parent) ('docs/release-notes/'+$Tag+'.md')
Invoke-GitHub -Arguments @('release','edit',$Tag,'--notes-file',$taskNotes) | Out-Host
$taskPublished=(Invoke-GitHub -Arguments @('release','view',$Tag,'--json','assets')) -join "`n" | ConvertFrom-Json
$taskAsset=$taskPublished.assets | Where-Object name -ceq 'ClipboardBridge-Windows-x64.msix'
if($taskAsset.digest -cne ('sha256:'+$taskEvidence.sha256) -or $taskAsset.size -ne $taskEvidence.sizeBytes){throw 'Published full-package digest or size is incorrect.'}
'Added the trusted full MSIX alongside the existing Microsoft-signed online installer. Version and original binaries are unchanged.' | Out-File $env:GITHUB_STEP_SUMMARY -Append
