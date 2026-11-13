---
layout: default
lang: en
ref: install
title: Install
permalink: /install/
---

# Install

## Requirements

- **Windows 10 (version 2004) or Windows 11, 64-bit.** It has been tested on Windows 11.
- No administrator rights. Everything is installed for your user only.
- Nothing else: .NET is included in the download.
- Optional: a tablet to use as the deck (see [Using a tablet]({{ '/tablet/' | relative_url }})).

## Install

1. Download `FocusWShell-<version>-win-x64.zip` from [Releases](https://github.com/proteo5/FocusWShell/releases).
2. Extract the **whole** zip to a folder. Use a new folder for each version.
3. Double-click **install.cmd**.

The installer copies FocusWShell to `%LOCALAPPDATA%\Programs\FocusWShell`, adds it to the Start menu and opens it. It appears as an icon in the tray, next to the clock.

### "Windows protected your PC"

The files are not signed yet, so Windows may warn you the first time, because they come from the internet. Choose **More info → Run anyway**. You can check the download against the `.sha256` file published next to the zip:

```powershell
Get-FileHash .\FocusWShell-0.1.0-alpha-win-x64.zip -Algorithm SHA256
```

## Start with Windows

Right-click the tray icon and choose **Start with Windows**. FocusWShell will open when you sign in.

## Update

Install the new version the same way: extract its zip to a new folder and run its `install.cmd`. The installer closes FocusWShell first (putting your windows back) and keeps your settings.

## Uninstall

1. Open `%LOCALAPPDATA%\Programs\FocusWShell`.
2. Right-click **uninstall.ps1 → Run with PowerShell**.

It closes FocusWShell, putting every window back, and removes *Start with Windows*, the Start menu shortcut and the app. Your settings stay in `%LOCALAPPDATA%\FocusWShell`, in case you come back. To delete them too, run it from PowerShell with `-RemoveSettings`:

```powershell
& "$env:LOCALAPPDATA\Programs\FocusWShell\uninstall.ps1" -RemoveSettings
```

## Your settings

Everything FocusWShell remembers lives in `%LOCALAPPDATA%\FocusWShell`: your panels, the apps it has seen, your shortcuts and preferences. They are plain JSON files. `errors.log` appears there only if something fails.
