use std::io::{self, Write};
use std::path::PathBuf;

use crate::core::paths::GITHUB_REPO_ZIP_URL;
use crate::core::{detect, github, install, uninstall};

pub fn run() {
    println!("=== Wrath Access Installer ===");
    println!();

    let game_path = match get_game_path() {
        Some(p) => p,
        None => {
            println!("Error: invalid game directory.");
            return;
        }
    };

    println!();
    show_status();
    println!();

    loop {
        println!("Options:");
        println!("  1. Install / Update from GitHub (release)");
        println!("  2. Install latest alpha from GitHub (no release needed)");
        println!("  3. Install from local zip file");
        println!("  4. Uninstall");
        println!("  5. Exit");
        println!();

        let choice = prompt("Choose an option (1-5): ");
        println!();
        match choice.as_str() {
            "1" => install_from_github(&game_path),
            "2" => install_alpha(&game_path),
            "3" => install_from_file(&game_path),
            "4" => do_uninstall(&game_path),
            "5" => return,
            _ => println!("Invalid option."),
        }
        println!();
    }
}

fn get_game_path() -> Option<PathBuf> {
    if let Some(detected) = detect::detect_game_path() {
        println!("Detected game directory: {}", detected.display());
        let response = prompt("Use this path? (Y/n): ");
        if response != "n" && response != "no" {
            return Some(detected);
        }
    }

    let input = prompt("Press B to browse for the game directory, or type the path: ");
    let path = if input.eq_ignore_ascii_case("b") {
        rfd::FileDialog::new()
            .set_title("Select the Pathfinder: Wrath of the Righteous game directory")
            .pick_folder()?
    } else {
        PathBuf::from(input)
    };
    if detect::validate_game_path(&path) {
        Some(path)
    } else {
        println!("Error: that folder doesn't contain Wrath.exe.");
        None
    }
}

fn show_status() {
    if detect::is_mod_installed() {
        println!(
            "Installed version: {}",
            detect::installed_version().as_deref().unwrap_or("unknown")
        );
    } else {
        println!("Wrath Access is not installed.");
    }
}

fn install_from_github(game_path: &PathBuf) {
    let releases = match github::fetch_all_releases() {
        Ok(r) if !r.is_empty() => r,
        Ok(_) => {
            println!("No releases found on GitHub yet.");
            return;
        }
        Err(e) => {
            println!("Error: {}", e);
            return;
        }
    };

    println!("Available versions:");
    for (i, r) in releases.iter().enumerate() {
        let tag = if r.prerelease {
            format!("{} (pre-release)", r.tag_name)
        } else {
            r.tag_name.clone()
        };
        println!("  {}. {}", i + 1, tag);
    }
    let choice = prompt("Choose a version number (or Enter for the newest): ");
    let index = if choice.is_empty() {
        0
    } else {
        match choice.parse::<usize>() {
            Ok(n) if n >= 1 && n <= releases.len() => n - 1,
            _ => {
                println!("Invalid version number.");
                return;
            }
        }
    };
    let info = &releases[index];
    let Some(asset) = github::find_zip_asset(&info.assets) else {
        println!("Error: no .zip asset found in that release.");
        return;
    };

    println!("Downloading {}...", info.tag_name);
    match install::download_and_install(&asset.browser_download_url, game_path, |_| {}) {
        Ok(_) => println!("Successfully installed {}.", info.tag_name),
        Err(e) => println!("Error: {}", e),
    }
}

fn install_alpha(game_path: &PathBuf) {
    println!("Downloading the latest alpha straight from GitHub (no release needed)...");
    match install::download_and_install_repo(GITHUB_REPO_ZIP_URL, game_path, |_| {}) {
        Ok(_) => println!("Successfully installed the latest alpha."),
        Err(e) => println!("Error: {}", e),
    }
}

fn install_from_file(game_path: &PathBuf) {
    let input = prompt("Path to the mod zip (or B to browse): ");
    let zip = if input.eq_ignore_ascii_case("b") {
        match rfd::FileDialog::new()
            .set_title("Select the Wrath Access mod zip")
            .add_filter("Zip files", &["zip"])
            .pick_file()
        {
            Some(p) => p,
            None => return,
        }
    } else {
        PathBuf::from(input)
    };
    match install::install_from_file(&zip, game_path) {
        Ok(_) => println!("Successfully installed."),
        Err(e) => println!("Error: {}", e),
    }
}

fn do_uninstall(game_path: &PathBuf) {
    let confirm = prompt("Remove Wrath Access? Settings are kept. (y/N): ");
    if confirm != "y" && confirm != "yes" {
        return;
    }
    match uninstall::uninstall_mod(game_path) {
        Ok(removed) => {
            for r in removed {
                println!("Removed {}", r);
            }
            println!("Uninstalled.");
        }
        Err(e) => println!("Error: {}", e),
    }
}

fn prompt(message: &str) -> String {
    print!("{}", message);
    let _ = io::stdout().flush();
    let mut line = String::new();
    let _ = io::stdin().read_line(&mut line);
    line.trim().to_lowercase()
}
