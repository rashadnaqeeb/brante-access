use serde::Deserialize;

use super::paths::GITHUB_RELEASES_URL;

#[derive(Debug, Deserialize, Clone)]
pub struct ReleaseInfo {
    pub tag_name: String,
    #[serde(default)]
    pub body: String,
    #[serde(default)]
    pub prerelease: bool,
    #[serde(default)]
    pub assets: Vec<Asset>,
}

#[derive(Debug, Deserialize, Clone)]
pub struct Asset {
    pub name: String,
    pub browser_download_url: String,
}

pub fn fetch_all_releases() -> Result<Vec<ReleaseInfo>, String> {
    let client = reqwest::blocking::Client::builder()
        .user_agent("WrathAccessInstaller")
        .timeout(std::time::Duration::from_secs(15))
        .build()
        .map_err(|e| format!("Failed to create HTTP client: {}", e))?;

    let resp = client
        .get(GITHUB_RELEASES_URL)
        .send()
        .map_err(|e| format!("Failed to reach GitHub: {}", e))?;
    if !resp.status().is_success() {
        return Err(format!("GitHub returned status {}", resp.status()));
    }
    resp.json::<Vec<ReleaseInfo>>()
        .map_err(|e| format!("Failed to parse release info: {}", e))
}

/// The mod payload asset: the release zip (WrathAccess.zip).
pub fn find_zip_asset(assets: &[Asset]) -> Option<&Asset> {
    assets.iter().find(|a| a.name.to_lowercase().ends_with(".zip"))
}
