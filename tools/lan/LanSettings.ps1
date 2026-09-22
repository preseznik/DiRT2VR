function Read-LanSettings([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return [pscustomobject]@{Version=1;SkipIntroduction=$false} }
    $settings=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($settings.Version -ne 1 -or $settings.SkipIntroduction -isnot [bool]) {
        throw 'Unsupported LAN settings. Expected Version 1 and a true/false SkipIntroduction value.'
    }
    return $settings
}

function Show-LanSettings([string]$Path) {
    $settings=Read-LanSettings $Path
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [Windows.Forms.Application]::EnableVisualStyles()
    $form=New-Object Windows.Forms.Form
    $form.Text='DiRT2VR LAN settings'
    $form.StartPosition='CenterScreen'; $form.AutoScaleMode='Dpi'
    $form.ClientSize=New-Object Drawing.Size(520,210)
    $form.FormBorderStyle='FixedDialog'; $form.MaximizeBox=$false; $form.MinimizeBox=$false
    $skip=New-Object Windows.Forms.CheckBox
    $skip.Text='Skip introduction (experimental)'; $skip.AutoSize=$true
    $skip.Location=New-Object Drawing.Point(20,20); $skip.Checked=$settings.SkipIntroduction
    $help=New-Object Windows.Forms.Label
    $help.Location=New-Object Drawing.Point(20,55); $help.Size=New-Object Drawing.Size(480,90)
    $help.Text="Skip the first-run movie and forced career introduction.`r`nProfile creation remains available. Applies on the next LAN launch.`r`n`r`nTurning this off restores normal onboarding checks; it does not undo saved game progress."
    $save=New-Object Windows.Forms.Button
    $save.Text='Save'; $save.Location=New-Object Drawing.Point(310,165)
    $save.Size=New-Object Drawing.Size(85,28); $save.DialogResult='OK'
    $cancel=New-Object Windows.Forms.Button
    $cancel.Text='Cancel'; $cancel.Location=New-Object Drawing.Point(405,165)
    $cancel.Size=New-Object Drawing.Size(85,28); $cancel.DialogResult='Cancel'
    $form.Controls.AddRange(@($skip,$help,$save,$cancel)); $form.AcceptButton=$save; $form.CancelButton=$cancel
    try {
        if ($form.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
            [IO.Directory]::CreateDirectory((Split-Path $Path -Parent)) | Out-Null
            $json=@{Version=1;SkipIntroduction=$skip.Checked} | ConvertTo-Json
            Write-LanAtomic $Path ([Text.Encoding]::UTF8.GetBytes($json))
        }
    } finally { $form.Dispose() }
}
