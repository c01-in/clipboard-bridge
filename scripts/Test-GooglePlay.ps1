#Requires -Version 7.0
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'GooglePlay.psm1') -Force
# Fixtures exercise the real HTTP serialization and state machine without Google credentials.
# They are not evidence that Google accepted a release.
if(!('PlayFixtureHandler' -as [type])){Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
public sealed class PlayFixtureHandler : HttpMessageHandler {
    public Queue<HttpResponseMessage> Responses = new Queue<HttpResponseMessage>();
    public List<string> Methods = new List<string>();
    public List<string> Uris = new List<string>();
    public List<byte[]> Bodies = new List<byte[]>();
    public void Reply(string json, int code = 200) {
        Responses.Enqueue(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(json) });
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) {
        Methods.Add(r.Method.Method); Uris.Add(r.RequestUri.ToString());
        Bodies.Add(r.Content == null ? Array.Empty<byte>() : await r.Content.ReadAsByteArrayAsync(c));
        if(Responses.Count == 0) throw new InvalidOperationException("Unexpected fixture HTTP request.");
        return Responses.Dequeue();
    }
}
'@}
function Assert([bool]$Condition,[string]$Message){if(!$Condition){throw "FAILED: $Message"}}
function Expect-Failure([scriptblock]$Action,[string]$Pattern){
    $failure=$null
    try{& $Action | Out-Null}catch{$failure=$_.Exception.Message}
    Assert ($null -ne $failure -and $failure -match $Pattern) "Expected failure: $Pattern"
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('play-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$oldToken=$env:GOOGLE_PLAY_ACCESS_TOKEN
$env:GOOGLE_PLAY_ACCESS_TOKEN=$null
$count=0
try{
    $path=Join-Path $temp 'fixture.aab'
    $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
    try{foreach($name in @('BundleConfig.pb','base/manifest/AndroidManifest.xml','META-INF/TEST.RSA')){
        $stream=$zip.CreateEntry($name).Open();try{$stream.WriteByte(1)}finally{$stream.Dispose()}
    }}finally{$zip.Dispose()}
    $hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
    $bundleJson=@{versionCode=8;sha256=$hash} | ConvertTo-Json -Compress
    $argsBase=@{Token='fixture-access-token';PackageName='com.colin.clipbridge';Track='production';BundlePath=$path;Sha256=$hash;VersionCode=8;ReleaseName='v0.1.4';Notes='Test notes.'}
    function New-Fixture {
        $handler=[PlayFixtureHandler]::new()
        return @{Handler=$handler;Client=[Net.Http.HttpClient]::new($handler)}
    }
    function Add-Prefix($Fixture,[string]$Bundles='{}',[string]$Track='{}',[switch]$NoUpload){
        $Fixture.Handler.Reply('{}');$Fixture.Handler.Reply('{"id":"edit1"}');$Fixture.Handler.Reply($Bundles)
        if(!$NoUpload){$Fixture.Handler.Reply($bundleJson)}
        $Fixture.Handler.Reply($Track)
    }
    $fixture=New-Fixture
    try{
        Expect-Failure {Invoke-PlayPublish @argsBase -Client $fixture.Client -Sha256 ('0'*64)} 'checksum mismatch'
        Assert ($fixture.Handler.Methods.Count -eq 0) 'Bad checksum must not contact Google'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        $fixture.Handler.Reply('{"releases":[{"releaseLifecycleState":"RELEASE_LIFECYCLE_STATE_IN_REVIEW"}]}')
        $result=Invoke-PlayPublish @argsBase -Client $fixture.Client
        Assert ($result.state -eq 'waiting_previous_review' -and !$result.committed) 'Preserve existing review'
        Assert ($fixture.Handler.Methods.Count -eq 1 -and $fixture.Handler.Methods[0] -eq 'GET') 'Review preflight is read-only'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture
        $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}')
        $result=Invoke-PlayPublish @argsBase -Client $fixture.Client
        Assert ($result.state -eq 'submitted' -and $result.committed) 'Successful commit state'
        Assert (($fixture.Handler.Methods -join ',') -eq 'GET,POST,GET,POST,GET,PUT,POST,POST') 'Upload/track/validate/commit order'
        Assert ($fixture.Handler.Uris[7].EndsWith(':commit?changesInReviewBehavior=ERROR_IF_IN_REVIEW')) 'Commit protects concurrent review'
        Assert ([Convert]::ToBase64String($fixture.Handler.Bodies[3]) -eq [Convert]::ToBase64String([IO.File]::ReadAllBytes($path))) 'Upload bytes match verified bundle'
        $trackBody=[Text.Encoding]::UTF8.GetString($fixture.Handler.Bodies[5]) | ConvertFrom-Json
        Assert ($trackBody.track -eq 'production' -and $trackBody.releases[0].versionCodes[0] -eq '8' -and $trackBody.releases[0].status -eq 'completed') 'Expected release configuration'
        $count++
    }finally{$fixture.Client.Dispose()}
    foreach($badBundle in @(('{"versionCode":9,"sha256":"'+$hash+'"}'),('{"versionCode":8,"sha256":"'+('0'*64)+'"}'))){
        $fixture=New-Fixture
        try{
            $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{"id":"edit1"}');$fixture.Handler.Reply('{}');$fixture.Handler.Reply($badBundle);$fixture.Handler.Reply('{}')
            Expect-Failure {Invoke-PlayPublish @argsBase -Client $fixture.Client} 'version/hash did not match'
            Assert (($fixture.Handler.Methods -join ',') -eq 'GET,POST,GET,POST,DELETE') 'Bad uploaded artifact is never committed'
            $count++
        }finally{$fixture.Client.Dispose()}
    }
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture
        $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{"error":{}}',400);$fixture.Handler.Reply('{}')
        Expect-Failure {Invoke-PlayPublish @argsBase -Client $fixture.Client} 'HTTP 400'
        Assert (!($fixture.Handler.Uris -match ':commit') -and $fixture.Handler.Methods[-1] -eq 'DELETE') 'Failed validation cannot commit'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture
        $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}')
        $fixture.Handler.Reply('{"error":{"details":[{"reason":"CHANGES_ALREADY_IN_REVIEW"}]}}',400);$fixture.Handler.Reply('{}')
        $result=Invoke-PlayPublish @argsBase -Client $fixture.Client
        Assert ($result.state -eq 'waiting_previous_review' -and !$result.committed) 'Concurrent review returns waiting'
        Assert (!($fixture.Handler.Uris -match 'CANCEL_IN_REVIEW')) 'Never cancel review implicitly'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture
        $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}')
        $result=Invoke-PlayPublish @argsBase -Client $fixture.Client -ReplaceReview
        Assert ($result.committed -and $fixture.Handler.Uris[-1].EndsWith('changesInReviewBehavior=CANCEL_IN_REVIEW_AND_SUBMIT')) 'Explicit replacement only'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture -Bundles ('{"bundles":['+$bundleJson+']}') -NoUpload
        $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}')
        $result=Invoke-PlayPublish @argsBase -Client $fixture.Client
        Assert ($result.committed -and !($fixture.Handler.Uris -match '/upload/')) 'Existing identical bundle reused'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture -Bundles ('{"bundles":['+$bundleJson+']}') -NoUpload -Track '{"releases":[{"status":"completed","versionCodes":["8"]}]}'
        $fixture.Handler.Reply('{}')
        $result=Invoke-PlayPublish @argsBase -Client $fixture.Client
        Assert ($result.state -eq 'already_on_track' -and !$result.committed -and !($fixture.Handler.Uris -match ':commit')) 'Idempotent release does not resubmit'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture -Track '{"releases":[{"status":"completed","versionCodes":["9"]}]}'
        $fixture.Handler.Reply('{}')
        Expect-Failure {Invoke-PlayPublish @argsBase -Client $fixture.Client} 'newer version'
        Assert (!($fixture.Handler.Methods -contains 'PUT')) 'Never downgrade track'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        $fixture.Handler.Reply('{"error":{"message":"private-server-detail fixture-access-token"}}',403)
        try{Invoke-PlayPublish @argsBase -Client $fixture.Client;throw 'Expected HTTP failure'}catch{
            Assert ($_.Exception.Message -match 'HTTP 403' -and $_.Exception.Message -notmatch 'private-server-detail|fixture-access-token') 'API errors redact server content and token'
        }
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    try{
        Add-Prefix $fixture
        $fixture.Handler.Reply('{}');$fixture.Handler.Reply('{}');$fixture.Handler.Reply('{"error":{}}',500);$fixture.Handler.Reply('{}')
        Expect-Failure {Invoke-PlayPublish @argsBase -Client $fixture.Client} 'HTTP 500'
        Assert (@($fixture.Handler.Uris | Where-Object {$_ -match ':commit'}).Count -eq 1) 'Unknown commit outcome is never automatically retried'
        Assert (!($fixture.Handler.Uris -match 'CANCEL_IN_REVIEW')) 'Commit failure never falls back to replacing review'
        $count++
    }finally{$fixture.Client.Dispose()}
    $fixture=New-Fixture
    $rsa=[Security.Cryptography.RSA]::Create(2048)
    try{
        $fixture.Handler.Reply('{"access_token":"fixture-oauth-result"}')
        $account=@{type='service_account';client_email='test@example.iam.gserviceaccount.com';private_key=$rsa.ExportPkcs8PrivateKeyPem()} | ConvertTo-Json -Compress
        $token=Get-PlayAccessToken -Client $fixture.Client -ServiceAccountJson $account
        Assert ($token -eq 'fixture-oauth-result' -and $fixture.Handler.Uris[0] -eq 'https://oauth2.googleapis.com/token') 'Fixed OAuth audience endpoint'
        $form=[Text.Encoding]::UTF8.GetString($fixture.Handler.Bodies[0])
        $assertion=[Uri]::UnescapeDataString(($form -split '&assertion=')[1]);$parts=$assertion -split '\.'
        function Decode-Url([string]$Value){$Value=$Value.Replace('-','+').Replace('_','/');return [Convert]::FromBase64String($Value.PadRight(([int][Math]::Ceiling($Value.Length/4))*4,'='))}
        $claims=[Text.Encoding]::UTF8.GetString((Decode-Url $parts[1])) | ConvertFrom-Json
        Assert ($claims.scope -eq 'https://www.googleapis.com/auth/androidpublisher' -and $claims.exp-$claims.iat -eq 3600) 'Narrow Play scope, one-hour JWT'
        Assert ($rsa.VerifyData([Text.Encoding]::ASCII.GetBytes($parts[0]+'.'+$parts[1]),(Decode-Url $parts[2]),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) 'OAuth JWT signature verifies'
        $count++
        $fixture.Handler.Reply('{"error":"private-oauth-response"}',400)
        Expect-Failure {Get-PlayAccessToken -Client $fixture.Client -ServiceAccountJson $account} 'Google OAuth token exchange failed \(HTTP 400\); credentials were not printed'
        $count++
    }finally{$rsa.Dispose();$fixture.Client.Dispose();$account=$null}
    $fixture=New-Fixture
    try{
        Expect-Failure {Get-PlayAccessToken -Client $fixture.Client -ServiceAccountJson '{private-test-value'} 'Invalid service-account JSON'
        Assert ($fixture.Handler.Methods.Count -eq 0) 'Malformed key never sent'
        $count++
    }finally{$fixture.Client.Dispose()}
    Write-Host "$count Google Play publishing safeguard scenarios passed (local HTTP fixtures; no live Play upload)."
}finally{
    $env:GOOGLE_PLAY_ACCESS_TOKEN=$oldToken
    # Only individual files created by this script are removed; no recursive deletion.
    if(Test-Path -LiteralPath (Join-Path $temp 'fixture.aab')){Remove-Item -LiteralPath (Join-Path $temp 'fixture.aab') -Force}
    Remove-Item -LiteralPath $temp
}
