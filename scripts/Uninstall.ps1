#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess=$true)]
param([string]$InstallRoot = "$env:ProgramFiles\ClipboardBridge")
$ErrorActionPreference='Stop'
$taskResolved=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$taskExpected=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'ClipboardBridge')).TrimEnd('\')
if ($taskResolved -ne $taskExpected) { throw '卸载脚本只允许删除 Program Files\ClipboardBridge。' }
Get-Process -Name ClipboardBridge -ErrorAction SilentlyContinue | Stop-Process
if (Get-Service ClipboardBridge -ErrorAction SilentlyContinue) { Stop-Service ClipboardBridge -Force; & sc.exe delete ClipboardBridge | Out-Null }
Remove-NetFirewallRule -Name ClipboardBridge-In -ErrorAction SilentlyContinue
Remove-NetFirewallRule -Name ClipboardBridge-Discovery -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClipboardBridge -ErrorAction SilentlyContinue
$taskShortcutDirectory=Join-Path ([Environment]::GetFolderPath('Programs')) 'ClipboardBridge'
$taskShortcutPath=Join-Path $taskShortcutDirectory '剪贴板同步.lnk'
if(Test-Path -LiteralPath $taskShortcutPath){Remove-Item -LiteralPath $taskShortcutPath -Force}
if((Test-Path -LiteralPath $taskShortcutDirectory) -and !(Get-ChildItem -LiteralPath $taskShortcutDirectory -Force)){Remove-Item -LiteralPath $taskShortcutDirectory}
if (Test-Path -LiteralPath $taskResolved) { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ClipboardBridge' -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '程序已卸载；凭据与配对配置所在的数据目录予以保留。'
