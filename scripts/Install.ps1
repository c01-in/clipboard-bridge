#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$InstallRoot = "$env:ProgramFiles\ClipboardBridge", [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot), [string]$DataRoot, [switch]$NoStartTray)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
Import-Module NetSecurity -ErrorAction Stop
$taskExistingService = Get-CimInstance Win32_Service -Filter "Name='ClipboardBridge'" -ErrorAction SilentlyContinue
if (!$DataRoot -and $taskExistingService.PathName -match '--data-dir\s+"([^"]+)"') { $DataRoot = $Matches[1] }
if (!$DataRoot) {
    $taskRepositoryCandidates = @($PackageRoot, (Split-Path -Parent $PackageRoot))
    $taskRepositoryRoot = $taskRepositoryCandidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'windows\Service\Service.csproj') } | Select-Object -First 1
    $DataRoot = if ($taskRepositoryRoot) { Join-Path $taskRepositoryRoot 'local\runtime' } else { Join-Path $env:ProgramData 'ClipboardBridge' }
}
$taskDataDir = [IO.Path]::GetFullPath($DataRoot)
$taskOwnerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskSource = Join-Path $PackageRoot 'windows'
if (!(Test-Path -LiteralPath (Join-Path $taskSource 'service\ClipboardBridge.Service.exe'))) { throw '缺少已发布的 Windows 安装文件，请先运行 Build.ps1。' }
$taskService = Get-Service -Name ClipboardBridge -ErrorAction SilentlyContinue
if ($taskService) { Stop-Service -Name ClipboardBridge -Force; $taskService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20)) }
Get-Process -Name ClipboardBridge -ErrorAction SilentlyContinue | Stop-Process
New-Item -ItemType Directory -Path $InstallRoot,$taskDataDir -Force | Out-Null
foreach ($taskPart in @('service','tray','cli')) { Copy-Item -LiteralPath (Join-Path $taskSource $taskPart) -Destination $InstallRoot -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $InstallRoot 'scripts') -Force | Out-Null
foreach($taskScript in @('Install.ps1','Uninstall.ps1')){Copy-Item -LiteralPath (Join-Path $PackageRoot ('scripts\'+$taskScript)) -Destination (Join-Path $InstallRoot ('scripts\'+$taskScript)) -Force}
$taskAndroidSource=Join-Path $PackageRoot 'ClipboardBridge.apk'
if(Test-Path -LiteralPath $taskAndroidSource){New-Item -ItemType Directory -Path (Join-Path $InstallRoot 'android') -Force | Out-Null;Copy-Item -LiteralPath $taskAndroidSource -Destination (Join-Path $InstallRoot 'android\ClipboardBridge.apk') -Force}
$taskAcl = New-Object Security.AccessControl.DirectorySecurity
$taskAcl.SetAccessRuleProtection($true,$false)
foreach ($taskSid in @('S-1-5-18','S-1-5-32-544',$taskOwnerSid)) {
    $taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($taskSid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
}
$taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-19'),'Modify','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $taskDataDir -AclObject $taskAcl
& (Join-Path $InstallRoot 'service\ClipboardBridge.Service.exe') --data-dir $taskDataDir --initialize-owner $taskOwnerSid
if ($LASTEXITCODE -ne 0) { throw '初始化配置失败' }
$taskServiceCommand = '"'+(Join-Path $InstallRoot 'service\ClipboardBridge.Service.exe')+'" --data-dir "'+$taskDataDir+'"'
if (!$taskService) {
    $taskServiceResult=Invoke-CimMethod -ClassName Win32_Service -MethodName Create -Arguments @{Name='ClipboardBridge';DisplayName='Clipboard Bridge';PathName=$taskServiceCommand;StartMode='Automatic';StartName='NT AUTHORITY\LocalService';ServiceType=[byte]16;ErrorControl=[byte]1}
} else {
    $taskServiceResult=Invoke-CimMethod -InputObject $taskExistingService -MethodName Change -Arguments @{PathName=$taskServiceCommand}
}
if($taskServiceResult.ReturnValue -ne 0){throw ('配置后台服务失败：'+$taskServiceResult.ReturnValue)}
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\ClipboardBridge' -Name DelayedAutoStart -Value 1 -PropertyType DWord -Force | Out-Null
& sc.exe failure ClipboardBridge reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe description ClipboardBridge 'FCM 剪贴板发送、加密图片传输及手机接收回执。剪贴板监听由登录用户托盘程序完成。' | Out-Null
if (!(Get-NetFirewallRule -Name ClipboardBridge-In -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name ClipboardBridge-In -DisplayName 'Clipboard Bridge · LAN and Tailscale' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 45871 -Profile Any -RemoteAddress LocalSubnet,'100.64.0.0/10','fd7a:115c:a1e0::/48' -Program (Join-Path $InstallRoot 'service\ClipboardBridge.Service.exe') | Out-Null
}
if (!(Get-NetFirewallRule -Name ClipboardBridge-Discovery -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name ClipboardBridge-Discovery -DisplayName 'Clipboard Bridge · Local discovery' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 45872 -Profile Any -RemoteAddress LocalSubnet -Program (Join-Path $InstallRoot 'service\ClipboardBridge.Service.exe') | Out-Null
}
New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ClipboardBridge -Value ('"'+(Join-Path $InstallRoot 'tray\ClipboardBridge.exe')+'"')
$taskShortcutDirectory=Join-Path ([Environment]::GetFolderPath('Programs')) 'ClipboardBridge'
New-Item -ItemType Directory -Path $taskShortcutDirectory -Force | Out-Null
$taskShortcutShell=New-Object -ComObject WScript.Shell
$taskShortcut=$taskShortcutShell.CreateShortcut((Join-Path $taskShortcutDirectory '剪贴板同步.lnk'))
$taskShortcut.TargetPath=Join-Path $InstallRoot 'tray\ClipboardBridge.exe'
$taskShortcut.Arguments='--settings'
$taskShortcut.WorkingDirectory=Join-Path $InstallRoot 'tray'
$taskShortcut.IconLocation=$taskShortcut.TargetPath+',0'
$taskShortcut.Description='剪贴板同步：Windows 与 Android 互传文字和图片'
$taskShortcut.Save()
Start-Service ClipboardBridge
if(!$NoStartTray){Start-Process -FilePath (Join-Path $InstallRoot 'tray\ClipboardBridge.exe') -WindowStyle Hidden}
Write-Host "已安装并启动。Firebase 凭据与配对密钥保存在 $taskDataDir，后续升级会保留。"
