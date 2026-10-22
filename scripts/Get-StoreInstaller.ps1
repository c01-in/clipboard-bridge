#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
$taskIdentity=Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'windows/Store/identity.json') -Raw | ConvertFrom-Json
if($taskIdentity.storeId -cne '9PJT9PTSMCJQ'){throw 'Unexpected Microsoft Store product.'}
$taskOutput=[IO.Path]::GetFullPath($OutputPath)
$taskTemporary=$taskOutput+'.download'
New-Item -ItemType Directory -Path (Split-Path $taskOutput -Parent) -Force | Out-Null
try{
    $taskResponse=Invoke-WebRequest -Uri ('https://get.microsoft.com/installer/download/'+$taskIdentity.storeId+'?cid=website_cta_psi') -Headers @{'Accept-Language'='en-US'} -OutFile $taskTemporary -PassThru
    if($taskResponse.StatusCode -ne 200 -or ($taskResponse.Headers['Content-Disposition'] -join '') -notlike '*Clipboard Bridge Installer.exe*'){throw 'Microsoft did not return the Clipboard Bridge installer.'}
    $taskSize=(Get-Item -LiteralPath $taskTemporary).Length
    if($taskSize -lt 100KB -or $taskSize -gt 8MB){throw 'Unexpected Microsoft Store web-installer size.'}
    # Keep the .exe suffix for Windows Authenticode verification; the bytes are never modified.
    Move-Item -LiteralPath $taskTemporary -Destination $taskOutput -Force
    $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskOutput
    $taskPublisher='CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
    if($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -cne $taskPublisher -or !$taskSignature.TimeStamperCertificate){
        Remove-Item -LiteralPath $taskOutput -Force
        throw 'Microsoft Store installer does not have a valid, timestamped Microsoft signature.'
    }
    [pscustomobject]@{StoreId=$taskIdentity.storeId;Signature='Valid';Publisher=$taskPublisher;Bytes=$taskSize;SHA256=(Get-FileHash -LiteralPath $taskOutput).Hash;OnlineInstaller=$true}
}finally{
    if(Test-Path -LiteralPath $taskTemporary){Remove-Item -LiteralPath $taskTemporary -Force}
}
