---
layout: default
lang: en
ref: main-window
title: Main window and settings
permalink: /main-window/
---

# Main window and settings

The deck is for everyday use; the main window is for setting FocusWShell up and seeing how things are. Open it by double-clicking the tray icon or with **Open FocusWShell** in its menu. Closing it does not stop FocusWShell: it keeps running in the tray.

It follows the look of Windows 11, in a dark or light theme, with its sections on the left.

![The main window, Home]({{ '/assets/img/en/main-home.png' | relative_url }})

## Home

- **Your monitors:** a map of each monitor. The main one shows its panel columns in the order and width of the current layout, and the app in focus; the others show their panels. Each panel says how many apps it has.
- **Main screen layout:** the same column toggles as in the deck, the current widths and **Reset width**.
- **Quick actions:** open the deck, back to the previous app, focus to panel, free all, rearrange all.
- A reminder of the emergency shortcut.

## Windows

Every window FocusWShell can organize, with its app's icon, its title and its state. **Choose a panel and it applies right away**: *free*, *to the focus* or any panel. The other actions are in **…** at the end of the row, or on right-click:

- to the focus, or fullscreen on a monitor;
- park or bring back, re-fit, free it;
- how its size is handled (some apps have a fixed size);
- exclude its app or its kind of window.

Windows that cannot be organized are listed too, with the reason: for example, apps running as administrator.

## Panels

Create, rename, move and delete panels. Each one has a place (monitor, left or right of the focus on the main monitor, top or bottom, up to two per column) and a presentation. Panels on other monitors can be *Fullscreen*.

## Apps

Every app FocusWShell has seen, with its icon. Rename how it is shown, open it in a panel, hide it from the deck or forget it.

## Shortcuts and gestures

The shortcuts in use, and **Change shortcuts…** to set new ones. Under *Windows gestures*, choose whether Alt+Tab, Task view, the taskbar or a click bring a panel's window to the focus. See [Shortcuts]({{ '/shortcuts/' | relative_url }}).

## Scenes

Apply, delete and save scenes: which apps are in each panel and in the focus. They also apply with <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>1</kbd>…<kbd>9</kbd>.

## Excluded

Apps FocusWShell should never touch, by executable, AUMID or window class.

## General

![The main window, General]({{ '/assets/img/en/main-general.png' | relative_url }})

- **Language:** automatic (the one of Windows), English or Spanish. It applies when FocusWShell restarts.
- **Theme:** automatic (follows Windows), light or dark. It applies right away. The deck keeps its own look.
- **Start with Windows** and **Keep the screen on**.
- **About:** the version and the build, links to this manual, the releases, reporting a problem, the source code and the settings folder, the license and the open source components it uses.

## Tray menu

- The version, on the first line: please mention it when you report a problem.
- **Open FocusWShell**, **Deck**.
- **Keep the screen on.**
- **Free all.**
- **Start with Windows.**
- **Exit (puts the windows back).**

## Messages

What just happened (or what could not be done) appears in a strip at the top of the window, and goes away by itself.
