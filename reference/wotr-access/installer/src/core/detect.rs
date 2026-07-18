use std::path::{Path, PathBuf};

use regex::Regex;

use super::paths::{installed_mod_dir, steam_defaults, GAME_DIR_NAME, GAME_EXE, MOD_NAME};

pub fn detect_game_path() -> Option<PathBuf> {
    for steam_dir in steam_defaults() {
        // Every Steam library from libraryfolders.vdf.
        let vdf_path = steam_dir.join("steamapps").join("libraryfolders.vdf");
        if let Ok(content) = std::fs::read_to_string(&vdf_path) {
            for lib_path in parse_vdf_library_paths(&content) {
                let game_path = lib_path.join("steamapps").join("common").join(GAME_DIR_NAME);
                if validate_game_path(&game_path) {
                    return Some(game_path);
                }
            }
        }
        // The default location.
        let default_path = steam_dir.join("steamapps").join("common").join(GAME_DIR_NAME);
        if validate_game_path(&default_path) {
            return Some(default_path);
        }
    }
    None
}

pub fn parse_vdf_library_paths(content: &str) -> Vec<PathBuf> {
    let re = Regex::new(r#""path"\s+"([^"]+)""#).unwrap();
    re.captures_iter(content)
        .map(|cap| PathBuf::from(cap[1].replace("\\\\", "\\")))
        .collect()
}

pub fn validate_game_path(path: &Path) -> bool {
    path.join(GAME_EXE).exists()
}

/// Installed = the mod's assembly is present in the native Modifications folder.
pub fn is_mod_installed() -> bool {
    installed_mod_dir()
        .join("Assemblies")
        .join(format!("{}.dll", MOD_NAME))
        .exists()
}

/// The installed version, read from the installed manifest's Version field.
pub fn installed_version() -> Option<String> {
    let manifest = installed_mod_dir().join("OwlcatModificationManifest.json");
    let text = std::fs::read_to_string(manifest).ok()?;
    let json: serde_json::Value = serde_json::from_str(&text).ok()?;
    json.get("Version")?.as_str().map(|s| s.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    #[test]
    fn parse_vdf_paths() {
        let content = r#"
        "0" { "path"		"C:\\Program Files (x86)\\Steam" }
        "1" { "path"		"D:\\SteamLibrary" }
        "#;
        let paths = parse_vdf_library_paths(content);
        assert_eq!(paths.len(), 2);
        assert_eq!(paths[1], PathBuf::from("D:\\SteamLibrary"));
    }

    #[test]
    fn validate_requires_wrath_exe() {
        let dir = tempfile::tempdir().unwrap();
        assert!(!validate_game_path(dir.path()));
        fs::write(dir.path().join(GAME_EXE), "").unwrap();
        assert!(validate_game_path(dir.path()));
    }
}
