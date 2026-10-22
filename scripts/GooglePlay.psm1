#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:PlayRoot='https://androidpublisher.googleapis.com/androidpublisher/v3'

function New-PlayHttpClient {
    $handler=[Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect=$false
    $client=[Net.Http.HttpClient]::new($handler)
    $client.Timeout=[TimeSpan]::FromMinutes(3)
    return $client
}

function Invoke-PlayRequest {
    param([Net.Http.HttpClient]$Client,[string]$Token,[string]$Method,[string]$Uri,[object]$Body,[byte[]]$Bytes)
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),$Uri)
    try {
        $request.Headers.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$Token)
        if($null -ne $Bytes){
            $request.Content=[Net.Http.ByteArrayContent]::new($Bytes)
            $request.Content.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
        }elseif($null -ne $Body){
            $request.Content=[Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 20 -Compress),[Text.Encoding]::UTF8,'application/json')
        }
        $response=$Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if(!$response.IsSuccessStatusCode){
                $reason='';try{$errorBody=$text | ConvertFrom-Json -AsHashtable;foreach($detail in @($errorBody.error.details)){if($detail.reason -match '^[A-Z_]+$'){$reason=$detail.reason;break}}}catch{}
                # Do not print response bodies, identities, request payloads or bearer tokens.
                $exception=[InvalidOperationException]::new("Google Play API HTTP $([int]$response.StatusCode) ($Method); reason=$reason. Check API enablement and app-specific Play permissions for 403 errors.")
                $exception.Data['PlayReason']=$reason;$exception.Data['HttpStatus']=[int]$response.StatusCode
                throw $exception
            }
            if([string]::IsNullOrWhiteSpace($text)){return @{}}
            try{return $text | ConvertFrom-Json -AsHashtable}catch{throw 'Google Play returned malformed JSON; response content was not printed.'}
        }finally{$response.Dispose()}
    }finally{$request.Dispose()}
}

function Get-PlayAccessToken {
    param([Net.Http.HttpClient]$Client,[string]$ServiceAccountJson)
    if($env:GOOGLE_PLAY_ACCESS_TOKEN){return $env:GOOGLE_PLAY_ACCESS_TOKEN}
    if([string]::IsNullOrWhiteSpace($ServiceAccountJson)){throw 'Google Play authentication is missing. Configure workload identity or a dedicated Play service-account secret.'}
    try{$account=$ServiceAccountJson | ConvertFrom-Json -AsHashtable}catch{throw 'Invalid service-account JSON; credentials were not printed.'}
    if($account['type'] -ne 'service_account' -or $account['client_email'] -notmatch '^[^\s@]+@[^\s@]+\.iam\.gserviceaccount\.com$' -or !$account['private_key']){throw 'Expected a Google service-account JSON key; credentials were not printed.'}
    function Base64Url([byte[]]$Value){return [Convert]::ToBase64String($Value).TrimEnd('=').Replace('+','-').Replace('/','_')}
    $now=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header=Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"RS256","typ":"JWT"}'))
    $payload=Base64Url ([Text.Encoding]::UTF8.GetBytes((@{iss=$account.client_email;scope='https://www.googleapis.com/auth/androidpublisher';aud='https://oauth2.googleapis.com/token';iat=$now;exp=$now+3600} | ConvertTo-Json -Compress)))
    $rsa=[Security.Cryptography.RSA]::Create()
    try{$rsa.ImportFromPem($account.private_key);$signature=Base64Url ($rsa.SignData([Text.Encoding]::ASCII.GetBytes("$header.$payload"),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))}catch{throw 'Could not sign the OAuth assertion; credentials were not printed.'}finally{$rsa.Dispose()}
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,'https://oauth2.googleapis.com/token')
    try{
        $values=[Collections.Generic.Dictionary[string,string]]::new()
        $values.Add('grant_type','urn:ietf:params:oauth:grant-type:jwt-bearer');$values.Add('assertion',"$header.$payload.$signature")
        $request.Content=[Net.Http.FormUrlEncodedContent]::new($values)
        $response=$Client.SendAsync($request).GetAwaiter().GetResult()
        try{
            if(!$response.IsSuccessStatusCode){throw "Google OAuth token exchange failed (HTTP $([int]$response.StatusCode)); credentials were not printed."}
            try{$result=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -AsHashtable}catch{throw 'Google OAuth returned malformed JSON; response content was not printed.'}
            if(!$result['access_token']){throw 'Google OAuth did not return an access token.'}
            return $result.access_token
        }finally{$response.Dispose()}
    }finally{$request.Dispose();$account=$null;$signature=$null}
}

function Test-PlayBundle {
    param([string]$Path,[string]$Sha256)
    if($Sha256 -notmatch '^[0-9a-fA-F]{64}$'){throw 'A release SHA-256 checksum is required.'}
    if(!(Test-Path -LiteralPath $Path -PathType Leaf)){throw 'The Android App Bundle is missing.'}
    $actual=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if($actual -ne $Sha256.ToLowerInvariant()){throw 'AAB checksum mismatch; nothing was uploaded.'}
    $zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    try{
        $names=@($zip.Entries.FullName)
        if('BundleConfig.pb' -notin $names -or 'base/manifest/AndroidManifest.xml' -notin $names -or !($names -match '^META-INF/[^/]+\.(RSA|EC|DSA)$')){throw 'Expected a signed Android App Bundle.'}
    }finally{$zip.Dispose()}
    return $actual
}

function Get-PlayReleases {
    param([Net.Http.HttpClient]$Client,[string]$Token,[string]$PackageName,[string]$Track)
    $result=Invoke-PlayRequest -Client $Client -Token $Token -Method GET -Uri "$script:PlayRoot/applications/$PackageName/tracks/$Track/releases"
    return @($result['releases'] | Where-Object {$null -ne $_})
}

function Test-PlayListings {
    param([object[]]$Listings)
    $languages=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($listing in $Listings){
        if($listing -isnot [Collections.IDictionary]){throw 'Play listing must be a JSON object.'}
        if(@($listing.Keys | Where-Object {$_ -notin @('language','title','shortDescription','fullDescription')}).Count){throw 'Unexpected Play listing field.'}
        if($listing['language'] -isnot [string] -or $listing.language -notmatch '^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$' -or !$languages.Add($listing.language)){throw 'Invalid or duplicate Play listing language.'}
        foreach($field in @(@('title',30),@('shortDescription',80),@('fullDescription',4000))){
            $value=$listing[$field[0]]
            if($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $field[1]){throw "Play listing $($field[0]) must contain 1 to $($field[1]) characters."}
        }
    }
}

function Read-PlayListingMetadata {
    param([string]$Path,[string]$ReleaseName,[string]$PackageName='com.colin.clipbridge')
    if(!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -gt 65536){throw 'Version-specific Play listing metadata is missing or too large.'}
    try{$metadata=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable}catch{throw 'Invalid Play listing metadata JSON.'}
    if($metadata -isnot [Collections.IDictionary] -or $metadata['schemaVersion'] -ne 1 -or $metadata['packageName'] -ne $PackageName -or ('v'+$metadata['version']) -ne $ReleaseName){throw 'Play listing metadata must match the release tag and Android package.'}
    if($metadata['listings'] -isnot [array] -or $metadata.listings.Count -eq 0){throw 'At least one version-specific Play listing is required.'}
    Test-PlayListings -Listings $metadata.listings
    if('en-US' -notin @($metadata.listings | ForEach-Object {$_.language})){throw 'English (en-US) Play listing is required.'}
    return $metadata.listings
}

function Invoke-PlayPublish {
    param([Net.Http.HttpClient]$Client,[string]$Token,[string]$PackageName,[string]$Track,[string]$BundlePath,[string]$Sha256,[int]$VersionCode,[string]$ReleaseName,[string]$Notes,[object[]]$Listings=@(),[switch]$ReplaceReview)
    if($PackageName -notmatch '^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$' -or $Track -notmatch '^[A-Za-z0-9_-]+$' -or $VersionCode -lt 1){throw 'Invalid Play package, track or version code.'}
    if([string]::IsNullOrWhiteSpace($Notes) -or $Notes.Length -gt 500){throw 'Release notes must contain 1 to 500 characters.'}
    Test-PlayListings -Listings $Listings
    if($ReleaseName -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -ge [version]'0.2.0' -and $Listings.Count -eq 0){throw 'Releases from 0.2.0 require version-specific Play product descriptions.'}
    $hash=Test-PlayBundle -Path $BundlePath -Sha256 $Sha256
    $releases=@(Get-PlayReleases -Client $Client -Token $Token -PackageName $PackageName -Track $Track)
    if(!$ReplaceReview -and @($releases | Where-Object {$_.releaseLifecycleState -eq 'RELEASE_LIFECYCLE_STATE_IN_REVIEW'}).Count){
        return @{state='waiting_previous_review';versionCode=$VersionCode;committed=$false;message='Previous review kept. No edit was created and no bundle uploaded.'}
    }
    $edit=$null;$committed=$false
    try{
        $edit=Invoke-PlayRequest -Client $Client -Token $Token -Method POST -Uri "$script:PlayRoot/applications/$PackageName/edits" -Body @{}
        if($edit.id -notmatch '^[A-Za-z0-9_-]+$'){throw 'Google Play returned an invalid edit identifier.'}
        $root="$script:PlayRoot/applications/$PackageName/edits/$($edit.id)"
        $existing=Invoke-PlayRequest -Client $Client -Token $Token -Method GET -Uri "$root/bundles"
        $bundle=@($existing['bundles'] | Where-Object {$null -ne $_ -and [int]$_.versionCode -eq $VersionCode})
        if($bundle.Count -gt 1){throw 'Google Play returned duplicate bundle version codes.'}
        if($bundle.Count -eq 0){
            $uploaded=Invoke-PlayRequest -Client $Client -Token $Token -Method POST -Uri "https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/$PackageName/edits/$($edit.id)/bundles?uploadType=media" -Bytes ([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $BundlePath).Path))
        }else{$uploaded=$bundle[0]}
        if([int]$uploaded.versionCode -ne $VersionCode -or $uploaded.sha256 -ne $hash){throw 'Google Play bundle version/hash did not match the verified release. Nothing was committed.'}
        $current=Invoke-PlayRequest -Client $Client -Token $Token -Method GET -Uri "$root/tracks/$Track"
        $currentReleases=@($current['releases'] | Where-Object {$null -ne $_})
        if(@($currentReleases | Where-Object {$_.status -ne 'draft' -and @($_.versionCodes | Where-Object {[long]$_ -gt $VersionCode}).Count}).Count){throw 'A newer version is already on this track; refusing to replace it.'}
        if(@($currentReleases | Where-Object {$_.status -eq 'completed' -and @($_.versionCodes).Count -eq 1 -and [int]$_.versionCodes[0] -eq $VersionCode}).Count){
            return @{state='already_on_track';versionCode=$VersionCode;committed=$false;message='Identical bundle already configured on this track. Review/public availability not inferred.'}
        }
        foreach($listing in $Listings){
            $listingUri="$root/listings/$($listing.language)"
            try{$oldListing=Invoke-PlayRequest -Client $Client -Token $Token -Method GET -Uri $listingUri}
            catch{if($_.Exception.Data['HttpStatus'] -eq 404){$oldListing=@{}}else{throw}}
            $body=@{language=$listing.language;title=$listing.title;shortDescription=$listing.shortDescription;fullDescription=$listing.fullDescription}
            # Preserve an existing promotional video. Screenshots and other locales are separate resources.
            if($oldListing.ContainsKey('video')){$body.video=$oldListing.video}
            $null=Invoke-PlayRequest -Client $Client -Token $Token -Method PUT -Uri $listingUri -Body $body
        }
        $release=@{name=$ReleaseName;versionCodes=@([string]$VersionCode);status='completed';releaseNotes=@(@{language='en-US';text=$Notes})}
        $null=Invoke-PlayRequest -Client $Client -Token $Token -Method PUT -Uri "$root/tracks/$Track" -Body @{track=$Track;releases=@($release)}
        $null=Invoke-PlayRequest -Client $Client -Token $Token -Method POST -Uri "${root}:validate"
        $behavior=if($ReplaceReview){'CANCEL_IN_REVIEW_AND_SUBMIT'}else{'ERROR_IF_IN_REVIEW'}
        try{$null=Invoke-PlayRequest -Client $Client -Token $Token -Method POST -Uri "${root}:commit?changesInReviewBehavior=$behavior"}
        catch{
            if(!$ReplaceReview -and $_.Exception.Data['PlayReason'] -eq 'CHANGES_ALREADY_IN_REVIEW'){
                return @{state='waiting_previous_review';versionCode=$VersionCode;committed=$false;message='Google detected another review at commit time. Existing review was not replaced.'}
            }
            throw
        }
        $committed=$true
        $submitted=if($Listings.Count){'Google Play accepted the AAB, product descriptions and release notes in one edit.'}else{'Google Play accepted the AAB and release notes. Product descriptions were unchanged for this legacy release.'}
        return @{state='submitted';versionCode=$VersionCode;committed=$true;listingLanguages=@($Listings | ForEach-Object {$_.language});message="$submitted This is not proof of review approval or public availability."}
    }finally{
        if($null -ne $edit -and !$committed -and $edit.id -match '^[A-Za-z0-9_-]+$'){
            try{$null=Invoke-PlayRequest -Client $Client -Token $Token -Method DELETE -Uri "$script:PlayRoot/applications/$PackageName/edits/$($edit.id)"}catch{Write-Warning 'Could not clean up the uncommitted edit; it will expire. No successful commit was confirmed.'}
        }
    }
}

Export-ModuleMember -Function New-PlayHttpClient,Get-PlayAccessToken,Test-PlayBundle,Get-PlayReleases,Read-PlayListingMetadata,Invoke-PlayPublish
