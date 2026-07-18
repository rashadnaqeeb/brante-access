# Builds the documentation book and stages it into deploy/docs/ for distribution.
#
# Requires mdbook on PATH (https://rust-lang.github.io/mdBook/). Run this whenever the docs change,
# then commit deploy/docs/. The committed copy is what ships through every install path — the release
# zip, deploy.ps1 (git testers), and the installer's "Install alpha" (repo zip) — so they all carry the
# same offline, version-matched docs the in-game Help > Read documentation button opens.
#
# For a live preview while editing, use `mdbook serve docs_src` instead.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

mdbook build "$root/docs_src"

$book = Join-Path $root 'docs_src/book'
$dest = Join-Path $root 'deploy/docs'
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$book/*" $dest -Recurse -Force

Write-Host "Built docs and staged them into deploy/docs/. Commit deploy/docs and push." -ForegroundColor Green
