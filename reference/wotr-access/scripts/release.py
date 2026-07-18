"""Build the Wrath Access release artifacts.

Produces, under dist/:
  WrathAccess.zip           — the mod payload the installer downloads/extracts:
                              WrathAccess/ (the native-mod folder) + game/ (prism.dll)
  WrathAccessInstaller.exe  — the Rust installer (cargo build --release)

Release = `gh release create vX.Y.Z dist/WrathAccess.zip dist/WrathAccessInstaller.exe`
(keep the tag in sync with Version in OwlcatModificationManifest.json — the
installer reads the installed manifest's Version to offer updates).
"""

import glob
import json
import os
import shutil
import subprocess
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(REPO, "dist")
STAGE = os.path.join(DIST, "stage")


def run(args, cwd=None):
    print(">", " ".join(args))
    subprocess.check_call(args, cwd=cwd or REPO)


def stage_payload():
    mod = os.path.join(STAGE, "WrathAccess")
    asm = os.path.join(mod, "Assemblies")
    game = os.path.join(STAGE, "game")
    os.makedirs(asm)
    os.makedirs(game)

    shutil.copy2(os.path.join(REPO, "OwlcatModificationManifest.json"), mod)
    shutil.copy2(os.path.join(REPO, "OwlcatModificationSettings.json"), mod)
    shutil.copy2(os.path.join(REPO, "bin", "Release", "WrathAccess.dll"), asm)
    naudio = glob.glob(os.path.join(os.path.expanduser("~"), ".nuget", "packages",
                                    "naudio", "*", "lib", "net35", "NAudio.dll"))
    if not naudio:
        raise SystemExit("NAudio.dll not found in the NuGet cache — run a build first.")
    shutil.copy2(sorted(naudio)[-1], asm)
    shutil.copytree(os.path.join(REPO, "assets"), os.path.join(mod, "assets"))
    # Bundled documentation (built into deploy/docs by build_docs.ps1) ships inside the mod folder,
    # so Help > Read documentation opens it offline. Warn rather than fail if it hasn't been built.
    docs = os.path.join(REPO, "deploy", "docs")
    if os.path.isdir(docs):
        shutil.copytree(docs, os.path.join(mod, "docs"))
    else:
        print("WARNING: deploy/docs not found — run build_docs.ps1 to bundle the docs.")
    shutil.copy2(os.path.join(REPO, "vendor", "prism.dll"), game)


def make_zip():
    out = os.path.join(DIST, "WrathAccess.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _dirs, files in os.walk(STAGE):
            for f in files:
                full = os.path.join(root, f)
                z.write(full, os.path.relpath(full, STAGE))
    return out


def main():
    if os.path.isdir(DIST):
        shutil.rmtree(DIST)
    os.makedirs(DIST)

    run(["dotnet", "build", "-c", "Release"])
    stage_payload()
    zip_path = make_zip()
    shutil.rmtree(STAGE)

    run(["cargo", "build", "--release"], cwd=os.path.join(REPO, "installer"))
    shutil.copy2(os.path.join(REPO, "installer", "target", "release", "wrath-access-installer.exe"),
                 os.path.join(DIST, "WrathAccessInstaller.exe"))

    with open(os.path.join(REPO, "OwlcatModificationManifest.json"), encoding="utf-8-sig") as f:
        version = json.load(f)["Version"]
    print("\nDone. Artifacts in dist/:")
    print("  WrathAccess.zip + WrathAccessInstaller.exe")
    print("Publish: gh release create v%s dist/WrathAccess.zip dist/WrathAccessInstaller.exe" % version)


if __name__ == "__main__":
    main()
