#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot,[Parameter(Mandatory)][string]$Publisher,[switch]$InstallerOnly)
$ErrorActionPreference='Stop'
if($Publisher -notmatch '^CN=' -or $Publisher -match 'OID\.2\.25\.311729368913984317654407730594956997722'){throw 'A public signing publisher is required.'}
$taskRoot=[IO.Path]::GetFullPath($PackageRoot)
$taskFiles=if($InstallerOnly){@('ClipboardBridge-Windows-x64-Setup.exe')}else{@(
    'windows/service/ClipboardBridge.Service.exe','windows/service/ClipboardBridge.Service.dll',
    'windows/tray/ClipboardBridge.exe','windows/tray/ClipboardBridge.dll',
    'windows/cli/ClipboardBridge.Cli.exe','windows/cli/ClipboardBridge.Cli.dll',
    'windows/share/ClipboardBridge.Share.msix'
)}
foreach($taskRelative in $taskFiles){
    $taskPath=Join-Path $taskRoot $taskRelative
    if(!(Test-Path -LiteralPath $taskPath -PathType Leaf)){throw "Missing signed artifact: $taskRelative"}
    $taskSignature=Get-AuthenticodeSignature -LiteralPath $taskPath
    if($taskSignature.Status -ne 'Valid' -or $null -eq $taskSignature.SignerCertificate){throw "Invalid or missing trusted signature: $taskRelative ($($taskSignature.Status))"}
    if($taskSignature.SignerCertificate.Subject -cne $Publisher){throw "Unexpected signing publisher: $taskRelative"}
    if($null -eq $taskSignature.TimeStamperCertificate){throw "Missing signing timestamp: $taskRelative"}
}
if(!$InstallerOnly){
    $taskManifest=[xml](Get-Content -LiteralPath (Join-Path $taskRoot 'windows/share/AppxManifest.xml') -Raw)
    if($taskManifest.Package.Identity.Publisher -cne $Publisher){throw 'Windows Share manifest publisher does not match the signing certificate.'}
    $taskArchive=[IO.Compression.ZipFile]::OpenRead((Join-Path $taskRoot 'windows/share/ClipboardBridge.Share.msix'))
    try{
        $taskEntry=$taskArchive.GetEntry('AppxManifest.xml')
        if(!$taskEntry){throw 'Signed MSIX has no manifest.'}
        $taskReader=[IO.StreamReader]::new($taskEntry.Open())
        try{$taskEmbedded=[xml]$taskReader.ReadToEnd()}finally{$taskReader.Dispose()}
        if($taskEmbedded.Package.Identity.Publisher -cne $Publisher){throw 'Signed MSIX publisher does not match the expected certificate.'}
    }finally{$taskArchive.Dispose()}
}
Write-Host "PASS: trusted publisher and timestamp verified for $($taskFiles.Count) Windows artifacts."
