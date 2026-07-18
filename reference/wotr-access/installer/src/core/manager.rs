//! The game's mod-manager settings file (OwlcatModificationManagerSettings.json):
//! a mod is only applied when its UniqueName is listed in EnabledModifications.
//! Edits go through serde_json::Value so other mods' entries and any unknown
//! fields are preserved verbatim.

use std::fs;
use std::path::Path;

use serde_json::{json, Value};

use super::paths::{manager_settings_path, MOD_NAME};

pub fn enable_mod() -> Result<(), String> {
    edit(|list| {
        if !list.iter().any(|v| v.as_str() == Some(MOD_NAME)) {
            list.push(Value::String(MOD_NAME.to_string()));
        }
    })
}

pub fn disable_mod() -> Result<(), String> {
    edit(|list| list.retain(|v| v.as_str() != Some(MOD_NAME)))
}

fn edit(change: impl FnOnce(&mut Vec<Value>)) -> Result<(), String> {
    let path = manager_settings_path();
    let mut root = load(&path)?;
    let obj = root
        .as_object_mut()
        .ok_or_else(|| "Settings file is not a JSON object".to_string())?;
    obj.entry("SourceDirectories").or_insert_with(|| json!([]));
    let list = obj
        .entry("EnabledModifications")
        .or_insert_with(|| json!([]))
        .as_array_mut()
        .ok_or_else(|| "EnabledModifications is not an array".to_string())?;
    change(list);

    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("Failed to create {}: {}", parent.display(), e))?;
    }
    let text = serde_json::to_string_pretty(&root)
        .map_err(|e| format!("Failed to serialize settings: {}", e))?;
    fs::write(&path, text).map_err(|e| format!("Failed to write {}: {}", path.display(), e))
}

fn load(path: &Path) -> Result<Value, String> {
    if !path.exists() {
        return Ok(json!({}));
    }
    let text = fs::read_to_string(path)
        .map_err(|e| format!("Failed to read {}: {}", path.display(), e))?;
    // Tolerate a UTF-8 BOM.
    serde_json::from_str(text.trim_start_matches('\u{feff}'))
        .map_err(|e| format!("Failed to parse {}: {}", path.display(), e))
}
