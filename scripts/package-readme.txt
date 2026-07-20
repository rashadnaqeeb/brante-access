Brante Access
=============

A screen reader accessibility mod for The Life and Suffering of Sir Brante.
The whole game becomes playable with speech and keyboard alone: story text,
choices (including locked choices and why they are locked), stats, menus,
saves, popups, and endings. No mouse needed anywhere.

Project page: https://github.com/rashadnaqeeb/brante-access

Requirements
------------

- The Windows version of The Life and Suffering of Sir Brante (Steam or GOG).
- A screen reader. JAWS and NVDA are spoken to directly through the bundled
  Prism library; without one, the mod falls back to the default SAPI voice.

Install
-------

1. Find the game folder, the one that contains
   "The Life and Suffering of Sir Brante.exe".
   On Steam: right click the game in your library, then Manage, then
   Browse local files. The default location is
   C:\Program Files (x86)\Steam\steamapps\common\The Life and Suffering of Sir Brante
2. Extract everything from the zip into that folder, keeping the folder
   structure. Afterwards winhttp.dll and prism.dll sit next to the game exe,
   and a BepInEx folder sits beside them.
3. Start the game normally. The first modded start takes a few seconds longer
   while the mod loader writes its config. The mod speaks a short startup
   line when it is ready.

The keyboard reference is in BranteAccess-KEYS.txt next to this file.

Uninstall
---------

Delete winhttp.dll from the game folder to turn the mod off. To remove it
completely, also delete prism.dll, doorstop_config.ini, .doorstop_version,
and the BepInEx folder. Your saves are untouched either way.

Troubleshooting
---------------

If the game starts but nothing speaks, check that the files landed in the
right place (step 2 above), then look at BepInEx\LogOutput.log inside the
game folder and report the problem at the project page.

Licenses
--------

The mod is MIT licensed. It bundles BepInEx (LGPL-2.1) and Prism (MPL-2.0);
all license texts are under BepInEx\plugins\BranteAccess\licenses.
