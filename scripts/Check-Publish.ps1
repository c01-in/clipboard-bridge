#Requires -Version 7.0
[CmdletBinding()]
param([switch]$IncludeHistory,[switch]$UseLocalSecrets)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskKnownValues = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Add-PrivateValue([object]$Value) {
    if ($Value -is [string] -and $Value.Length -ge 12) { [void]$taskKnownValues.Add($Value.Replace("`r`n","`n")) }
}
if ($UseLocalSecrets) {
    $taskCredentialFile = Join-Path $taskRepo 'local\credentials\firebase-service-account.json'
    if (Test-Path -LiteralPath $taskCredentialFile) {
        $taskAccount = Get-Content -LiteralPath $taskCredentialFile -Raw | ConvertFrom-Json
        foreach ($taskName in @('project_id','private_key_id','private_key','client_email','client_id')) { Add-PrivateValue $taskAccount.$taskName }
    }
    $taskAndroidFile = Join-Path $taskRepo 'local\credentials\google-services.json'
    if (Test-Path -LiteralPath $taskAndroidFile) {
        $taskAndroid = Get-Content -LiteralPath $taskAndroidFile -Raw | ConvertFrom-Json
        Add-PrivateValue $taskAndroid.project_info.project_id
        Add-PrivateValue ([string]$taskAndroid.project_info.project_number)
        foreach ($taskClient in $taskAndroid.client) {
            Add-PrivateValue $taskClient.client_info.mobilesdk_app_id
            foreach ($taskApiKey in $taskClient.api_key) { Add-PrivateValue $taskApiKey.current_key }
        }
    }
    $taskDeploymentFile = Join-Path $taskRepo 'local\deployment.json'
    if (Test-Path -LiteralPath $taskDeploymentFile) {
        $taskDeployment = Get-Content -LiteralPath $taskDeploymentFile -Raw | ConvertFrom-Json
        foreach ($taskName in @('firebaseProject','googleAccount','senderServiceAccount','localEndpoint')) { Add-PrivateValue $taskDeployment.$taskName }
    }
    $taskVaultFile = Join-Path $taskRepo 'local\runtime\configuration.dpapi'
    if (Test-Path -LiteralPath $taskVaultFile) {
        if (!$IsWindows) { throw '读取本机 DPAPI 配置的精确值检查需要 PowerShell 7 on Windows。' }
        Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
        $taskPlaintext = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskVaultFile),$null,[Security.Cryptography.DataProtectionScope]::LocalMachine)
        $taskVault = [Text.Encoding]::UTF8.GetString($taskPlaintext) | ConvertFrom-Json
        Add-PrivateValue $taskVault.key
        Add-PrivateValue $taskVault.deviceToken
        [Array]::Clear($taskPlaintext,0,$taskPlaintext.Length)
        $taskVault=$null
    }
}
$taskFailures = [Collections.Generic.List[string]]::new()
$taskChecked = [Collections.Generic.HashSet[string]]::new()
$taskPaths = '(^|/)(local|secrets|\.idea|\.vscode|node_modules)/|(^|/)(google-services\.json|deployment\.json|pairing\.txt|\.env(?:\..*)?)$|\.(?:jks|keystore|p12|pfx|dpapi|clixml|pem|key|apk|exe|pdb|zip)$|service-account[^/]*\.json$'
$taskPatterns = @(
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    'AIza[0-9A-Za-z_-]{35}',
    'gh[pousr]_[0-9A-Za-z]{30,}',
    'github_pat_[0-9A-Za-z_]{30,}',
    '"private_key"\s*:\s*"[^"\r\n]{20,}',
    '(?i)[A-Z]:[\\/]+Users[\\/]+(?!Public[\\/]|<)[^\s"<>|]+',
    'clipbridge://pair\?data=[0-9A-Za-z%+/=]{40,}'
)
function Check-Tree([string]$Ref) {
    $taskFiles = if ($Ref -eq 'INDEX') { @(git -C $taskRepo ls-files --cached) } else { @(git -C $taskRepo ls-tree -r --name-only $Ref) }
    if ($LASTEXITCODE -ne 0) { throw "无法读取 Git 树：$Ref" }
    foreach ($taskFile in $taskFiles) {
        if ($taskFile -match $taskPaths) { $taskFailures.Add("$Ref : $taskFile [private/generated file path]"); continue }
        $taskSpec = if ($Ref -eq 'INDEX') { ':'+$taskFile } else { $Ref+':'+$taskFile }
        $taskBlob = git -C $taskRepo rev-parse $taskSpec
        if (!$taskChecked.Add($taskBlob)) { continue }
        if ($taskFile -match '\.(png|ico|jar)$') { continue }
        $taskText = (git -C $taskRepo show $taskSpec) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "无法读取待检查文件：$taskFile" }
        foreach ($taskPattern in $taskPatterns) {
            if ($taskText -match $taskPattern) { $taskFailures.Add("$Ref : $taskFile [credential or personal-path pattern]"); break }
        }
        foreach ($taskValue in $taskKnownValues) {
            if ($taskText.Contains($taskValue,[StringComparison]::Ordinal)) { $taskFailures.Add("$Ref : $taskFile [exact local private value]"); break }
        }
    }
}
Check-Tree 'INDEX'
if ($IncludeHistory) { foreach ($taskCommit in @(git -C $taskRepo rev-list --all)) { Check-Tree $taskCommit } }
if ($taskFailures.Count) {
    $taskFailures | Sort-Object -Unique | ForEach-Object { Write-Host $_ }
    throw '发布检查失败。输出只包含文件路径和原因，不回显秘密内容。'
}
Write-Host "发布检查通过：$($taskChecked.Count) 个唯一 Git 对象；精确比对 $($taskKnownValues.Count) 个本地私密值；未输出私密值。"
