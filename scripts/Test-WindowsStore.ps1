#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$ValidationRoot)
$ErrorActionPreference='Stop'
$taskMetadata=Get-Content -LiteralPath (Join-Path $PackageDirectory 'store-package.json') -Raw | ConvertFrom-Json
$taskStage=[IO.Path]::GetFullPath($taskMetadata.stage)
$taskManifest=[xml](Get-Content -LiteralPath (Join-Path $taskStage 'AppxManifest.xml') -Raw)
if($taskManifest.SelectSingleNode('//*[local-name()="AllowExternalContent"]') -or $taskManifest.SelectSingleNode('//*[local-name()="Service"]')){throw 'Store package must include its own application files and must not install an elevated system service.'}
if(!$taskManifest.SelectSingleNode('//*[local-name()="Extension" and @Category="windows.shareTarget"]') -or !$taskManifest.SelectSingleNode('//*[local-name()="StartupTask" and @Enabled="true"]')){throw 'Store package must retain Windows Share and login startup.'}
$taskCore=@(Get-ChildItem -LiteralPath $taskStage -Recurse -File -Filter coreclr.dll)
if($taskCore.Count -ne 1){throw 'Store package must contain exactly one shared CLR.'}
if(@(Get-ChildItem -LiteralPath $taskStage -Recurse -File -Filter Common.dll).Count -ne 1){throw 'Application dependencies must be deduplicated.'}
$taskArchive=[IO.Compression.ZipFile]::OpenRead($taskMetadata.package)
try{
    if(!$taskArchive.GetEntry('app/ClipboardBridge.exe') -or !$taskArchive.GetEntry('app/ClipboardBridge.Service.exe')){throw 'MSIX is missing application payload.'}
    if(!$taskArchive.GetEntry('android/ClipboardBridge.apk')){throw 'MSIX is missing its downloadable Android companion.'}
    if($taskArchive.Entries.FullName -match '(?i)(\.dpapi$|\.pfx$|\.jks$|\.clixml$|^local/|^\.git/)'){throw 'Private state or source-control internals found in MSIX.'}
}finally{$taskArchive.Dispose()}
if((Get-FileHash -LiteralPath $taskMetadata.package).Hash -ne $taskMetadata.sha256){throw 'MSIX checksum does not match its build record.'}
Write-Output 'PASS: full MSIX payload, sharing, login startup, one CLR, dependency deduplication and package privacy.'
$taskRoot=Join-Path ([IO.Path]::GetFullPath($ValidationRoot)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$taskTcp=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$taskTcp.Start();$taskHttpPort=$taskTcp.LocalEndpoint.Port;$taskTcp.Stop()
$taskUdp=[Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0));$taskUdpPort=$taskUdp.Client.LocalEndPoint.Port;$taskUdp.Dispose()
$taskExe=Join-Path $taskStage 'app/ClipboardBridge.Service.exe'
$taskStart=[Diagnostics.ProcessStartInfo]::new($taskExe)
$taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.WindowStyle='Hidden'
$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
$taskStart.Environment['CLIPBRIDGE_USER_MODE']='1'
$taskStart.Environment['CLIPBOARD_BRIDGE_DATA']=Join-Path $taskRoot 'state'
$taskStart.Environment['CLIPBOARD_BRIDGE_FILE_STAGING']=Join-Path $taskRoot 'files'
$taskStart.Environment['CLIPBRIDGE_HTTP_PORT']=[string]$taskHttpPort
$taskStart.Environment['CLIPBRIDGE_DISCOVERY_PORT']=[string]$taskUdpPort
$taskStart.Environment['DOTNET_ROOT']=Join-Path $taskRoot 'no-global-runtime'
$taskStart.Environment['DOTNET_ROOT_X64']=$taskStart.Environment['DOTNET_ROOT']
$taskStart.Environment['PATH']=$env:WINDIR+'\System32'
$taskHost=$null
try{
    $taskHost=[Diagnostics.Process]::Start($taskStart)
    $taskStdout=$taskHost.StandardOutput.ReadToEndAsync();$taskStderr=$taskHost.StandardError.ReadToEndAsync()
    $taskReady=$false
    for($taskAttempt=0;$taskAttempt -lt 40;$taskAttempt++){
        if($taskHost.HasExited){throw ('Store host exited before readiness: '+$taskStderr.GetAwaiter().GetResult())}
        try{$taskHealth=Invoke-RestMethod "http://127.0.0.1:$taskHttpPort/health" -TimeoutSec 1;if($taskHealth.service -eq 'ClipboardBridge'){$taskReady=$true;break}}catch {if($taskHost.HasExited){throw}}
        Start-Sleep -Milliseconds 100
    }
    if(!$taskReady){throw 'Per-user Store host did not become ready.'}
    $taskApk=Join-Path $taskStage 'android/ClipboardBridge.apk'
    $taskApkHead=Invoke-WebRequest "http://127.0.0.1:$taskHttpPort/app/android" -Method Head -TimeoutSec 10
    if($taskApkHead.StatusCode -ne 200 -or [long]($taskApkHead.Headers['Content-Length'] -join '') -ne (Get-Item -LiteralPath $taskApk).Length){throw 'Packaged Android download has an incorrect length.'}
    $taskApkDownload=Join-Path $taskRoot 'downloaded-companion.apk'
    Invoke-WebRequest "http://127.0.0.1:$taskHttpPort/app/android" -OutFile $taskApkDownload -TimeoutSec 30
    if((Get-FileHash -LiteralPath $taskApkDownload).Hash -ne (Get-FileHash -LiteralPath $taskApk).Hash){throw 'Packaged Android download is truncated or corrupted.'}
    Write-Output 'PASS: packaged Android companion downloads completely with the expected Content-Length and SHA-256.'
    $taskSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $taskPipe=[IO.Pipes.NamedPipeClientStream]::new('.','ClipboardBridge-user-'+$taskSid,[IO.Pipes.PipeDirection]::InOut)
    try{
        $taskPipe.Connect(3000)
        $taskBody=[Text.Encoding]::UTF8.GetBytes('{"op":"status"}')
        $taskPrefix=[BitConverter]::GetBytes([Net.IPAddress]::HostToNetworkOrder([int]$taskBody.Length))
        $taskPipe.Write($taskPrefix,0,$taskPrefix.Length);$taskPipe.Write($taskBody,0,$taskBody.Length);$taskPipe.Flush()
        $taskReplyPrefix=[byte[]]::new(4);$taskPipe.ReadExactly($taskReplyPrefix,0,4)
        $taskLength=[Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($taskReplyPrefix))
        if($taskLength -le 0 -or $taskLength -gt 32768){throw 'Invalid IPC status response.'}
        $taskReplyBytes=[byte[]]::new($taskLength);$taskPipe.ReadExactly($taskReplyBytes,0,$taskLength)
        $taskReply=[Text.Encoding]::UTF8.GetString($taskReplyBytes) | ConvertFrom-Json
        if(!$taskReply.ok){throw 'Per-user authenticated IPC failed.'}
    }finally{$taskPipe.Dispose()}
    $taskStateFile=Join-Path $taskRoot 'state/configuration.dpapi'
    $taskState=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskStateFile),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $taskConfig=[Text.Encoding]::UTF8.GetString($taskState) | ConvertFrom-Json
    if($taskConfig.ownerSid -ne $taskSid -or !$taskConfig.computerId){throw 'Store host did not initialize its own per-user identity.'}
    Write-Output 'PASS: real per-user HTTP host and owner IPC start without a Windows service or global dotnet; encrypted per-user state initializes.'
    @{http=$true;ownerIpc=$true;sharedRuntime=$true;applicationCount=3;configurationInitialized=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'result.json') -Encoding utf8NoBOM
}finally{
    if($taskHost){if(!$taskHost.HasExited){$taskHost.Kill();$taskHost.WaitForExit()};$taskHost.Dispose()}
}
