#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageRoot,[ValidateSet('Assets','share\Assets')][string]$AssetPath='Assets')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskPackage=(Resolve-Path -LiteralPath $PackageRoot).Path
$taskAssets=Join-Path $taskPackage $AssetPath
New-Item -ItemType Directory -Path $taskAssets -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$taskIcon=[Drawing.Icon]::new((Join-Path $taskRoot 'windows/Tray/Assets/ClipboardBridge.ico'),256,256)
try{
    $taskSource=$taskIcon.ToBitmap()
    try{
        $taskImages=@{}
        foreach($taskSize in @(44,150,50)){
            $taskImages["Logo$taskSize.png"]=$taskSize
            foreach($taskScale in @(100,200,400)){$taskImages["Logo$taskSize.scale-$taskScale.png"]=[int]($taskSize*$taskScale/100)}
        }
        foreach($taskSize in @(16,20,24,30,32,36,40,48,60,64,72,80,96,256)){
            foreach($taskForm in @('','_altform-unplated','_altform-lightunplated')){$taskImages["Logo44.targetsize-$taskSize$taskForm.png"]=$taskSize}
        }
        foreach($taskImage in $taskImages.GetEnumerator()){
            $taskBitmap=[Drawing.Bitmap]::new([int]$taskImage.Value,[int]$taskImage.Value)
            $taskGraphics=[Drawing.Graphics]::FromImage($taskBitmap)
            try{
                $taskGraphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $taskGraphics.DrawImage($taskSource,0,0,[int]$taskImage.Value,[int]$taskImage.Value)
                $taskBitmap.Save((Join-Path $taskAssets $taskImage.Key),[Drawing.Imaging.ImageFormat]::Png)
            }finally{$taskGraphics.Dispose();$taskBitmap.Dispose()}
        }
    }finally{$taskSource.Dispose()}
}finally{$taskIcon.Dispose()}
$taskNuget=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
$taskMakePri=Join-Path $taskNuget 'microsoft.windows.sdk.buildtools/10.0.26100.9169/bin/10.0.26100.0/x64/makepri.exe'
$taskConfig=$taskPackage+'.pri.xml'
try{
    $taskResult=& $taskMakePri createconfig /cf $taskConfig /dq en-US /o 2>&1
    if($LASTEXITCODE -ne 0){throw ('Could not create Windows visual-resource configuration: '+(($taskResult -join "`n").Replace("`0",'')))}
    $taskResult=& $taskMakePri new /pr $taskPackage /cf $taskConfig /mn (Join-Path $taskPackage 'AppxManifest.xml') /of (Join-Path $taskPackage 'resources.pri') /o 2>&1
    if($LASTEXITCODE -ne 0){throw ('Could not index Windows visual resources: '+(($taskResult -join "`n").Replace("`0",'')))}
}finally{if(Test-Path -LiteralPath $taskConfig){Remove-Item -LiteralPath $taskConfig -Force}}
