#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot,[Parameter(Mandatory)][string]$OutputDirectory,[switch]$LocalReview)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskIdentity=Get-Content (Join-Path $taskRoot 'windows/Store/identity.json') -Raw | ConvertFrom-Json
if($LocalReview){
    $taskIdentity.name='ClipboardBridge.StoreReview'
    $taskIdentity.publisher='CN=Clipboard Bridge Contributors, OID.2.25.311729368913984317654407730594956997722=1'
    $taskIdentity.publisherDisplayName='Clipboard Bridge Contributors'
}
$taskVersion=([xml](Get-Content (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version+'.0'
$taskSource=Join-Path ([IO.Path]::GetFullPath($PackageRoot)) 'windows'
$taskApk=Join-Path ([IO.Path]::GetFullPath($PackageRoot)) 'ClipboardBridge.apk'
if(!(Test-Path -LiteralPath $taskApk)){throw 'The signed Android companion APK is required for the in-app download. Build the complete distribution first.'}
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskStage=Join-Path $taskOutput ('stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $taskStage 'app'),(Join-Path $taskStage 'Assets') -Force | Out-Null
# Merge identical application dependencies. Never silently overwrite a conflicting DLL.
foreach($taskPart in @('service','tray','cli')){
    foreach($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskSource $taskPart) -File){
        $taskTarget=Join-Path $taskStage ('app/'+$taskFile.Name)
        if(Test-Path -LiteralPath $taskTarget){
            if((Get-FileHash -LiteralPath $taskTarget).Hash -ne (Get-FileHash -LiteralPath $taskFile.FullName).Hash){throw "Conflicting application dependency: $($taskFile.Name)"}
        }else{Copy-Item -LiteralPath $taskFile.FullName -Destination $taskTarget}
    }
}
Copy-Item -LiteralPath (Join-Path $taskSource 'runtime') -Destination $taskStage -Recurse
Copy-Item -Path (Join-Path $taskSource 'share/Assets/*.png') -Destination (Join-Path $taskStage 'Assets')
New-Item -ItemType Directory -Path (Join-Path $taskStage 'android') | Out-Null
Copy-Item -LiteralPath $taskApk -Destination (Join-Path $taskStage 'android/ClipboardBridge.apk')
foreach($taskLicense in @('LICENSE','THIRD_PARTY_NOTICES.md','PRIVACY.md')){Copy-Item -LiteralPath (Join-Path $taskRoot $taskLicense) -Destination $taskStage}
'{"background":"per-user","runtime":"one-shared-copy"}' | Set-Content -LiteralPath (Join-Path $taskStage 'store-deployment.json') -Encoding utf8NoBOM
$taskName=[Security.SecurityElement]::Escape($taskIdentity.name)
$taskPublisher=[Security.SecurityElement]::Escape($taskIdentity.publisher)
$taskPublisherName=[Security.SecurityElement]::Escape($taskIdentity.publisherDisplayName)
$taskManifest=@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap uap10 desktop rescap">
 <Identity Name="$taskName" Publisher="$taskPublisher" Version="$taskVersion" ProcessorArchitecture="x64" />
 <Properties><DisplayName>Clipboard Bridge</DisplayName><PublisherDisplayName>$taskPublisherName</PublisherDisplayName><Logo>Assets\Logo50.png</Logo></Properties>
 <Resources><Resource Language="en-us" /><Resource Language="zh-cn" /><Resource Language="zh-tw" /><Resource Language="es" /><Resource Language="pt" /><Resource Language="fr" /><Resource Language="de" /><Resource Language="ja" /><Resource Language="ko" /></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Applications><Application Id="ClipboardBridge" Executable="app\ClipboardBridge.exe" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL" uap10:SupportsMultipleInstances="true">
  <uap:VisualElements DisplayName="Clipboard Bridge" Description="Share your clipboard, photos and files with your paired device" BackgroundColor="transparent" Square150x150Logo="Assets\Logo150.png" Square44x44Logo="Assets\Logo44.png" />
  <Extensions>
   <uap:Extension Category="windows.shareTarget"><uap:ShareTarget><uap:SupportedFileTypes><uap:SupportsAnyFileType /></uap:SupportedFileTypes><uap:DataFormat>StorageItems</uap:DataFormat></uap:ShareTarget></uap:Extension>
   <desktop:Extension Category="windows.startupTask" Executable="app\ClipboardBridge.exe" EntryPoint="Windows.FullTrustApplication"><desktop:StartupTask TaskId="ClipboardBridgeStartup" Enabled="true" DisplayName="Clipboard Bridge" /></desktop:Extension>
  </Extensions>
 </Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
[IO.File]::WriteAllText((Join-Path $taskStage 'AppxManifest.xml'),$taskManifest,[Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'New-WindowsVisualAssets.ps1') -PackageRoot $taskStage
$taskNuget=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
$taskMakeAppx=Join-Path $taskNuget 'microsoft.windows.sdk.buildtools/10.0.26100.9169/bin/10.0.26100.0/x64/makeappx.exe'
$taskPackage=Join-Path $taskOutput $(if($LocalReview){'ClipboardBridge-StoreReview-x64.msix'}else{'ClipboardBridge-Store-x64.msix'})
# Keep the verbose file inventory out of CI logs, and outside the packaged payload.
$taskLog=Join-Path $taskOutput 'makeappx-output.txt'
& $taskMakeAppx pack /d $taskStage /p $taskPackage /o *> $taskLog
if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $taskLog -Tail 35;throw 'Microsoft Store MSIX packaging/manifest validation failed.'}
$taskBytes=(Get-Item -LiteralPath $taskPackage).Length
@{package=$taskPackage;stage=$taskStage;version=$taskVersion;sizeBytes=$taskBytes;sha256=(Get-FileHash -LiteralPath $taskPackage).Hash;storeId=$taskIdentity.storeId} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'store-package.json') -Encoding utf8NoBOM
Write-Host ('Store MSIX ready: '+$taskPackage+' ('+[Math]::Round($taskBytes/1MB,2)+' MiB). Not submitted or published.')
