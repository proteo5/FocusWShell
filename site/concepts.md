---
layout: default
lang: en
ref: concepts
title: How it works
permalink: /concepts/
---

# How it works

## Focus, panels and free windows

| | What it is | Where it goes |
| --- | --- | --- |
| **Focus** | The app you are working on. There is only one. | The large area of the main screen. |
| **Panels** | Named groups of apps you keep at hand: *Context*, *Work*, *Chat*… | Columns beside the focus, and your other monitors. |
| **Free** | Windows FocusWShell does not organize. | Wherever you leave them. |

When you bring an app to the focus, the one that was there **goes back to its panel**. A window never changes place on its own: a chat message, a notification or a click does not move it.

Apps in a panel can also be:

- **Parked:** open but set aside (minimized). They come back from their panel.
- **Out of layout:** you moved or resized one by hand; FocusWShell leaves it where you put it until you bring it back.

## The main screen layout

Your main screen has two columns of panels, one on each side of the focus: **Context** on the left and **Work** on the right. Turn each one on or off, and the focus takes the rest:

| Columns on | Screen |
| --- | --- |
| Both | Context · **focus** · Work |
| Context only | Context · **focus** |
| Work only | **focus** · Work |
| None | **focus** fills the screen (focus only) |

When a column is off, its apps are minimized and wait in their panel; tapping one brings it to the focus.

On an **ultrawide** monitor (21:9 or wider) there are two more: with one side off, an extra button brings the other column to that side, so both panels sit together and the focus goes to the edge: *Context · Work · focus*, or *focus · Work · Context*.

Each layout remembers its widths. Drag the border between two windows to change them; **Reset width** in the menu of the layout buttons goes back to the default.

## Panel presentations

Each panel shows its apps in one of these ways. Change it in the panel header in the deck.

- **▦ Tiles:** every app visible, side by side. Apps that do not fit go to rows you scroll from the panel strip.
- **⧉ Tabs:** one app at a time, the others behind it. Switch with the strip or with ◀ ▶ in the deck.
- **☰ List:** a list with thumbnails, like File Explorer. It also lists the closed apps pinned to that panel.
- **⛶ Fullscreen:** on monitors other than the main one, the panel's app fills the monitor.

## Strips

Above each panel there is a thin **strip** with one tab per app:

- **Click:** shows it in the panel (or brings it back if it was parked).
- **Double-click:** brings it to the focus.
- **Right-click:** everything else.

## Several monitors

The main monitor holds the focus and its two columns. Every other monitor can have its own panels, up to two per side. If a monitor disconnects (a tablet over Wi-Fi, a laptop leaving its dock), its apps are kept safe; when it comes back, its panels come back too.

## Always reversible

Before moving any window, FocusWShell writes down where it was. **Exit**, the emergency shortcut <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Backspace</kbd>, and reopening after a crash all put every window back.
