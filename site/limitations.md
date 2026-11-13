---
layout: default
lang: en
ref: limitations
title: Known limitations
permalink: /limitations/
---

# Known limitations

FocusWShell is an alpha. These are the things we know about.

## Virtual desktops

FocusWShell knows about Windows virtual desktops only partly:

- It **never opens a second copy** of an app that is on another desktop: it takes you to it instead.
- In the deck, apps on another desktop are dimmed with **⧉**.
- But there is still **one focus for all desktops**, and the panel strips may show tabs from another desktop.

Full support (a focus and apps per desktop, and desktop tabs in the deck) is planned. Until then, it works best on a single desktop.

## Apps that cannot be organized

- **Apps running as administrator** cannot be moved by FocusWShell, which runs without elevation. They are listed with the reason, and stay where they are.
- **Fullscreen games and presentations:** when an app goes fullscreen by itself (F11, a game), FocusWShell leaves that monitor alone until it exits. This has been tested with browsers; games and presentations have not been tested much yet.
- Some apps have a **fixed size** or a large minimum size. FocusWShell learns it and makes room, or leaves them out of the layout with a note.

## Browser pages

- Only **Chrome and Edge**. Firefox and others are not supported.
- The site of each page is guessed from its title, so some pages may appear as single pages.
- Tested with vertical tabs. Horizontal tabs and collapsed tab groups have not been tested much.

## Other

- Tested on **Windows 11**. Windows 10 should work but has not been tested.
- Tested mostly on one setup: an ultrawide main monitor, a second monitor and a tablet as the deck. Other setups are exactly what this alpha is for.
- The files are **not signed**, so Windows may warn you when you install.

Found something else? Please [tell us]({{ '/feedback/' | relative_url }}).
