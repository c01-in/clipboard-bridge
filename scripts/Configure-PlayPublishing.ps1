#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Repository='c01-in/clipboard-bridge',
    [string]$ServiceAccountFile
)
$ErrorActionPreference='Stop'
if(!$IsWindows){throw 'This helper uses Windows DPAPI for the persistent local backup.'}
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Expected owner/repository.'}
Import-Module (Join-Path $PSScriptRoot 'GooglePlay.psm1') -Force
$repoRoot=Split-Path -Parent $PSScriptRoot
$vault=Join-Path $repoRoot 'local/play-publishing'
$encrypted=Join-Path $vault 'service-account.clixml'
$credential=$null;$client=$null;$oldToken=$env:GOOGLE_PLAY_ACCESS_TOKEN
function Set-PlaySecret([string]$Value){
    $start=[Diagnostics.ProcessStartInfo]::new('gh')
    foreach($argument in @('secret','set','GOOGLE_PLAY_SERVICE_ACCOUNT_JSON','--repo',$Repository)){$start.ArgumentList.Add($argument)}
    $start.UseShellExecute=$false;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start)
    try{
        $process.StandardInput.Write($Value);$process.StandardInput.Close()
        $out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
        $process.WaitForExit();[Threading.Tasks.Task]::WaitAll(@($out,$err))
        if($process.ExitCode -ne 0){throw 'Could not save the encrypted GitHub Play secret; check repository authorization.'}
    }finally{$process.Dispose()}
}
try{
    if($ServiceAccountFile){$credential=Get-Content -LiteralPath $ServiceAccountFile -Raw}
    elseif(Test-Path -LiteralPath $encrypted){$credential=[Net.NetworkCredential]::new('',(Import-Clixml -LiteralPath $encrypted)).Password}
    else{throw 'Provide a dedicated Google Play service-account key using -ServiceAccountFile. Never use Firebase sender credentials.'}
    # Do not accidentally repurpose this installation's old FCM sender identity.
    $legacy=Join-Path $repoRoot 'local/credentials/firebase-service-account.json'
    if(Test-Path -LiteralPath $legacy){
        try{
            $old=(Get-Content -LiteralPath $legacy -Raw | ConvertFrom-Json -AsHashtable)['client_email']
            $candidate=($credential | ConvertFrom-Json -AsHashtable)['client_email']
        }catch{throw 'Could not validate the dedicated publisher identity; credential contents were not printed.'}
        if($candidate -and $candidate -eq $old){throw 'Use a separate Play publisher service account, not the old Firebase sender.'}
    }
    $env:GOOGLE_PLAY_ACCESS_TOKEN=$null
    $client=New-PlayHttpClient
    $token=Get-PlayAccessToken -Client $client -ServiceAccountJson $credential
    $null=Get-PlayReleases -Client $client -Token $token -PackageName 'com.colin.clipbridge' -Track production
    New-Item -ItemType Directory -Path $vault -Force | Out-Null
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetOwner($identity)
    $rule=[Security.AccessControl.FileSystemAccessRule]::new($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $vault -AclObject $acl
    ConvertTo-SecureString -String $credential -AsPlainText -Force | Export-Clixml -LiteralPath $encrypted
    Set-PlaySecret $credential
    gh variable set GOOGLE_PLAY_UPLOAD_ENABLED --body true --repo $Repository
    if($LASTEXITCODE -ne 0){throw 'Publisher secret saved, but automatic uploads were not enabled.'}
    Write-Host 'Play read access verified; dedicated publisher saved in encrypted GitHub Secrets and local DPAPI backup. Automatic release uploads enabled.'
    Write-Host 'Read access does not prove production write permission; the first submission must also succeed. No AAB was uploaded by this helper.'
    Write-Host 'Future tags use CI. Existing v0.1.4 can be submitted from Publish to Google Play without rebuilding.'
}finally{
    if($null -ne $client){$client.Dispose()}
    $env:GOOGLE_PLAY_ACCESS_TOKEN=$oldToken
    $credential=$null;$token=$null
}
