#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$taskWindows=Join-Path ([IO.Path]::GetFullPath($PackageRoot)) 'windows'
$taskRuntime=Join-Path $taskWindows 'runtime'
$taskCore=@(Get-ChildItem -LiteralPath $taskWindows -File -Recurse -Filter coreclr.dll)
if($taskCore.Count -ne 1 -or !$taskCore[0].FullName.StartsWith($taskRuntime+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Expected exactly one private CoreCLR runtime.'}
$taskVersion=Split-Path -Leaf (Split-Path -Parent $taskCore[0].FullName)
$taskShare=Join-Path $taskWindows 'share'
$taskSharePackage=Join-Path $taskShare 'ClipboardBridge.Share.msix'
if(!(Test-Path -LiteralPath $taskSharePackage)){throw 'Windows Share identity package is missing.'}
$taskShareManifest=[xml](Get-Content -LiteralPath (Join-Path $taskShare 'AppxManifest.xml') -Raw)
if(!$taskShareManifest.SelectSingleNode('//*[local-name()="Extension" and @Category="windows.shareTarget"]/*[local-name()="ShareTarget"]/*[local-name()="DataFormat" and text()="StorageItems"]')){throw 'Windows Share does not declare file input.'}
if(!(Test-Path -LiteralPath (Join-Path $taskWindows 'tray\Microsoft.Windows.SDK.NET.dll')) -or !(Test-Path -LiteralPath (Join-Path $taskWindows 'tray\WinRT.Runtime.dll'))){throw 'Windows Share WinRT runtime projections are missing.'}
$taskIdentityArchive=[IO.Compression.ZipFile]::OpenRead($taskSharePackage)
try{if($taskIdentityArchive.Entries | Where-Object {$_.FullName -match '\.(exe|dll)$'}){throw 'The identity package must not duplicate application or runtime binaries.'}}
finally{$taskIdentityArchive.Dispose()}
foreach($taskRequired in @("host\fxr\$taskVersion\hostfxr.dll","shared\Microsoft.NETCore.App\$taskVersion\System.Private.CoreLib.dll","shared\Microsoft.WindowsDesktop.App\$taskVersion\System.Windows.Forms.dll","shared\Microsoft.AspNetCore.App\$taskVersion\Microsoft.AspNetCore.dll",'LICENSE.txt','ThirdPartyNotices.txt')){
    if(!(Test-Path -LiteralPath (Join-Path $taskRuntime $taskRequired))){throw "Missing bundled Windows runtime file: $taskRequired"}
}
foreach($taskPart in @(@('service','ClipboardBridge.Service'),@('tray','ClipboardBridge'),@('cli','ClipboardBridge.Cli'))){
    $taskApp=Join-Path $taskWindows $taskPart[0]
    if(!(Test-Path -LiteralPath (Join-Path $taskApp ($taskPart[1]+'.exe')))){throw 'Missing Windows application.'}
    $taskConfig=Get-Content -LiteralPath (Join-Path $taskApp ($taskPart[1]+'.runtimeconfig.json')) -Raw | ConvertFrom-Json
    $taskFrameworks=@($taskConfig.runtimeOptions.framework)+@($taskConfig.runtimeOptions.frameworks) | Where-Object {$_}
    if(!$taskFrameworks -or $taskConfig.runtimeOptions.includedFrameworks){throw 'Applications must use the shared private framework layout.'}
    foreach($taskFramework in $taskFrameworks){
        if(!(Test-Path -LiteralPath (Join-Path $taskRuntime ("shared\$($taskFramework.name)\$taskVersion"))) -or [version]$taskVersion -lt [version]$taskFramework.version -or ([version]$taskVersion).Major -ne ([version]$taskFramework.version).Major){throw 'Bundled runtime does not satisfy the application.'}
    }
}
function Invoke-BundledCli([string]$Exe){
    $taskStart=[Diagnostics.ProcessStartInfo]::new($Exe)
    $taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true
    $taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
    $taskStart.Environment['PATH']=$env:WINDIR+'\System32'
    $taskStart.Environment['DOTNET_ROOT']=Join-Path $taskWindows 'no-global-runtime'
    $taskStart.Environment['DOTNET_ROOT_X64']=$taskStart.Environment['DOTNET_ROOT']
    $taskStart.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
    $taskProcess=[Diagnostics.Process]::Start($taskStart)
    try {
        $taskOutput=$taskProcess.StandardOutput.ReadToEndAsync();$taskError=$taskProcess.StandardError.ReadToEndAsync()
        if(!$taskProcess.WaitForExit(15000)){$taskProcess.Kill();throw 'Bundled CLI startup timed out.'}
        return @{ExitCode=$taskProcess.ExitCode;Output=$taskOutput.GetAwaiter().GetResult();Error=$taskError.GetAwaiter().GetResult()}
    }finally{$taskProcess.Dispose()}
}
$taskExe=Join-Path $taskWindows 'cli\ClipboardBridge.Cli.exe'
$taskResult=Invoke-BundledCli $taskExe
if($taskResult.ExitCode -ne 0 -or $taskResult.Output -notmatch 'pairing-status'){throw ('Bundled CLI failed to start: '+$taskResult.Error)}
# Withhold only this disposable package's private runtime. Global .NET must not mask it.
$taskWithheld=Join-Path $taskWindows ('runtime-withheld-'+[Guid]::NewGuid().ToString('N'))
try {
    Move-Item -LiteralPath $taskRuntime -Destination $taskWithheld
    $taskResult=Invoke-BundledCli $taskExe
    if($taskResult.ExitCode -eq 0){throw 'Apphost incorrectly fell back to a machine-installed runtime.'}
}finally{
    if(Test-Path -LiteralPath $taskWithheld){Move-Item -LiteralPath $taskWithheld -Destination $taskRuntime}
}
Write-Host "PASS: one bundled .NET $taskVersion runtime; CLI starts without dotnet on PATH and cannot fall back to global .NET."
