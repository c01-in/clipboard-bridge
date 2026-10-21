#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess=$true)]
param([string]$InstallRoot = "$env:ProgramFiles\ClipboardBridge")
$ErrorActionPreference='Stop'
$taskResolved=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$taskExpected=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'ClipboardBridge')).TrimEnd('\')
if ($taskResolved -ne $taskExpected) { throw '卸载脚本只允许删除 Program Files\ClipboardBridge。' }
Get-Process -Name ClipboardBridge -ErrorAction SilentlyContinue | Stop-Process
$taskShareRegistration=Join-Path $taskResolved 'scripts\Register-WindowsShare.ps1'
if(Test-Path -LiteralPath $taskShareRegistration){
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File $taskShareRegistration -InstallRoot $taskResolved -Unregister
    if($LASTEXITCODE -ne 0){throw 'Windows Share removal failed.'}
}
if (Get-Service ClipboardBridge -ErrorAction SilentlyContinue) { Stop-Service ClipboardBridge -Force; & sc.exe delete ClipboardBridge | Out-Null }
Remove-NetFirewallRule -Name ClipboardBridge-In -ErrorAction SilentlyContinue
Remove-NetFirewallRule -Name ClipboardBridge-Discovery -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClipboardBridge -ErrorAction SilentlyContinue
$taskShortcutDirectory=Join-Path ([Environment]::GetFolderPath('Programs')) 'ClipboardBridge'
foreach($taskShortcutName in @('剪贴板同步','Clipboard Bridge')){
    $taskSendToPath=Join-Path ([Environment]::GetFolderPath('SendTo')) ($taskShortcutName+'.lnk')
    if(Test-Path -LiteralPath $taskSendToPath){Remove-Item -LiteralPath $taskSendToPath -Force}
    $taskShortcutPath=Join-Path $taskShortcutDirectory ($taskShortcutName+'.lnk')
    if(Test-Path -LiteralPath $taskShortcutPath){Remove-Item -LiteralPath $taskShortcutPath -Force}
}
if((Test-Path -LiteralPath $taskShortcutDirectory) -and !(Get-ChildItem -LiteralPath $taskShortcutDirectory -Force)){Remove-Item -LiteralPath $taskShortcutDirectory}
if (Test-Path -LiteralPath $taskResolved) { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClipboardBridge' -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '程序已卸载；凭据与配对配置所在的数据目录予以保留。'
