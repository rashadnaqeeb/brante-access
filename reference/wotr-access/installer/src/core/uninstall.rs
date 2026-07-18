use std::fs;
use std::path::Path;

use super::manager;
use super::paths::{installed_mod_dir, GAME_ROOT_DLLS, LEGACY_GAME_ROOT_DLLS};

/// Remove the mod folder, its EnabledModifications entry, and the Tolk natives
/// from the game folder. The user's Wrath Access settings (LocalLow/WrathAccess)
/// are deliberately kept, so a reinstall restores their configuration.
pub fn uninstall_mod(game_path: &Path) -> Result<Vec<String>, String> {
    let mut removed = Vec::new();

    let mod_dir = installed_mod_dir();
    if mod_dir.exists() {
        fs::remove_dir_all(&mod_dir)
            .map_err(|e| format!("Failed to remove {}: {}", mod_dir.display(), e))?;
        removed.push(mod_dir.display().to_string());
    }

    manager::disable_mod()?;

    for dll in GAME_ROOT_DLLS.iter().chain(LEGACY_GAME_ROOT_DLLS.iter()) {
        let p = game_path.join(dll);
        if p.exists() {
            fs::remove_file(&p).map_err(|e| {
                format!(
                    "Couldn't remove {} — close the game if it's running, then try again. ({})",
                    p.display(),
                    e
                )
            })?;
            removed.push(dll.to_string());
        }
    }

    Ok(removed)
}
