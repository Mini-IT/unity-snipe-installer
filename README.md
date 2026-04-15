# Snipe Installer

Unity installer package for [Snipe](https://github.com/Mini-IT/snipeUnityPackage/).

## Installation

1. Download [`SnipeInstaller.unitypackage`](Installer/SnipeInstaller.unitypackage).
2. Open your Unity project.
3. Import the downloaded package into the project.

After import, Unity runs the installer script automatically. The installer adds the required Snipe dependencies through Unity Package Manager, then deletes itself from `Assets/Editor/InstallSnipePackages.cs`.

Progress is written to the Unity Console with the `Snipe Installer:` prefix.

## Notes

- Keep Unity open until the installer finishes.
- The project must have access to GitHub and OpenUPM while packages are being installed.
- If installation fails, check the Unity Console for the package name or registry error reported by the installer.
