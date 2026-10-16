#Requires -Version 7.0
[CmdletBinding()]
param([string]$AndroidSdk=$env:ANDROID_HOME)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskDist=Join-Path $taskRoot 'dist'
$taskVersion=([xml](Get-Content (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if($taskVersion -notmatch '^0\.\d+\.\d+$'){throw 'Preview releases must use a 0.x.y version.'}
foreach($taskPart in @(@('service','ClipboardBridge.Service.exe'),@('tray','ClipboardBridge.exe'),@('cli','ClipboardBridge.Cli.exe'))){
    $taskFile=Join-Path $taskDist ('windows\'+$taskPart[0]+'\'+$taskPart[1])
    if(!(Test-Path -LiteralPath $taskFile) -or (Get-Item $taskFile).VersionInfo.ProductVersion.Split('+')[0] -ne $taskVersion){throw 'Build all Windows applications at the declared version before packaging.'}
}
$taskApk=Join-Path $taskDist 'ClipboardBridge.apk'
$taskAapt=Join-Path $AndroidSdk 'build-tools\36.0.0\aapt.exe'
$taskBadging=(& $taskAapt dump badging $taskApk) -join "`n"
if($LASTEXITCODE -ne 0 -or $taskBadging -notmatch ('versionName='''+[regex]::Escape($taskVersion)+'''')){throw 'The signed Android APK version must match Windows.'}
$taskStage=Join-Path $taskDist ('package-'+[Guid]::NewGuid().ToString('N'))
$taskRelease=Join-Path $taskDist 'release'
New-Item -ItemType Directory -Path $taskStage,$taskRelease -Force | Out-Null
try{
    New-Item -ItemType Directory -Path (Join-Path $taskStage 'windows'),(Join-Path $taskStage 'scripts') -Force | Out-Null
    foreach($taskPart in @('service','tray','cli')){
        $taskDestination=Join-Path $taskStage ('windows\'+$taskPart)
        New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
        foreach($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskDist ('windows\'+$taskPart)) -File){
            if($taskFile.Extension -eq '.pdb'){continue}
            Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination
        }
    }
    foreach($taskScript in @('Install.ps1','Uninstall.ps1')){
        # Windows PowerShell 5.1 needs a BOM to decode the localized shortcut and messages.
        [IO.File]::WriteAllText((Join-Path $taskStage ('scripts\'+$taskScript)),[IO.File]::ReadAllText((Join-Path $PSScriptRoot $taskScript)),[Text.UTF8Encoding]::new($true))
    }
    foreach($taskName in @('LICENSE','THIRD_PARTY_NOTICES.md','PRIVACY.md','README.md','README.zh-CN.md')){Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskStage}
    # Keep the README illustrations available in the manual installation ZIP.
    New-Item -ItemType Directory -Path (Join-Path $taskStage 'docs') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/assets') -Destination (Join-Path $taskStage 'docs') -Recurse
    Copy-Item -LiteralPath $taskApk -Destination $taskStage
    $taskZip=Join-Path $taskRelease 'ClipboardBridge-Windows-x64.zip'
    if(Test-Path -LiteralPath $taskZip){Remove-Item -LiteralPath $taskZip -Force}
    [IO.Compression.ZipFile]::CreateFromDirectory($taskStage,$taskZip,[IO.Compression.CompressionLevel]::Optimal,$false)
    $taskFramework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $taskAssemblyInfo=Join-Path $taskStage 'AssemblyInfo.cs'
    [IO.File]::WriteAllText($taskAssemblyInfo,('using System.Reflection;[assembly:AssemblyVersion("'+$taskVersion+'.0")][assembly:AssemblyFileVersion("'+$taskVersion+'.0")][assembly:AssemblyProduct("Clipboard Bridge")][assembly:AssemblyTitle("Clipboard Bridge Setup")]'))
    $taskSetup=Join-Path $taskRelease 'ClipboardBridge-Windows-x64-Setup.exe'
    $taskArguments=@('/nologo','/target:winexe','/platform:x64',('/out:'+$taskSetup),('/win32manifest:'+(Join-Path $taskRoot 'installer\Setup.manifest')),('/win32icon:'+(Join-Path $taskRoot 'windows\Tray\Assets\ClipboardBridge.ico')),('/resource:'+$taskZip+',ClipboardBridge.Payload.zip'))
    foreach($taskLibrary in @('System.Windows.Forms','System.Drawing','System.Xml.Linq','System.IO.Compression','System.IO.Compression.FileSystem')){$taskArguments+=('/reference:'+(Join-Path $taskFramework ($taskLibrary+'.dll')))}
    $taskLocales=@{en='values';'zh-Hans'='values-b+zh+Hans';'zh-Hant'='values-b+zh+Hant';es='values-es';pt='values-pt';fr='values-fr';de='values-de';ja='values-ja';ko='values-ko'}
    foreach($taskLocale in $taskLocales.GetEnumerator()){$taskArguments+=('/resource:'+(Join-Path $taskRoot ('android\app\src\main\res\'+$taskLocale.Value+'\strings.xml'))+',ClipboardBridge.Locale.'+$taskLocale.Key+'.xml')}
    $taskArguments+=(Join-Path $taskRoot 'installer\Setup.cs');$taskArguments+=$taskAssemblyInfo
    & (Join-Path $taskFramework 'csc.exe') @taskArguments
    if($LASTEXITCODE -ne 0){throw 'Windows installer compilation failed.'}
    Copy-Item -LiteralPath $taskApk -Destination (Join-Path $taskRelease 'ClipboardBridge.apk') -Force
    Copy-Item -LiteralPath (Join-Path $taskDist 'ClipboardBridge.aab') -Destination $taskRelease -Force
    Copy-Item -LiteralPath (Join-Path $taskDist 'ClipboardBridge-Android-Library-Sources.zip') -Destination $taskRelease -Force
    $taskChecksums=foreach($taskName in @('ClipboardBridge.apk','ClipboardBridge.aab','ClipboardBridge-Windows-x64-Setup.exe','ClipboardBridge-Windows-x64.zip','ClipboardBridge-Android-Library-Sources.zip')){(Get-FileHash (Join-Path $taskRelease $taskName)).Hash.ToLowerInvariant()+'  '+$taskName}
    [IO.File]::WriteAllLines((Join-Path $taskRelease 'SHA256SUMS.txt'),$taskChecksums,[Text.UTF8Encoding]::new($false))
    Write-Host "Release packages ready: $taskRelease"
}finally{
    if(![IO.Path]::GetFullPath($taskStage).StartsWith([IO.Path]::GetFullPath($taskDist)+[IO.Path]::DirectorySeparatorChar) -or !(Split-Path $taskStage -Leaf).StartsWith('package-')){throw 'Unexpected packaging directory'}
    Remove-Item -LiteralPath $taskStage -Recurse -Force
}
