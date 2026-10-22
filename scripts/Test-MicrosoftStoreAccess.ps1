#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
foreach($taskName in @('MS_STORE_TENANT_ID','MS_STORE_CLIENT_ID','MS_STORE_CLIENT_SECRET')){
    if(![Environment]::GetEnvironmentVariable($taskName)){throw "Microsoft Store authentication is not configured: $taskName"}
}
foreach($taskValue in @($env:MS_STORE_TENANT_ID,$env:MS_STORE_CLIENT_ID)){
    $taskGuid=[Guid]::Empty
    if(![Guid]::TryParse($taskValue,[ref]$taskGuid)){throw 'Invalid Microsoft Store application or tenant identifier.'}
}
$taskIdentity=Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'windows/Store/identity.json') -Raw | ConvertFrom-Json
$taskToken=Invoke-RestMethod -Method Post -Uri ('https://login.microsoftonline.com/'+$env:MS_STORE_TENANT_ID+'/oauth2/token') -Body @{
    grant_type='client_credentials';client_id=$env:MS_STORE_CLIENT_ID;client_secret=$env:MS_STORE_CLIENT_SECRET;resource='https://manage.devcenter.microsoft.com'
} -TimeoutSec 30
try{
    $taskApp=Invoke-RestMethod -Uri ('https://manage.devcenter.microsoft.com/v1.0/my/applications/'+$taskIdentity.storeId) -Headers @{Authorization='Bearer '+$taskToken.access_token} -TimeoutSec 30
    if($taskApp.id -ne $taskIdentity.storeId){throw 'The service principal cannot read the expected Store application.'}
    Write-Output ('PASS: real OAuth and read-only Microsoft Store API access to '+$taskIdentity.storeId+'. No submission changed.')
}finally{$taskToken=$null}
