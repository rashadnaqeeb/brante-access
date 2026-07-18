use std::path::PathBuf;

pub const GITHUB_RELEASES_URL: &str =
    "https://api.github.com/repos/bradjrenshaw/wotr-access/releases";
/// The repo's auto-generated source archive of the main branch. The alpha "install latest from GitHub"
/// path downloads this (no release needed) and installs from the repo layout, exactly the files the
/// git-pull + deploy.ps1 flow uses. GitHub redirects this to codeload; the HTTP client follows it.
pub const GITHUB_REPO_ZIP_URL: &str =
    "https://github.com/bradjrenshaw/wotr-access/archive/refs/heads/main.zip";
pub const GAME_DIR_NAME: &str = "Pathfinder Second Adventure";
pub const GAME_EXE: &str = "Wrath.exe";
pub const MOD_NAME: &str = "WrathAccess";

/// Native speech DLL the mod P/Invokes — it must sit next to Wrath.exe. Prism talks to screen
/// readers directly, so it's the only native we ship.
pub const GAME_ROOT_DLLS: &[&str] = &["prism.dll"];

/// Files older versions shipped but we no longer install (the Tolk era). Install and uninstall
/// both remove them so upgrades don't leave stale binaries in the game folder.
pub const LEGACY_GAME_ROOT_DLLS: &[&str] =
    &["Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll"];

/// The game's LocalLow data root, where the native mod system lives.
pub fn locallow_game_dir() -> PathBuf {
    dirs::home_dir()
        .unwrap_or_else(|| PathBuf::from("C:\\Users\\Default"))
        .join("AppData")
        .join("LocalLow")
        .join("Owlcat Games")
        .join("Pathfinder Wrath Of The Righteous")
}

pub fn modifications_dir() -> PathBuf {
    locallow_game_dir().join("Modifications")
}

pub fn installed_mod_dir() -> PathBuf {
    modifications_dir().join(MOD_NAME)
}

/// The mod-manager settings file holding EnabledModifications.
pub fn manager_settings_path() -> PathBuf {
    locallow_game_dir().join("OwlcatModificationManagerSettings.json")
}

pub fn steam_defaults() -> Vec<PathBuf> {
    vec![
        PathBuf::from("C:\\Program Files (x86)\\Steam"),
        PathBuf::from("C:\\Program Files\\Steam"),
    ]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn locallow_paths_nest() {
        assert!(installed_mod_dir().starts_with(modifications_dir()));
        assert!(modifications_dir().starts_with(locallow_game_dir()));
        assert!(manager_settings_path()
            .to_string_lossy()
            .ends_with("OwlcatModificationManagerSettings.json"));
    }
}
