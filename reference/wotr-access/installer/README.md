# Wrath Access installer

An accessible **Rust + wxdragon** (native wxWidgets controls) GUI installer —
the same proven approach as the SayTheSpire2 installer: compiles to a small
ordinary exe with no self-extraction or runtime unpacking, so it doesn't trip
antivirus heuristics the way packaged Python does (the Python/Nuitka attempt
got `Wacatac.H!ml`-flagged mid-build and was abandoned).

## What it does
- Detects the game across Steam libraries (`libraryfolders.vdf`), Browse fallback.
- **Install alpha**: downloads the repo's own source archive
  (`github.com/.../archive/refs/heads/main.zip`) and installs the latest build straight from it —
  no GitHub release and no git needed (the repo must be public). Assembles the mod from the repo
  layout exactly like `deploy.ps1`: manifest + settings to the mod root, `deploy/Assemblies/*` to
  `Assemblies/`, `assets/*` to `assets/`, `vendor/prism.dll` next to `Wrath.exe` — ignoring the rest
  (`src/`, the installer, the dev-only `Mono.CSharp.dll`, docs). This is the primary alpha path.
- **Install / Update** (release): downloads the chosen release's `WrathAccess.zip` from
  GitHub (version picker incl. pre-releases, release notes shown first) and
  extracts it: `WrathAccess/` into the game's LocalLow `Modifications\` folder
  (replaced wholesale), `game/` (prism.dll) next to `Wrath.exe`; recreates
  the empty `Assemblies`/`Bundles`/`Blueprints` dirs the loader requires; adds
  `WrathAccess` to `EnabledModifications` in
  `OwlcatModificationManagerSettings.json` (other mods' entries preserved).
- **Install from file**: the same (release-layout zip) from a local zip (testers / offline).
- **Uninstall**: reverses everything; the user's Wrath Access settings are kept.
- `--cli` flag: a fully keyboard/console flow instead of the GUI.

## Building
```
cargo build --release          # in installer/
```
Release artifacts (payload zip + installer exe): `python scripts/release.py`,
then `gh release create vX.Y.Z dist/WrathAccess.zip dist/WrathAccessInstaller.exe`.
Keep the tag in sync with `Version` in `OwlcatModificationManifest.json` — the
installer compares it against release tags to offer updates.
