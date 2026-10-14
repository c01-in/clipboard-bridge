#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$FirebaseCredentialPath,[string]$AndroidConfigPath)
$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$taskLocal = Join-Path $taskRepo 'local'
$taskRuntime = Join-Path $taskLocal 'runtime'
$taskSigning = Join-Path $taskLocal 'signing'
$taskLegacyRuntime = Join-Path $env:ProgramData 'ClipboardBridge'
$taskLegacySigning = Join-Path $env:LOCALAPPDATA 'ClipboardBridge\signing'
$taskMoves = @(@($taskLegacyRuntime,$taskRuntime),@($taskLegacySigning,$taskSigning))
foreach ($taskMove in $taskMoves) {
    $taskSourceResolved = [IO.Path]::GetFullPath($taskMove[0]).TrimEnd('\')
    $taskDestinationResolved = [IO.Path]::GetFullPath($taskMove[1]).TrimEnd('\')
    if ($taskSourceResolved -notin @([IO.Path]::GetFullPath($taskLegacyRuntime),[IO.Path]::GetFullPath($taskLegacySigning))) { throw '迁移源不在允许目录内。' }
    if (!$taskDestinationResolved.StartsWith($taskRepo+'\local\',[StringComparison]::OrdinalIgnoreCase)) { throw '迁移目标必须位于当前仓库 local 目录内。' }
    if ((Test-Path -LiteralPath $taskMove[0]) -and (Test-Path -LiteralPath $taskMove[1])) { throw "源和目标均已存在，停止迁移以避免覆盖：$($taskMove[1])" }
}
New-Item -ItemType Directory -Path $taskLocal -Force | Out-Null
$taskAcl = [Security.AccessControl.DirectorySecurity]::new()
$taskAcl.SetAccessRuleProtection($true,$false)
foreach ($taskSid in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')) {
    $taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($taskSid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
}
$taskAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-19'),'ReadAndExecute','None','None','Allow'))
Set-Acl -LiteralPath $taskLocal -AclObject $taskAcl
$taskOriginalHashes = @{}
foreach ($taskMove in $taskMoves) {
    if (Test-Path -LiteralPath $taskMove[0]) {
        Get-ChildItem -LiteralPath $taskMove[0] -File | ForEach-Object { $taskOriginalHashes[(Join-Path $taskMove[1] $_.Name)] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
}
$taskService = Get-Service ClipboardBridge -ErrorAction SilentlyContinue
if ($taskService) { Stop-Service ClipboardBridge -Force; $taskService.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20)) }
$taskCompletedMoves = @()
try {
    foreach ($taskMove in $taskMoves) {
        if (Test-Path -LiteralPath $taskMove[0]) { Move-Item -LiteralPath $taskMove[0] -Destination $taskMove[1]; $taskCompletedMoves += ,$taskMove }
    }
    foreach ($taskDestination in $taskOriginalHashes.Keys) {
        if ((Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash -ne $taskOriginalHashes[$taskDestination]) { throw '迁移后的文件校验失败。' }
    }
    & (Join-Path $PSScriptRoot 'Install.ps1') -PackageRoot (Join-Path $taskRepo 'dist') -DataRoot $taskRuntime
    if ((Get-Service ClipboardBridge).Status -ne 'Running') { throw '服务未启动。' }
} catch {
    $taskFailure = $_
    Stop-Service ClipboardBridge -Force -ErrorAction SilentlyContinue
    foreach ($taskMove in $taskCompletedMoves) {
        if (!(Test-Path -LiteralPath $taskMove[0]) -and (Test-Path -LiteralPath $taskMove[1])) {
            # Both paths were checked against the fixed legacy roots and repository local directory above.
            Move-Item -LiteralPath $taskMove[1] -Destination $taskMove[0]
        }
    }
    & sc.exe config ClipboardBridge binPath= ('"'+(Join-Path $env:ProgramFiles 'ClipboardBridge\service\ClipboardBridge.Service.exe')+'"') | Out-Null
    Start-Service ClipboardBridge -ErrorAction SilentlyContinue
    throw $taskFailure
}
$taskCredentials = Join-Path $taskLocal 'credentials'
New-Item -ItemType Directory -Path $taskCredentials -Force | Out-Null
if ($FirebaseCredentialPath) { Move-Item -LiteralPath $FirebaseCredentialPath -Destination (Join-Path $taskCredentials 'firebase-service-account.json') }
if ($AndroidConfigPath) { Move-Item -LiteralPath $AndroidConfigPath -Destination (Join-Path $taskCredentials 'google-services.json') }
[ordered]@{ migratedAt=(Get-Date).ToString('o'); runtime=$taskRuntime; signing=$taskSigning; credentials=$taskCredentials; integrityVerified=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskLocal 'migration.json') -Encoding utf8
Write-Host '凭据、配对配置和原有签名密钥已集中到仓库 local 目录；迁移前后文件 SHA-256 一致。'
