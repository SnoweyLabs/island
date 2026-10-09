# Makes the public copy of Island (WORK-ORDER-14 section 1) in dist/public: what git holds at HEAD (git archive, not this folder's history),
# and at its top the licence, the README, the third-party notices; its .gitignore without the line that keeps this workshop's notes out of git.
param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$ErrorActionPreference = 'Stop'

$public = Join-Path $Root 'dist\public'
if (Test-Path $public) { Remove-Item $public -Recurse -Force }
New-Item -ItemType Directory -Force $public | Out-Null
$tar = Join-Path $Root 'dist\public.tar'
git -C $Root archive --format=tar -o $tar HEAD
if ($LASTEXITCODE -ne 0) { throw "git archive failed ($LASTEXITCODE)" }
& (Join-Path $env:SystemRoot 'System32\tar.exe') -xf $tar -C $public  # Windows' own tar: another tar on the PATH may read "D:" as a host
if ($LASTEXITCODE -ne 0) { throw "tar failed ($LASTEXITCODE)" }
Remove-Item $tar

Copy-Item (Join-Path $Root 'ship\public\LICENSE') (Join-Path $public 'LICENSE')
Copy-Item (Join-Path $Root 'ship\public\README.md') (Join-Path $public 'README.md')
Copy-Item (Join-Path $Root 'ship\THIRD-PARTY-NOTICES.md') (Join-Path $public 'THIRD-PARTY-NOTICES.md')

# In the workshop every .md at the top is a working note kept out of git; in the public copy the top holds the README and the notices.
$ignore = Join-Path $public '.gitignore'
$lines = Get-Content $ignore | Where-Object { $_ -ne '/*.md' -and $_ -ne '/Research/' }
[System.IO.File]::WriteAllLines($ignore, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
"dist/public: {0} files" -f @(Get-ChildItem $public -Recurse -File).Count

# A git of its own, with every file added but nothing committed, so the copy behaves as a clone does (the guards list what git holds);
# the one commit is made when it is published.
git -C $public init -q -b main
if ($LASTEXITCODE -ne 0) { throw "git init failed ($LASTEXITCODE)" }
git -C $public add -A
if ($LASTEXITCODE -ne 0) { throw "git add failed ($LASTEXITCODE)" }
"dist/public: {0} files added to its own git" -f @(git -C $public ls-files).Count
