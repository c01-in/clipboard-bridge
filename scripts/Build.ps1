#Requires -Version 7.0
[CmdletBinding()]
param([string]$JavaHome=$env:JAVA_HOME,[string]$AndroidSdk=$env:ANDROID_HOME,[string]$Gradle)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
if (!$JavaHome -or !(Test-Path -LiteralPath (Join-Path $JavaHome 'bin\java.exe'))) { throw '请设置 JAVA_HOME 或通过 -JavaHome 指定 JDK 21 目录。' }
if (!$AndroidSdk -or !(Test-Path -LiteralPath (Join-Path $AndroidSdk 'platforms\android-36\android.jar'))) { throw '请设置 ANDROID_HOME 或通过 -AndroidSdk 指定包含 Android SDK 36 的目录。' }
if (!$Gradle) { $Gradle = Join-Path $taskRoot 'android\gradlew.bat' }
$taskDist=Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null
foreach ($taskProject in @(@('Service','service'),@('Tray','tray'),@('Cli','cli'))) {
    & dotnet publish (Join-Path $taskRoot "windows\$($taskProject[0])\$($taskProject[0]).csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $taskDist "windows\$($taskProject[1])") --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows 编译失败' }
}
$env:JAVA_HOME=$JavaHome;$env:ANDROID_HOME=$AndroidSdk
$env:Path=(Join-Path $JavaHome 'bin')+';'+$env:Path
$taskSigning=Join-Path $taskRoot 'local\signing'
New-Item -ItemType Directory -Path $taskSigning -Force | Out-Null
$taskSigningAcl=Get-Acl -LiteralPath $taskSigning;$taskSigningAcl.SetAccessRuleProtection($true,$false)
foreach($taskSid in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')) { $taskSigningAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($taskSid),'FullControl','ContainerInherit,ObjectInherit','None','Allow')) }
Set-Acl -LiteralPath $taskSigning -AclObject $taskSigningAcl
$taskPasswordPath=Join-Path $taskSigning 'password.clixml'
if (!(Test-Path -LiteralPath $taskPasswordPath)) { $taskRandom=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)); ConvertTo-SecureString $taskRandom -AsPlainText -Force | Export-Clixml -LiteralPath $taskPasswordPath }
$taskPassword=Import-Clixml -LiteralPath $taskPasswordPath
$env:CLIPBRIDGE_STORE_PASSWORD=[Net.NetworkCredential]::new('',$taskPassword).Password
$env:CLIPBRIDGE_KEYSTORE=Join-Path $taskSigning 'release.jks'
if (!(Test-Path -LiteralPath $env:CLIPBRIDGE_KEYSTORE)) {
    & (Join-Path $JavaHome 'bin\keytool.exe') -genkeypair -keystore $env:CLIPBRIDGE_KEYSTORE -alias clipbridge -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Clipboard Bridge, O=Clipboard Bridge Contributors' -storepass:env CLIPBRIDGE_STORE_PASSWORD -keypass:env CLIPBRIDGE_STORE_PASSWORD
    if ($LASTEXITCODE -ne 0) { throw '无法生成持久签名密钥' }
}
try {
    & $Gradle -p (Join-Path $taskRoot 'android') assembleRelease --console=plain
    if ($LASTEXITCODE -ne 0) { throw 'Android 编译失败' }
} finally { Remove-Item Env:CLIPBRIDGE_STORE_PASSWORD -ErrorAction SilentlyContinue }
Copy-Item -LiteralPath (Join-Path $taskRoot 'android\app\build\outputs\apk\release\app-release.apk') -Destination (Join-Path $taskDist 'ClipboardBridge.apk') -Force
Copy-Item -LiteralPath $PSScriptRoot -Destination $taskDist -Recurse -Force
foreach ($taskNotice in @('LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskNotice) -Destination $taskDist -Force }
Write-Host "构建完成：$taskDist"
