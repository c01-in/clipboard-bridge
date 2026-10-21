#Requires -Version 7.0
[CmdletBinding()]
param([string]$PackageRoot=(Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'))
$ErrorActionPreference='Stop'
function Expect-Rejection([scriptblock]$Action,[string]$Message){
    $taskRejected=$false
    try{& $Action | Out-Null}catch{$taskRejected=$true}
    if(!$taskRejected){throw "Unsafe signing input accepted: $Message"}
    Write-Host "PASS: $Message"
}
$taskVerifier=Join-Path $PSScriptRoot 'Test-WindowsSignatures.ps1'
Expect-Rejection {& $taskVerifier -PackageRoot $PackageRoot -Publisher 'CN=Preview, OID.2.25.311729368913984317654407730594956997722=1'} 'Local preview identity cannot pass public-signing validation'
Expect-Rejection {& $taskVerifier -PackageRoot (Join-Path $PackageRoot 'missing-signing-output') -Publisher 'CN=SignPath Foundation'} 'Missing signing output cannot pass validation'
$taskBinary=Join-Path $PackageRoot 'windows/service/ClipboardBridge.Service.exe'
if(!(Test-Path -LiteralPath $taskBinary)){throw 'Build Windows apps before running the signing safeguards.'}
if((Get-AuthenticodeSignature -LiteralPath $taskBinary).Status -ne 'NotSigned'){throw 'Run these negative checks against the unsigned build-review output.'}
Expect-Rejection {& $taskVerifier -PackageRoot $PackageRoot -Publisher 'CN=SignPath Foundation'} 'An unsigned real app cannot masquerade as trusted SignPath output'
Write-Host '3 signing safeguard scenarios passed; no live signing request was made.'
