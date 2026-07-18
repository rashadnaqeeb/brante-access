//! Install = three file-level steps against the game's NATIVE mod system:
//!   1. Extract the release zip: entries under `WrathAccess/` become
//!      LocalLow/.../Modifications/WrathAccess (replaced wholesale so stale files
//!      from older versions can't linger), entries under `game/` go next to
//!      Wrath.exe (the Tolk natives the mod P/Invokes).
//!   2. Recreate the empty Assemblies/Bundles/Blueprints dirs — the game's loader
//!      enumerates them and throws if any is missing, and zips drop empty dirs.
//!   3. Add WrathAccess to EnabledModifications in the manager settings json.

use std::fs;
use std::io::{Cursor, Read};
use std::path::{Component, Path, PathBuf};

use super::manager;
use super::paths::{installed_mod_dir, modifications_dir, GAME_EXE, LEGACY_GAME_ROOT_DLLS, MOD_NAME};

fn download_bytes(url: &str, progress: impl Fn(u32)) -> Result<Vec<u8>, String> {
    let client = reqwest::blocking::Client::builder()
        .user_agent("WrathAccessInstaller")
        .timeout(std::time::Duration::from_secs(120))
        .build()
        .map_err(|e| format!("Failed to create HTTP client: {}", e))?;

    let resp = client
        .get(url)
        .send()
        .map_err(|e| format!("Download failed: {}", e))?;
    if !resp.status().is_success() {
        return Err(format!("Download returned status {}", resp.status()));
    }

    let total = resp.content_length().unwrap_or(0);
    let mut reader = resp;
    let mut buffer = Vec::new();
    let mut downloaded: u64 = 0;
    let mut buf = [0u8; 8192];
    loop {
        let n = reader.read(&mut buf).map_err(|e| format!("Read error: {}", e))?;
        if n == 0 {
            break;
        }
        buffer.extend_from_slice(&buf[..n]);
        downloaded += n as u64;
        if total > 0 {
            progress((downloaded * 100 / total) as u32);
        }
    }
    Ok(buffer)
}

/// Install a release's pre-assembled WrathAccess.zip (the `WrathAccess/` + `game/` layout).
pub fn download_and_install(
    url: &str,
    game_path: &Path,
    progress: impl Fn(u32),
) -> Result<(), String> {
    let data = download_bytes(url, progress)?;
    install_zip(&data, game_path)
}

/// Install straight from the repo's own source archive (the alpha "from GitHub directly" path) — same
/// files the git-pull + deploy.ps1 flow uses, for testers without git.
pub fn download_and_install_repo(
    url: &str,
    game_path: &Path,
    progress: impl Fn(u32),
) -> Result<(), String> {
    let data = download_bytes(url, progress)?;
    install_repo_zip(&data, game_path)
}

pub fn install_from_file(zip_path: &Path, game_path: &Path) -> Result<(), String> {
    let data = fs::read(zip_path).map_err(|e| format!("Failed to read zip: {}", e))?;
    install_zip(&data, game_path)
}

pub fn install_zip(data: &[u8], game_path: &Path) -> Result<(), String> {
    if !game_path.join(GAME_EXE).exists() {
        return Err(format!(
            "That folder doesn't contain {} — pick the game's install folder.",
            GAME_EXE
        ));
    }

    let cursor = Cursor::new(data);
    let mut archive =
        zip::ZipArchive::new(cursor).map_err(|e| format!("Failed to open zip: {}", e))?;

    // Replace any existing install wholesale.
    let mod_dir = installed_mod_dir();
    if mod_dir.exists() {
        fs::remove_dir_all(&mod_dir)
            .map_err(|e| format!("Failed to remove the old install: {}", e))?;
    }

    for i in 0..archive.len() {
        let mut file = archive
            .by_index(i)
            .map_err(|e| format!("Failed to read zip entry: {}", e))?;
        let name = file.name().to_string();

        // Route by top-level prefix; reject path traversal.
        let rel = sanitize(&name)?;
        let dest: PathBuf = if let Ok(stripped) = rel.strip_prefix(MOD_NAME) {
            mod_dir.join(stripped)
        } else if let Ok(stripped) = rel.strip_prefix("game") {
            game_path.join(stripped)
        } else {
            continue; // unknown top-level entry — ignore
        };

        if name.ends_with('/') {
            fs::create_dir_all(&dest)
                .map_err(|e| format!("Failed to create {}: {}", dest.display(), e))?;
            continue;
        }
        if let Some(parent) = dest.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| format!("Failed to create {}: {}", parent.display(), e))?;
        }
        let mut contents = Vec::new();
        file.read_to_end(&mut contents)
            .map_err(|e| format!("Failed to read {} from zip: {}", name, e))?;
        fs::write(&dest, &contents).map_err(|e| {
            if dest.starts_with(game_path) {
                format!(
                    "Couldn't write {} — close the game if it's running, then try again. ({})",
                    dest.display(),
                    e
                )
            } else {
                format!("Failed to write {}: {}", dest.display(), e)
            }
        })?;
    }

    // The loader enumerates these and throws if any is missing.
    for required in ["Assemblies", "Bundles", "Blueprints"] {
        fs::create_dir_all(mod_dir.join(required))
            .map_err(|e| format!("Failed to create {}: {}", required, e))?;
    }
    if !mod_dir.join("OwlcatModificationManifest.json").exists() {
        return Err("The zip didn't contain the mod (no WrathAccess/OwlcatModificationManifest.json).".into());
    }

    // Retire files older versions placed in the game folder (best-effort).
    for dll in LEGACY_GAME_ROOT_DLLS {
        let _ = fs::remove_file(game_path.join(dll));
    }

    fs::create_dir_all(modifications_dir())
        .map_err(|e| format!("Failed to create Modifications dir: {}", e))?;
    manager::enable_mod()
}

/// Install from the repo's own source archive (github .../archive/refs/heads/main.zip). Entries sit
/// under a single top folder like `wotr-access-main/`; we strip it and map the repo layout onto the
/// installed mod — manifest + settings to the mod root, `deploy/Assemblies/*` to `Assemblies/`,
/// `deploy/docs/*` to `docs/` (the bundled documentation), `assets/*` to `assets/`, and
/// `vendor/prism.dll` next to Wrath.exe — ignoring everything else (src, installer, docs_src, the
/// dev-only Mono.CSharp.dll). Mirrors what deploy.ps1 does from a clone.
pub fn install_repo_zip(data: &[u8], game_path: &Path) -> Result<(), String> {
    if !game_path.join(GAME_EXE).exists() {
        return Err(format!(
            "That folder doesn't contain {} — pick the game's install folder.",
            GAME_EXE
        ));
    }

    let cursor = Cursor::new(data);
    let mut archive =
        zip::ZipArchive::new(cursor).map_err(|e| format!("Failed to open zip: {}", e))?;

    let mod_dir = installed_mod_dir();
    if mod_dir.exists() {
        fs::remove_dir_all(&mod_dir)
            .map_err(|e| format!("Failed to remove the old install: {}", e))?;
    }

    for i in 0..archive.len() {
        let mut file = archive
            .by_index(i)
            .map_err(|e| format!("Failed to read zip entry: {}", e))?;
        let name = file.name().to_string();
        let rel = sanitize(&name)?;

        // Drop the archive's single top-level folder (e.g. "wotr-access-main/").
        let mut comps = rel.components();
        comps.next();
        let repo_rel = comps.as_path();
        if repo_rel.as_os_str().is_empty() {
            continue;
        }

        // Map the repo layout onto the installed mod; ignore anything that isn't part of the payload.
        let dest: PathBuf = if repo_rel == Path::new("OwlcatModificationManifest.json")
            || repo_rel == Path::new("OwlcatModificationSettings.json")
        {
            mod_dir.join(repo_rel)
        } else if let Ok(stripped) = repo_rel.strip_prefix("deploy/Assemblies") {
            mod_dir.join("Assemblies").join(stripped)
        } else if let Ok(stripped) = repo_rel.strip_prefix("deploy/docs") {
            mod_dir.join("docs").join(stripped)
        } else if let Ok(stripped) = repo_rel.strip_prefix("assets") {
            mod_dir.join("assets").join(stripped)
        } else if repo_rel == Path::new("vendor/prism.dll") {
            game_path.join("prism.dll")
        } else {
            continue;
        };

        if name.ends_with('/') {
            fs::create_dir_all(&dest)
                .map_err(|e| format!("Failed to create {}: {}", dest.display(), e))?;
            continue;
        }
        if let Some(parent) = dest.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| format!("Failed to create {}: {}", parent.display(), e))?;
        }
        let mut contents = Vec::new();
        file.read_to_end(&mut contents)
            .map_err(|e| format!("Failed to read {} from zip: {}", name, e))?;
        fs::write(&dest, &contents).map_err(|e| {
            if dest.starts_with(game_path) {
                format!(
                    "Couldn't write {} — close the game if it's running, then try again. ({})",
                    dest.display(),
                    e
                )
            } else {
                format!("Failed to write {}: {}", dest.display(), e)
            }
        })?;
    }

    for required in ["Assemblies", "Bundles", "Blueprints"] {
        fs::create_dir_all(mod_dir.join(required))
            .map_err(|e| format!("Failed to create {}: {}", required, e))?;
    }
    if !mod_dir.join("OwlcatModificationManifest.json").exists() {
        return Err("The download didn't contain the mod payload (no OwlcatModificationManifest.json).".into());
    }
    if !mod_dir.join("Assemblies").join("WrathAccess.dll").exists() {
        return Err("The download didn't contain WrathAccess.dll (deploy/Assemblies) — the repo may be mid-build.".into());
    }

    for dll in LEGACY_GAME_ROOT_DLLS {
        let _ = fs::remove_file(game_path.join(dll));
    }

    fs::create_dir_all(modifications_dir())
        .map_err(|e| format!("Failed to create Modifications dir: {}", e))?;
    manager::enable_mod()
}

/// A zip entry as a safe relative path (no absolute paths, no `..`).
fn sanitize(name: &str) -> Result<PathBuf, String> {
    let path = PathBuf::from(name);
    if path
        .components()
        .any(|c| !matches!(c, Component::Normal(_)))
    {
        return Err(format!("Unsafe path in zip: {}", name));
    }
    Ok(path)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sanitize_rejects_traversal() {
        assert!(sanitize("../evil.dll").is_err());
        assert!(sanitize("WrathAccess/../../evil.dll").is_err());
        assert!(sanitize("WrathAccess/Assemblies/WrathAccess.dll").is_ok());
    }
}
