$ErrorActionPreference = 'Stop'

$asm = Join-Path $PSScriptRoot '..\javis\bin\Debug\net10.0-windows\javis.dll'
$asm = [IO.Path]::GetFullPath($asm)

if (!(Test-Path $asm)) {
  Write-Host "Assembly not found: $asm"
  exit 1
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($asm)
try {
  $hits = $zip.Entries | Where-Object { $_.FullName -like '*jaemin_face.png' }
  if ($hits.Count -eq 0) {
    Write-Host 'NOT FOUND in assembly resources: jaemin_face.png'
  } else {
    Write-Host 'FOUND in assembly resources:'
    $hits | ForEach-Object { Write-Host " - $($_.FullName) ($($_.Length) bytes)" }
  }
}
finally {
  $zip.Dispose()
}
