#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('validate','check-access','submit')][string]$Mode='validate',
    [string]$PackageName='com.colin.clipbridge',
    [ValidateSet('production','internal','alpha','beta')][string]$Track='production',
    [string]$BundlePath,[string]$Sha256,[int]$VersionCode,[string]$ReleaseName,[string]$NotesPath,[string]$ListingPath,
    [switch]$ReplaceReview
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'GooglePlay.psm1') -Force
$client=$null
try{
    $listings=@()
    if($ListingPath){$listings=@(Read-PlayListingMetadata -Path $ListingPath -ReleaseName $ReleaseName -PackageName $PackageName)}
    if($ReleaseName -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -ge [version]'0.2.0' -and $listings.Count -eq 0){throw 'Releases from 0.2.0 require version-specific Play product descriptions.'}
    if($Mode -eq 'validate'){
        $null=Test-PlayBundle -Path $BundlePath -Sha256 $Sha256
        $result=@{state='artifact_validated';committed=$false;message='AAB structure and release checksum verified. No Google authentication, upload or review submission was performed.'}
    }else{
        $client=New-PlayHttpClient
        $token=Get-PlayAccessToken -Client $client -ServiceAccountJson $env:GOOGLE_PLAY_SERVICE_ACCOUNT_JSON
        if($Mode -eq 'check-access'){
            $releases=@(Get-PlayReleases -Client $client -Token $token -PackageName $PackageName -Track $Track)
            $result=@{state='access_verified';committed=$false;message='Authenticated read access verified. No app edit was created.';releaseStates=@($releases | ForEach-Object {$_.releaseLifecycleState})}
        }else{
            $notes=Get-Content -LiteralPath $NotesPath -Raw
            $result=Invoke-PlayPublish -Client $client -Token $token -PackageName $PackageName -Track $Track -BundlePath $BundlePath -Sha256 $Sha256 -VersionCode $VersionCode -ReleaseName $ReleaseName -Notes $notes.Trim() -Listings $listings -ReplaceReview:$ReplaceReview
        }
    }
    $result | ConvertTo-Json -Depth 5
    if($env:GITHUB_OUTPUT){"state=$($result.state)" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8}
    if($env:GITHUB_STEP_SUMMARY){
        "### Google Play: $($result.state)`n`n$($result.message)" | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
        if($listings.Count){"`nVersioned product descriptions: $((@($listings | ForEach-Object {$_.language})) -join ', '). These are committed only when the release edit is committed." | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8}
    }
    if($result.state -eq 'waiting_previous_review'){Write-Warning $result.message}
}catch{
    if($env:GITHUB_STEP_SUMMARY){
        "### Google Play: failed`n`nNo successful submission was confirmed. Check the failed step. If the commit request had an unknown network outcome, inspect the Play release state before retrying." | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
    throw
}finally{
    if($null -ne $client){$client.Dispose()}
    $token=$null
}
