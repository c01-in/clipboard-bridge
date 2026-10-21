#Requires -Version 7.0
[CmdletBinding()]
param([string]$OutputDirectory=$env:CLIPBRIDGE_DIST)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskDist=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'dist'}
$taskWindows=Join-Path $taskDist 'windows'
$taskFramework=([xml](Get-Content (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.TargetFramework
$taskRuntimeMajor=[int]([regex]::Match($taskFramework,'^net(\d+)\.').Groups[1].Value)
$taskFrameworks=@('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App','Microsoft.AspNetCore.App')
$taskRuntimes=@(foreach($taskLine in (& dotnet --list-runtimes)){
    if($taskLine -match '^(\S+) (\d+\.\d+\.\d+) \[(.+)\]$'){
        [pscustomobject]@{Name=$Matches[1];Version=[version]$Matches[2];Path=$Matches[3]}
    }
})
if($LASTEXITCODE -ne 0){throw 'Unable to enumerate .NET runtimes.'}
$taskRuntimeVersion=@($taskRuntimes | Where-Object { $_.Name -eq 'Microsoft.NETCore.App' -and $_.Version.Major -eq $taskRuntimeMajor } | Sort-Object Version -Descending | Where-Object {
    $taskCandidate=$_.Version
    @($taskRuntimes | Where-Object { $_.Name -in $taskFrameworks -and $_.Version -eq $taskCandidate }).Count -eq $taskFrameworks.Count
} | Select-Object -First 1)
if($taskRuntimeVersion.Count -ne 1){throw "Install the Windows x64 .NET $taskRuntimeMajor SDK with matching Core, Windows Desktop and ASP.NET runtimes before building."}
$taskVersion=$taskRuntimeVersion[0].Version.ToString()
$taskDotnetRoot=Split-Path -Parent (Split-Path -Parent $taskRuntimeVersion[0].Path)
if(!(Test-Path -LiteralPath (Join-Path $taskDotnetRoot "host\fxr\$taskVersion\hostfxr.dll"))){throw 'Missing matching .NET hostfxr.'}
if(Test-Path -LiteralPath $taskWindows){
    if([IO.Path]::GetFullPath($taskWindows) -ne (Join-Path $taskDist 'windows') -or (Get-Item -LiteralPath $taskWindows).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected Windows output directory.'}
    Remove-Item -LiteralPath $taskWindows -Recurse -Force
}
New-Item -ItemType Directory -Path $taskWindows -Force | Out-Null
foreach($taskProject in @(@('Service','service'),@('Tray','tray'),@('Cli','cli'))){
    # Search only the bundled runtime. Neither global .NET nor DOTNET_ROOT is used.
    & dotnet publish (Join-Path $taskRoot "windows\$($taskProject[0])\$($taskProject[0]).csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=../runtime -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $taskWindows $taskProject[1]) --nologo --disable-build-servers -m:1
    if($LASTEXITCODE -ne 0){throw "Windows $($taskProject[0]) publish failed."}
}
$taskRuntime=Join-Path $taskWindows 'runtime'
New-Item -ItemType Directory -Path (Join-Path $taskRuntime 'shared'),(Join-Path $taskRuntime 'host\fxr') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskDotnetRoot 'dotnet.exe') -Destination $taskRuntime
Copy-Item -LiteralPath (Join-Path $taskDotnetRoot "host\fxr\$taskVersion") -Destination (Join-Path $taskRuntime 'host\fxr') -Recurse
foreach($taskName in $taskFrameworks){
    $taskSource=@($taskRuntimes | Where-Object { $_.Name -eq $taskName -and $_.Version.ToString() -eq $taskVersion })[0]
    $taskDestination=Join-Path $taskRuntime ('shared\'+$taskName)
    New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskSource.Path $taskVersion) -Destination $taskDestination -Recurse
}
foreach($taskNotice in @('LICENSE.txt','ThirdPartyNotices.txt')){
    if(!(Test-Path -LiteralPath (Join-Path $taskDotnetRoot $taskNotice))){throw "Missing runtime license: $taskNotice"}
    Copy-Item -LiteralPath (Join-Path $taskDotnetRoot $taskNotice) -Destination $taskRuntime
}
& (Join-Path $PSScriptRoot 'Build-WindowsShare.ps1') -OutputDirectory $taskDist
& (Join-Path $PSScriptRoot 'Test-WindowsPackage.ps1') -PackageRoot $taskDist
Write-Host "Windows applications with one bundled .NET $taskVersion runtime ready: $taskWindows"
