#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[ValidateRange(1,65535)][int]$IdentityRevision=1)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskShare=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'windows\share'
New-Item -ItemType Directory -Path (Join-Path $taskShare 'Assets') -Force | Out-Null
$taskNuget=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
$taskTools=Join-Path $taskNuget 'microsoft.windows.sdk.buildtools\10.0.26100.9169\bin\10.0.26100.0\x64'
$taskMakeAppx=Join-Path $taskTools 'makeappx.exe'
if(!(Test-Path -LiteralPath $taskMakeAppx)){throw 'Restore the Tray project to obtain Windows SDK BuildTools.'}
$taskPublisher='CN=Clipboard Bridge Contributors, OID.2.25.311729368913984317654407730594956997722=1'
$taskCertificate=$null
if($env:CLIPBRIDGE_WINDOWS_SHARE_PUBLISHER){
    $taskPublisher=$env:CLIPBRIDGE_WINDOWS_SHARE_PUBLISHER
    if($taskPublisher -notmatch '^CN=' -or $taskPublisher -match '[\r\n]' -or $taskPublisher -match 'OID\.2\.25\.311729368913984317654407730594956997722'){throw 'Use the exact public signing certificate subject for the cloud-signing publisher.'}
}
if($env:CLIPBRIDGE_WINDOWS_SHARE_CERT_THUMBPRINT){
    if($env:CLIPBRIDGE_WINDOWS_SHARE_CERT_THUMBPRINT -notmatch '^[0-9A-Fa-f]{40}$'){throw 'Invalid Windows Share signing certificate thumbprint.'}
    $taskCertificate=Get-Item -LiteralPath ('Cert:\CurrentUser\My\'+$env:CLIPBRIDGE_WINDOWS_SHARE_CERT_THUMBPRINT)
    if(!$taskCertificate.HasPrivateKey -or $taskCertificate.NotAfter -lt (Get-Date)){throw 'A valid Windows Share signing private key is required.'}
    if($env:CLIPBRIDGE_WINDOWS_SHARE_PUBLISHER -and $taskPublisher -ne $taskCertificate.Subject){throw 'Windows Share publisher and local signing certificate do not match.'}
    $taskPublisher=$taskCertificate.Subject
}
$taskPublisherXml=[Security.SecurityElement]::Escape($taskPublisher)
$taskVersion=([xml](Get-Content (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version+'.'+$IdentityRevision
$taskStage=Join-Path ([IO.Path]::GetTempPath()) ('ClipboardBridge-share-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $taskStage 'share\Assets') -Force | Out-Null
$taskManifest=@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap uap10 rescap">
 <Identity Name="ClipboardBridge.SharePreview" Publisher="$taskPublisherXml" Version="$taskVersion" ProcessorArchitecture="x64" />
 <Properties><DisplayName>Clipboard Bridge</DisplayName><PublisherDisplayName>Clipboard Bridge</PublisherDisplayName><Logo>share\Assets\Logo50.png</Logo><uap10:AllowExternalContent>true</uap10:AllowExternalContent></Properties>
 <Resources><Resource Language="en-us" /></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Applications><Application Id="ClipboardBridge" Executable="tray\ClipboardBridge.exe" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL" uap10:SupportsMultipleInstances="true">
  <uap:VisualElements AppListEntry="none" DisplayName="Clipboard Bridge" Description="Share files with your paired device" BackgroundColor="transparent" Square150x150Logo="share\Assets\Logo150.png" Square44x44Logo="share\Assets\Logo44.png" />
  <Extensions><uap:Extension Category="windows.shareTarget"><uap:ShareTarget><uap:SupportedFileTypes><uap:SupportsAnyFileType /></uap:SupportedFileTypes><uap:DataFormat>StorageItems</uap:DataFormat></uap:ShareTarget></uap:Extension></Extensions>
 </Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /><rescap:Capability Name="unvirtualizedResources" /></Capabilities>
</Package>
"@
[IO.File]::WriteAllText((Join-Path $taskStage 'AppxManifest.xml'),$taskManifest,[Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'New-WindowsVisualAssets.ps1') -PackageRoot $taskStage -AssetPath 'share\Assets'
& $taskMakeAppx pack /d $taskStage /p (Join-Path $taskShare 'ClipboardBridge.Share.msix') /nv /o *> (Join-Path $taskShare 'makeappx-output.txt')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $taskShare 'makeappx-output.txt') -Tail 15;throw 'Share identity package build failed.'}
if($taskCertificate){
    & (Join-Path $taskTools 'signtool.exe') sign /sha1 $taskCertificate.Thumbprint /s My /fd SHA256 (Join-Path $taskShare 'ClipboardBridge.Share.msix')
    if($LASTEXITCODE -ne 0){throw 'Windows Share identity signing failed.'}
}
Copy-Item -LiteralPath (Join-Path $taskStage 'AppxManifest.xml') -Destination $taskShare -Force
Get-ChildItem -LiteralPath $taskStage -Filter '*.pri' -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $taskShare -Force}
Get-ChildItem -LiteralPath (Join-Path $taskStage 'share\Assets') -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskShare 'Assets') -Force}
# Keep a small, reproducible manifest/assets staging directory if cleanup is unavailable.
Write-Host "Windows Share identity preview ready: $taskShare"
