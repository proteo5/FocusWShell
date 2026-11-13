---
layout: default
lang: en
ref: tablet
title: Using a tablet
permalink: /tablet/
---

# Using a tablet as the deck

The deck works on any screen: a corner of your main monitor, a touch screen, a second monitor. But it is best on a tablet next to your keyboard, where it is always visible and never covers your work.

For that, Windows has to see the tablet as **one more monitor**. FocusWShell does not stream anything itself; a remote display app does it.

## Tested: Apollo + Artemis (Android)

[Apollo](https://github.com/ClassicOldSong/Apollo) is a streaming server for Windows. When a device connects, it creates a **virtual monitor** with that device's resolution, and Windows treats it as a real extended monitor. **Artemis** is its client for Android.

1. Install **Apollo** on your PC and set a PIN or pair as its setup asks.
2. Install **Artemis** on the Android tablet and connect it to your PC.
3. Start the desktop stream. Windows now shows one more monitor, the tablet's.
4. In the deck, tap **⇢** and choose the tablet: **half** (its panels use the other half) or **whole**.

Touches on the tablet arrive as mouse clicks, and the deck is built for that: buttons of at least 48 points and drag-to-scroll. It does not steal the keyboard from your focus app.

## Should work, not tested yet

- **Moonlight** on an iPad or another device, connected to Apollo.
- **Sunshine** instead of Apollo, with a virtual display driver so the tablet extends the desktop instead of mirroring it.

If you try one of these, please tell us how it went in [Issues](https://github.com/proteo5/FocusWShell/issues).

## When the tablet disconnects

If the tablet goes away (Wi-Fi, sleep), its monitor disappears. FocusWShell keeps the apps of its panels safe and the deck goes back to the corner of the main monitor. When the tablet comes back, its panels and the deck come back too.
