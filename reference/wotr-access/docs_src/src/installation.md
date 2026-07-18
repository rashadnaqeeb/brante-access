# Installation

## Requirements

- Pathfinder: Wrath of the Righteous on **Windows** (Steam).
- A **screen reader** (NVDA, JAWS, …) or Windows SAPI voices.
- Optionally **Git** — only if you want the pull-and-deploy install path below; the installer
  doesn't need it.
- That's it — this uses the game's **native mod system**, so there is **no Unity Mod Manager** to
  install.

There are two ways to install — use whichever suits you. **Close the game first** either way, and
the setup wizard runs automatically on the first launch. Your Wrath Access settings are stored
separately, so updating never resets them.

## Without Git (installer)

1. Download the installer:
   **[WrathAccessInstaller.exe](https://github.com/bradjrenshaw/wotr-access/releases/latest/download/WrathAccessInstaller.exe)**
2. Run it. Check the game directory is right (it auto-detects your Steam install; use Browse if
   not), then choose **Install alpha** — it downloads the latest build straight from GitHub.
3. Start the game.

To **update**, just run the installer again and choose **Install alpha** — you only download the
installer itself once. It's a small accessible app, with a `--cli` keyboard/console mode if you
prefer.

## With Git

1. Clone the repo:
   ```
   git clone https://github.com/bradjrenshaw/wotr-access
   ```
2. In PowerShell, from the repo folder, run `.\deploy.ps1`.
3. Start the game. To **update** later: `git pull`, then `.\deploy.ps1` again.

`deploy.ps1` finds your install automatically (Steam libraries); if it can't, pass the folder
explicitly:

```
.\deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure"
```

## First launch

Launch the game normally. If the game has been running for a while and nothing is happening, press
Enter on the keyboard and it should load you into the main menu. The first time you launch with the
mod installed, a **setup wizard** walks you through speech rate, movement, and your preferred
exploration features (sonar, wall tones, and so on). You can re-run it any time from the mod menu
(Ctrl+M).
