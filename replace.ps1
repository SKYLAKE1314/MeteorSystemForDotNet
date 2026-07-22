$files = Get-ChildItem -Path "C:\VisionTek\FranceLake_26R2\MeteorSystemForDotNet" -Filter "*.vb" -Recurse
foreach ($file in $files) {
    if ($file.Name -eq "MeteorMessageBox.xaml.vb") { continue }
    $content = Get-Content $file.FullName -Raw
    $original = $content
    $content = $content -replace '(?<!\.)MessageBox\.Show\(', 'MeteorMessageBox.Show('
    $content = $content -replace 'ErrorDialogHelper\.ShowError\(', 'MeteorMessageBox.ShowError('
    if ($content -cne $original) {
        Write-Host "Updating $($file.FullName)"
        Set-Content -Path $file.FullName -Value $content -NoNewline
    }
}
