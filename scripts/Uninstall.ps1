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
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClipboardBridge -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $taskResolved) { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
Write-Host '程序已卸载；凭据与配对配置所在的数据目录予以保留。'
