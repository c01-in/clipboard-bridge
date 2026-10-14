param([ValidateSet('Text','Image')][string]$Mode='Text',[string]$ImagePath)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Run with Windows PowerShell -STA.' }
if ($Mode -eq 'Text') {
    $taskText='Clipboard Bridge automatic text verification'
    [Windows.Forms.Clipboard]::SetText($taskText)
    if ([Windows.Forms.Clipboard]::GetText() -ne $taskText) { throw 'Text clipboard verification failed' }
    'Windows native text clipboard: PASS'
} else {
    $taskImage=[Drawing.Image]::FromFile($ImagePath)
    try {
        [Windows.Forms.Clipboard]::SetImage($taskImage)
        $taskReadImage=[Windows.Forms.Clipboard]::GetImage()
        if (!$taskReadImage -or $taskReadImage.Width -ne $taskImage.Width -or $taskReadImage.Height -ne $taskImage.Height) { throw 'Image clipboard verification failed' }
        $taskReadImage.Dispose()
        'Windows native image clipboard: PASS'
    } finally { $taskImage.Dispose() }
}
