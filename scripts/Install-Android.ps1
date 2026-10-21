#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Serial,
    [Parameter(Mandatory)][string]$ApkPath,
    [string]$Adb='adb'
)
$ErrorActionPreference='Stop'
$taskApk=(Resolve-Path -LiteralPath $ApkPath).Path
if([IO.Path]::GetExtension($taskApk) -ne '.apk'){throw 'Provide an APK file.'}
$taskState=(& $Adb -s $Serial get-state) -join "`n"
if($LASTEXITCODE -ne 0 -or $taskState.Trim() -ne 'device'){throw 'The specified Android device is not available.'}
$taskUsers=(& $Adb -s $Serial shell pm list users) -join "`n"
if($LASTEXITCODE -ne 0 -or $taskUsers -notmatch 'UserInfo\{0:'){throw 'Cannot confirm primary Android user 0; refusing installation.'}
# No user parameter or all-users fallback: clone and private profiles are excluded.
$taskResult=(& $Adb -s $Serial install --user 0 -r $taskApk 2>&1) -join "`n"
if($LASTEXITCODE -ne 0 -or $taskResult -notmatch '(?m)^Success\s*$'){throw ('Primary-user installation failed: '+$taskResult)}
Write-Host 'APK installed for primary Android user 0 only.'
