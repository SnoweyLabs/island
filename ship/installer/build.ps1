# Builds what people download (WORK-ORDER-14 sections 2 and 3), from the repository's root:
#   1. the self-contained release build of ship/size.md into ship/app (Microsoft's .NET runtime inside, a plain folder, no debugging files, no path of this computer);
#   2. into the same folder: the add-on (extension/ without its tests and PROTOCOL.md), the licence and the third-party notices;
#   3. with -Installer: ship/installer/out/Island-Setup.exe, and with -TestCopy the quiet test installer Island-Setup-testcopy.exe as well.
# Inno Setup's compiler is looked for where its own installer puts it (per user or for everyone) unless -Iscc names it.
param(
  [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
  [switch]$SkipBuild,
  [switch]$Installer,
  [switch]$TestCopy,
  [string]$Iscc
)
$ErrorActionPreference = 'Stop'

$app = Join-Path $Root 'ship\app'
if (-not $SkipBuild) {
  $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
  if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
  if (Test-Path $app) { Remove-Item $app -Recurse -Force }
  & $dotnet publish (Join-Path $Root 'src\Island.App') -c Release -r win-x64 --self-contained true -o $app `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false `
    -p:Deterministic=true -p:ContinuousIntegrationBuild=true "-p:PathMap=$Root=/_"
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

  # The add-on, where Settings -> General -> "Open the add-on folder" looks for it: a folder named extension beside the program.
  $source = Join-Path $Root 'extension'
  $target = Join-Path $app 'extension'
  Get-ChildItem $source -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($source.Length + 1)
    ($relative.Split('\')[0] -notin @('tests', 'node_modules')) -and ($relative -ne 'PROTOCOL.md')
  } | ForEach-Object {
    $to = Join-Path $target $_.FullName.Substring($source.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item $_.FullName $to
  }

  # The licence (the installer shows it on one page) and the third-party notices with the runtime's own licence files.
  Copy-Item (Join-Path $Root 'ship\public\LICENSE') (Join-Path $app 'LICENSE.txt')
  Copy-Item (Join-Path $Root 'ship\THIRD-PARTY-NOTICES.md') (Join-Path $app 'THIRD-PARTY-NOTICES.md')
  New-Item -ItemType Directory -Force (Join-Path $app 'third-party') | Out-Null
  Copy-Item (Join-Path $Root 'ship\third-party\*') (Join-Path $app 'third-party')
}

if ($Installer -or $TestCopy) {
  if (-not $Iscc) {
    $Iscc = @(
      (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
      (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
      (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
  }
  if (-not $Iscc) { throw 'Inno Setup 6 was not found: winget install JRSoftware.InnoSetup' }
  $script = Join-Path $PSScriptRoot 'island.iss'
  if ($Installer) {
    & $Iscc /Q $script
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
  }
  if ($TestCopy) {
    & $Iscc /Q /DTestCopy $script
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for the test copy ($LASTEXITCODE)" }
  }
}
