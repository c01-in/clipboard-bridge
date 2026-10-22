#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Download and installation verification run only on disposable GitHub Actions runners.'}
Import-Module Appx -UseWindowsPowerShell
if($Tag -notmatch '^v0\.\d+\.\d+$'){throw 'Expected an existing preview release tag.'}
$taskVersion=$Tag.Substring(1)+'.0'
$taskIdentity=Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'windows/Store/identity.json') -Raw | ConvertFrom-Json
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
if(!$taskOutput.StartsWith([IO.Path]::GetFullPath($env:RUNNER_TEMP)+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Use disposable runner storage.'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& (Join-Path $PSScriptRoot 'Get-StoreOfflinePackage.ps1') -Version $taskVersion -OutputDirectory $taskOutput
$taskCandidates=@(Get-ChildItem -LiteralPath $taskOutput -Recurse -File | Where-Object {$_.Extension -in @('.msix','.appx')})
$taskMatches=@()
foreach($taskCandidate in $taskCandidates){
    $taskArchive=[IO.Compression.ZipFile]::OpenRead($taskCandidate.FullName)
    try{
        $taskEntry=$taskArchive.GetEntry('AppxManifest.xml')
        if(!$taskEntry){continue}
        $taskReader=[IO.StreamReader]::new($taskEntry.Open())
        try{$taskManifest=[xml]$taskReader.ReadToEnd()}finally{$taskReader.Dispose()}
        if($taskManifest.Package.Identity.Name -ceq $taskIdentity.name){$taskMatches+=@{File=$taskCandidate;Manifest=$taskManifest}}
    }finally{$taskArchive.Dispose()}
}
if($taskMatches.Count -ne 1){throw 'Expected exactly one standalone full package for this Store identity.'}
$taskSelected=$taskMatches[0]
if($taskSelected.Manifest.Package.Identity.Publisher -cne $taskIdentity.publisher -or $taskSelected.Manifest.Package.Identity.Version -cne $taskVersion -or $taskSelected.Manifest.Package.Identity.ProcessorArchitecture -cne 'x64'){throw 'Store package publisher, architecture or version does not match this release.'}
$taskPackage=Join-Path $taskOutput 'ClipboardBridge-Windows-x64.msix'
if($taskSelected.File.FullName -cne $taskPackage){Copy-Item -LiteralPath $taskSelected.File.FullName -Destination $taskPackage}
$taskSignature=Get-AuthenticodeSignature -LiteralPath $taskPackage
if($taskSignature.Status -ne 'Valid' -or !$taskSignature.SignerCertificate){throw 'The downloaded MSIX does not have a Windows-trusted signature.'}
$taskChain=[Security.Cryptography.X509Certificates.X509Chain]::new()
try{
    if(!$taskChain.Build($taskSignature.SignerCertificate)){throw 'Store signing certificate chain validation failed.'}
    if(!(@($taskChain.ChainElements | ForEach-Object {$_.Certificate.Subject}) -match 'Microsoft')){throw 'Expected a Microsoft Store signing certificate chain.'}
}finally{$taskChain.Dispose()}
$taskArchive=[IO.Compression.ZipFile]::OpenRead($taskPackage)
try{
    foreach($taskRequired in @('AppxSignature.p7x','app/ClipboardBridge.exe','app/ClipboardBridge.Service.exe','app/ClipboardBridge.Cli.exe','android/ClipboardBridge.apk')){
        if(!$taskArchive.GetEntry($taskRequired)){throw "Full package is missing $taskRequired"}
    }
    if(@($taskArchive.Entries | Where-Object {$_.FullName -match '(^|/)coreclr\.dll$'}).Count -ne 1){throw 'Full package must contain exactly one shared runtime.'}
}finally{$taskArchive.Dispose()}
Write-Host 'PASS: exact Store identity/version, Windows-trusted Microsoft certificate chain, full application payload and one bundled runtime.'
$taskExisting=Get-AppxPackage -Name $taskIdentity.name
if($taskExisting){throw 'Fresh install verification requires a runner without an existing installation.'}
# Use the operating system installer. Never enable Developer Mode, import a certificate,
# use AllowUnsigned or repackage/resign the Store bytes to make this test pass.
$taskCommand="`$ErrorActionPreference='Stop'; Add-AppxPackage -Path '"+$taskPackage.Replace("'","''")+"'"
$taskAdapters=@(Get-NetAdapter | Where-Object Status -eq 'Up')
if(!$taskAdapters){throw 'Expected a disposable runner network adapter for the offline installation test.'}
$taskDevModePath='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
$taskDevMode=(Get-ItemProperty -LiteralPath $taskDevModePath -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
$taskInstalled=$null
$taskHost=$null
try{
    if($taskDevMode -eq 1){Set-ItemProperty -LiteralPath $taskDevModePath -Name AllowDevelopmentWithoutDevLicense -Value 0}
    $taskAdapters | Disable-NetAdapter -Confirm:$false
    if(Get-NetAdapter | Where-Object Status -eq 'Up'){throw 'Runner networking is still active; offline installation has not been proven.'}
    $taskInstall=Start-Process powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-EncodedCommand',[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($taskCommand))) -PassThru -WindowStyle Hidden
    try{
        if(!$taskInstall.WaitForExit(45000)){$taskInstall.Kill($true);throw 'Offline MSIX installation timed out.'}
        if($taskInstall.ExitCode -ne 0){throw 'Unmodified Store package could not be installed offline with normal Windows trust.'}
    }finally{$taskInstall.Dispose()}
    $taskInstalled=Get-AppxPackage -Name $taskIdentity.name
    if(!$taskInstalled -or [string]$taskInstalled.Version -cne $taskVersion -or $taskInstalled.SignatureKind -ne 'Store' -or $taskInstalled.Status -ne 'Ok'){throw 'Installed package identity, Store signature or status is invalid.'}
    $taskStart=[Diagnostics.ProcessStartInfo]::new((Join-Path $taskInstalled.InstallLocation 'app/ClipboardBridge.Service.exe'))
    $taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
    $taskStart.Environment['CLIPBRIDGE_USER_MODE']='1'
    $taskStart.Environment['CLIPBOARD_BRIDGE_DATA']=Join-Path $taskOutput 'test-state'
    $taskStart.Environment['CLIPBOARD_BRIDGE_FILE_STAGING']=Join-Path $taskOutput 'test-files'
    $taskStart.Environment['DOTNET_ROOT']=Join-Path $taskOutput 'no-global-runtime'
    $taskStart.Environment['DOTNET_ROOT_X64']=$taskStart.Environment['DOTNET_ROOT']
    $taskStart.Environment['PATH']=$env:WINDIR+'\System32'
    $taskHost=[Diagnostics.Process]::Start($taskStart)
    $taskStdout=$taskHost.StandardOutput.ReadToEndAsync();$taskStderr=$taskHost.StandardError.ReadToEndAsync()
    $taskReady=$false
    for($taskAttempt=0;$taskAttempt -lt 50;$taskAttempt++){
        if($taskHost.HasExited){throw ('Installed host exited: '+$taskStderr.GetAwaiter().GetResult())}
        try{$taskHealth=Invoke-RestMethod http://127.0.0.1:45871/health -TimeoutSec 1;if($taskHealth.service -eq 'ClipboardBridge'){$taskReady=$true;break}}catch{}
        Start-Sleep -Milliseconds 200
    }
    if(!$taskReady){throw 'Installed full package did not start its real HTTP host.'}
    $taskHead=Invoke-WebRequest http://127.0.0.1:45871/app/android -Method Head -TimeoutSec 10
    $taskApk=Join-Path $taskInstalled.InstallLocation 'android/ClipboardBridge.apk'
    if($taskHead.StatusCode -ne 200 -or [long]($taskHead.Headers['Content-Length'] -join '') -ne (Get-Item $taskApk).Length){throw 'Installed companion download has an incorrect length.'}
    @{version=$taskVersion;packageFamilyName=$taskInstalled.PackageFamilyName;signatureStatus=[string]$taskSignature.Status;signatureKind=[string]$taskInstalled.SignatureKind;signer=$taskSignature.SignerCertificate.Subject;sha256=(Get-FileHash $taskPackage).Hash.ToLowerInvariant();sizeBytes=(Get-Item $taskPackage).Length;freshInstall=$true;hostReady=$true;bundledRuntime=$true;companionDownload=$true;networkIsolatedInstall=$true;developerMode=$false} | ConvertTo-Json | Set-Content (Join-Path $taskOutput 'verification.json') -Encoding utf8NoBOM
    'PASS: trusted full MSIX installs and its real host runs while all runner network adapters are disabled, with Developer Mode disabled and no global .NET runtime.' | Tee-Object -Variable taskResult
    $taskResult | Out-File $env:GITHUB_STEP_SUMMARY -Append
}finally{
    $taskAdapters | Enable-NetAdapter -Confirm:$false
    if($taskDevMode -eq 1){Set-ItemProperty -LiteralPath $taskDevModePath -Name AllowDevelopmentWithoutDevLicense -Value 1}
    if($taskHost){if(!$taskHost.HasExited){$taskHost.Kill();$taskHost.WaitForExit()};$taskHost.Dispose()}
    if($taskInstalled){
        $taskRemove="`$ErrorActionPreference='Stop'; Remove-AppxPackage -Package '"+$taskInstalled.PackageFullName.Replace("'","''")+"'"
        & powershell.exe -NoProfile -NonInteractive -Command $taskRemove
        if($LASTEXITCODE -ne 0){throw 'Test package uninstall failed.'}
    }
}
