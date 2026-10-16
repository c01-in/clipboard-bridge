#Requires -Version 7.0
[CmdletBinding()]
param([string]$Repository='c01-in/clipboard-bridge')
$ErrorActionPreference='Stop'
$taskSigning=Join-Path (Split-Path -Parent $PSScriptRoot) 'local\signing'
$taskKeystore=Join-Path $taskSigning 'release.jks'
$taskPasswordFile=Join-Path $taskSigning 'password.clixml'
if(!(Test-Path -LiteralPath $taskKeystore) -or !(Test-Path -LiteralPath $taskPasswordFile)){throw 'Build once locally with your persistent Android signing key first.'}
function Set-SigningSecret([string]$Name,[string]$Value){
    $taskStart=[Diagnostics.ProcessStartInfo]::new('gh')
    foreach($taskArgument in @('secret','set',$Name,'--repo',$Repository)){$taskStart.ArgumentList.Add($taskArgument)}
    $taskStart.UseShellExecute=$false;$taskStart.RedirectStandardInput=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
    $taskProcess=[Diagnostics.Process]::Start($taskStart)
    try{
        # Write without a trailing newline; never put the keystore or password in process arguments.
        $taskProcess.StandardInput.Write($Value);$taskProcess.StandardInput.Close()
        $taskOutput=$taskProcess.StandardOutput.ReadToEndAsync();$taskError=$taskProcess.StandardError.ReadToEndAsync()
        $taskProcess.WaitForExit();[Threading.Tasks.Task]::WaitAll(@($taskOutput,$taskError))
        if($taskProcess.ExitCode -ne 0){throw "Could not save encrypted GitHub secret $Name; check repository authorization."}
    }finally{$taskProcess.Dispose()}
    Write-Host "Saved encrypted repository secret: $Name"
}
$taskPassword=Import-Clixml -LiteralPath $taskPasswordFile
Set-SigningSecret 'ANDROID_KEYSTORE_BASE64' ([Convert]::ToBase64String([IO.File]::ReadAllBytes($taskKeystore)))
Set-SigningSecret 'ANDROID_KEYSTORE_PASSWORD' ([Net.NetworkCredential]::new('',$taskPassword).Password)
Write-Host 'Firebase credentials and runtime pairing data were not uploaded.'
