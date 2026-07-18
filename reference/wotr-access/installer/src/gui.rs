use std::cell::RefCell;
use std::path::PathBuf;
use std::rc::Rc;

use wxdragon::prelude::*;

use crate::core::paths::GITHUB_REPO_ZIP_URL;
use crate::core::{detect, github, install, uninstall};

struct State {
    all_releases: Vec<github::ReleaseInfo>,
    latest: Option<github::ReleaseInfo>,
}

pub fn run() {
    wxdragon::main(|_app| {
        let frame = Frame::builder()
            .with_title("Wrath Access Installer")
            .with_size(Size::new(650, 500))
            .build();

        let panel = Panel::builder(&frame).build();
        let main_sizer = BoxSizer::builder(Orientation::Vertical).build();

        let status = StaticText::builder(&panel)
            .with_label("Detecting game directory...")
            .build();

        // Game path row
        let path_sizer = BoxSizer::builder(Orientation::Horizontal).build();
        let path_label = StaticText::builder(&panel)
            .with_label("Game directory:")
            .build();
        let path_input = TextCtrl::builder(&panel).build();
        let browse_btn = Button::builder(&panel).with_label("Browse...").build();
        path_sizer.add(&path_label, 0, SizerFlag::All, 4);
        path_sizer.add(&path_input, 1, SizerFlag::Expand | SizerFlag::All, 4);
        path_sizer.add(&browse_btn, 0, SizerFlag::All, 4);

        // Log area
        let log = TextCtrl::builder(&panel)
            .with_style(TextCtrlStyle::MultiLine | TextCtrlStyle::ReadOnly | TextCtrlStyle::WordWrap)
            .build();

        // Buttons
        let btn_sizer = BoxSizer::builder(Orientation::Horizontal).build();
        let install_btn = Button::builder(&panel).with_label("Install").build();
        let install_alpha_btn = Button::builder(&panel).with_label("Install alpha").build();
        let install_file_btn = Button::builder(&panel)
            .with_label("Install from file...")
            .build();
        let uninstall_btn = Button::builder(&panel).with_label("Uninstall").build();
        btn_sizer.add_stretch_spacer(1);
        btn_sizer.add(&install_btn, 0, SizerFlag::All, 4);
        btn_sizer.add(&install_alpha_btn, 0, SizerFlag::All, 4);
        btn_sizer.add(&install_file_btn, 0, SizerFlag::All, 4);
        btn_sizer.add(&uninstall_btn, 0, SizerFlag::All, 4);

        main_sizer.add(&status, 0, SizerFlag::Expand | SizerFlag::All, 8);
        main_sizer.add_sizer(&path_sizer, 0, SizerFlag::Expand | SizerFlag::Left | SizerFlag::Right, 8);
        main_sizer.add(&log, 1, SizerFlag::Expand | SizerFlag::All, 8);
        main_sizer.add_sizer(&btn_sizer, 0, SizerFlag::Expand | SizerFlag::All, 4);
        panel.set_sizer(main_sizer, true);

        install_btn.enable(false);
        install_alpha_btn.enable(false);
        install_file_btn.enable(false);
        uninstall_btn.enable(false);

        let state = Rc::new(RefCell::new(State { all_releases: Vec::new(), latest: None }));

        // Auto-detect the game.
        if let Some(detected) = detect::detect_game_path() {
            path_input.set_value(&detected.to_string_lossy());
            log_append(&log, &format!("Game directory: {}", detected.display()));
        } else {
            status.set_label("Game directory not found. Please browse to select it.");
            log_append(&log, "Could not auto-detect the game directory.");
        }

        // Fetch releases (before the window shows, so there's no visible delay).
        match github::fetch_all_releases() {
            Ok(releases) => {
                let latest = releases.iter().find(|r| !r.prerelease).cloned();
                if let Some(ref info) = latest {
                    log_append(&log, &format!("Latest version: {}", info.tag_name));
                }
                let mut s = state.borrow_mut();
                s.all_releases = releases;
                s.latest = latest;
            }
            Err(e) => {
                log_append(&log, &format!("Failed to check for releases: {}", e));
                log_append(&log, "Online install unavailable; \"Install from file\" still works.");
            }
        }
        update_state(&status, &install_btn, &install_alpha_btn, &install_file_btn, &uninstall_btn,
                     &PathBuf::from(path_input.get_value()), &state, &log);

        // Browse
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_alpha_btn_c = install_alpha_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();
            browse_btn.on_click(move |_| {
                let dialog = DirDialog::builder(
                    &frame_c,
                    "Select the Pathfinder: Wrath of the Righteous game directory",
                    "",
                )
                .build();
                if dialog.show_modal() == ID_OK {
                    if let Some(path_str) = dialog.get_path() {
                        let path = PathBuf::from(&path_str);
                        path_input_c.set_value(&path_str);
                        if detect::validate_game_path(&path) {
                            log_append(&log_c, &format!("Game directory: {}", path.display()));
                        } else {
                            log_append(&log_c, "That folder doesn't contain Wrath.exe.");
                        }
                        update_state(&status_c, &install_btn_c, &install_alpha_btn_c, &install_file_btn_c,
                                     &uninstall_btn_c, &path, &state_c, &log_c);
                    }
                }
            });
        }

        // Install (from GitHub, with a version picker)
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_alpha_btn_c = install_alpha_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let browse_btn_c = browse_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();
            install_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                let borrow = state_c.borrow();
                if borrow.all_releases.is_empty() {
                    return;
                }
                let choices: Vec<String> = borrow
                    .all_releases
                    .iter()
                    .map(|r| {
                        if r.prerelease {
                            format!("{} (pre-release)", r.tag_name)
                        } else {
                            r.tag_name.clone()
                        }
                    })
                    .collect();
                let choice_refs: Vec<&str> = choices.iter().map(|s| s.as_str()).collect();
                drop(borrow);

                let dialog = SingleChoiceDialog::builder(
                    &frame_c,
                    "Select a version to install:",
                    "Choose Version",
                    &choice_refs,
                )
                .build();
                if dialog.show_modal() != ID_OK {
                    return;
                }
                let selection = dialog.get_selection();
                if selection < 0 {
                    return;
                }

                let borrow = state_c.borrow();
                let info = &borrow.all_releases[selection as usize];
                if !info.body.is_empty() {
                    let notes = MessageDialog::builder(
                        &frame_c,
                        &format!("Release notes for {}:\n\n{}\n\nProceed?", info.tag_name, info.body),
                        &format!("Install {}", info.tag_name),
                    )
                    .with_style(MessageDialogStyle::YesNo | MessageDialogStyle::IconQuestion)
                    .build();
                    if notes.show_modal() != ID_YES {
                        return;
                    }
                }
                let Some(asset) = github::find_zip_asset(&info.assets) else {
                    log_append(&log_c, "Error: no .zip asset found in that release.");
                    return;
                };
                let url = asset.browser_download_url.clone();
                let version = info.tag_name.clone();
                drop(borrow);

                install_btn_c.enable(false);
                install_file_btn_c.enable(false);
                uninstall_btn_c.enable(false);
                browse_btn_c.enable(false);
                log_append(&log_c, "Downloading...");
                status_c.set_label("Downloading...");

                let result = install::download_and_install(&url, &game_path, |_pct| {});

                browse_btn_c.enable(true);
                finish(&frame_c, result.map(|_| version), "installed", &log_c);
                update_state(&status_c, &install_btn_c, &install_alpha_btn_c, &install_file_btn_c,
                             &uninstall_btn_c, &game_path, &state_c, &log_c);
            });
        }

        // Install the latest alpha straight from the repo (no release, no git needed).
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_alpha_btn_c = install_alpha_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let browse_btn_c = browse_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();
            install_alpha_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                let confirm = MessageDialog::builder(
                    &frame_c,
                    "Install the latest Wrath Access alpha, downloaded directly from GitHub? This is always the newest build.",
                    "Install alpha",
                )
                .with_style(MessageDialogStyle::YesNo | MessageDialogStyle::IconQuestion)
                .build();
                if confirm.show_modal() != ID_YES {
                    return;
                }

                install_btn_c.enable(false);
                install_alpha_btn_c.enable(false);
                install_file_btn_c.enable(false);
                uninstall_btn_c.enable(false);
                browse_btn_c.enable(false);
                log_append(&log_c, "Downloading the latest alpha from GitHub...");
                status_c.set_label("Downloading...");

                let result = install::download_and_install_repo(GITHUB_REPO_ZIP_URL, &game_path, |_pct| {});

                browse_btn_c.enable(true);
                finish(&frame_c, result.map(|_| "latest alpha".to_string()), "installed", &log_c);
                update_state(&status_c, &install_btn_c, &install_alpha_btn_c, &install_file_btn_c,
                             &uninstall_btn_c, &game_path, &state_c, &log_c);
            });
        }

        // Install from file
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_alpha_btn_c = install_alpha_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();
            install_file_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                let dialog = FileDialog::builder(&frame_c)
                    .with_message("Select the Wrath Access mod zip")
                    .with_wildcard("Zip files (*.zip)|*.zip")
                    .with_style(FileDialogStyle::Open | FileDialogStyle::FileMustExist)
                    .build();
                if dialog.show_modal() != ID_OK {
                    return;
                }
                let Some(zip_path) = dialog.get_path() else { return };

                let result = install::install_from_file(&PathBuf::from(&zip_path), &game_path);
                finish(&frame_c, result.map(|_| "from file".to_string()), "installed", &log_c);
                update_state(&status_c, &install_btn_c, &install_alpha_btn_c, &install_file_btn_c,
                             &uninstall_btn_c, &game_path, &state_c, &log_c);
            });
        }

        // Uninstall
        {
            let frame_c = frame.clone();
            let path_input_c = path_input.clone();
            let status_c = status.clone();
            let install_btn_c = install_btn.clone();
            let install_alpha_btn_c = install_alpha_btn.clone();
            let install_file_btn_c = install_file_btn.clone();
            let uninstall_btn_c = uninstall_btn.clone();
            let log_c = log.clone();
            let state_c = state.clone();
            uninstall_btn.on_click(move |_| {
                let game_path = PathBuf::from(path_input_c.get_value());
                let confirm = MessageDialog::builder(
                    &frame_c,
                    "Remove Wrath Access? Your settings will be kept for a future reinstall.",
                    "Uninstall Wrath Access",
                )
                .with_style(MessageDialogStyle::YesNo | MessageDialogStyle::IconQuestion)
                .build();
                if confirm.show_modal() != ID_YES {
                    return;
                }

                let result = uninstall::uninstall_mod(&game_path).map(|_| "removed".to_string());
                finish(&frame_c, result, "uninstalled", &log_c);
                update_state(&status_c, &install_btn_c, &install_alpha_btn_c, &install_file_btn_c,
                             &uninstall_btn_c, &game_path, &state_c, &log_c);
            });
        }

        frame.show(true);
    })
    .expect("Failed to start application");
}

/// Common completion handling: log + an announced modal with the outcome.
fn finish(parent: &impl WxWidget, result: Result<String, String>, verb: &str, log: &TextCtrl) {
    match result {
        Ok(what) => {
            log_append(log, &format!("Successfully {} ({}).", verb, what));
            MessageDialog::builder(
                parent,
                &format!("Wrath Access {} successfully.", verb),
                "Wrath Access Installer",
            )
            .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconInformation)
            .build()
            .show_modal();
        }
        Err(e) => {
            log_append(log, &format!("Error: {}", e));
            MessageDialog::builder(parent, &e, "Wrath Access Installer")
                .with_style(MessageDialogStyle::OK | MessageDialogStyle::IconError)
                .build()
                .show_modal();
        }
    }
}

fn update_state(
    status: &StaticText,
    install_btn: &Button,
    install_alpha_btn: &Button,
    install_file_btn: &Button,
    uninstall_btn: &Button,
    game_path: &std::path::Path,
    state: &Rc<RefCell<State>>,
    log: &TextCtrl,
) {
    let valid = detect::validate_game_path(game_path);
    let installed = detect::is_mod_installed();
    let installed_version = detect::installed_version();

    install_alpha_btn.enable(valid); // the alpha build downloads straight from the repo; no release needed
    install_file_btn.enable(valid);
    uninstall_btn.enable(installed && valid);

    if installed {
        log_append(
            log,
            &format!(
                "Installed version: {}",
                installed_version.as_deref().unwrap_or("unknown")
            ),
        );
    }

    let borrow = state.borrow();
    let Some(info) = borrow.latest.as_ref() else {
        install_btn.enable(false);
        if !valid {
            status.set_label("Select the game directory to continue.");
        }
        return;
    };
    let latest = &info.tag_name;
    if !valid {
        install_btn.enable(false);
        status.set_label("Select the game directory to continue.");
    } else if !installed {
        install_btn.set_label("Install");
        install_btn.enable(true);
        status.set_label(&format!("Ready to install version {}.", latest));
    } else if is_up_to_date(installed_version.as_deref(), latest) {
        install_btn.set_label("Install");
        install_btn.enable(true); // allow reinstall/downgrade via the version picker
        status.set_label(&format!("Wrath Access is up to date (version {}).", latest));
    } else {
        install_btn.set_label("Update");
        install_btn.enable(true);
        status.set_label(&format!(
            "Update available: {} -> {}",
            installed_version.as_deref().unwrap_or("unknown"),
            latest
        ));
    }
}

fn parse_version(s: &str) -> Option<semver::Version> {
    let trimmed = s.strip_prefix('v').or_else(|| s.strip_prefix('V')).unwrap_or(s);
    semver::Version::parse(trimmed).ok()
}

fn is_up_to_date(installed: Option<&str>, latest: &str) -> bool {
    let Some(installed) = installed else { return false };
    match (parse_version(installed), parse_version(latest)) {
        (Some(inst), Some(lat)) => inst >= lat,
        _ => installed == latest,
    }
}

fn log_append(log: &TextCtrl, msg: &str) {
    let current = log.get_value();
    if current.is_empty() {
        log.set_value(msg);
    } else {
        log.set_value(&format!("{}\n{}", current, msg));
    }
}
