# Builds ship/addon/island-addon.zip from the extension/ folder (WORK-ORDER-8 section 7).
# Left out: the add-on's tests, PROTOCOL.md (the island's side of the protocol) and README.md (instructions for loading it by hand). The manifest is copied as it is.
# Entry names use forward slashes and every entry carries the same fixed date, so the same files always give the same zip and no date of this computer is in it.
param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)

Add-Type -AssemblyName System.IO.Compression
$source = Join-Path $Root 'extension'
$target = Join-Path $PSScriptRoot 'island-addon.zip'
if (Test-Path $target) { Remove-Item $target }

$skip = @('tests', 'node_modules')
$left = @('PROTOCOL.md', 'README.md')
$files = Get-ChildItem $source -Recurse -File | Where-Object {
  $relative = $_.FullName.Substring($source.Length + 1)
  $top = $relative.Split('\')[0]
  ($skip -notcontains $top) -and ($left -notcontains $relative)
} | Sort-Object { $_.FullName.Substring($source.Length + 1).Replace('\', '/') }

$stream = [System.IO.File]::Open($target, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($file in $files) {
  $name = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
  $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
  $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
  $in = [System.IO.File]::OpenRead($file.FullName)
  $out = $entry.Open()
  $in.CopyTo($out)
  $out.Dispose()
  $in.Dispose()
}
$zip.Dispose()
$stream.Dispose()
"{0}: {1} files" -f $target, @($files).Count
