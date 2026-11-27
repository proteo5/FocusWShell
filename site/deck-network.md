---
layout: default
lang: en
ref: deck-network
title: Deck on the network
permalink: /deck-network/
---

# Deck on the network

Use the deck from a tablet or a phone on your local network, without Apollo. FocusWShell serves the deck as a web page: you open it in the tablet's browser and tap, and FocusWShell does the same as with the deck on the PC. It works the same on Android and on iPad, with nothing to install on the tablet.

It's **off** until you turn it on, and it only answers the devices you pair.

## Turn it on and pair a tablet

1. In the main window, open **Deck on the network** and turn on **Serve the deck on the local network**.
2. The first time, Windows may ask for Firewall permission. Allow **private networks** only.
3. It only listens on networks that Windows marks as **private** (or domain). If your home Wi-Fi is marked as public, the section says so and has a button to the Windows network settings: open your network and choose **Private network**. The deck starts listening by itself.
4. Tap **Pair**. A QR code and a 6-digit code appear; they work once and expire in 5 minutes.
5. On the tablet (on the same Wi-Fi), scan the QR code with the camera, or open the address shown and type the code. Give the device a name and tap **Pair**.
6. The deck opens. Add it to the home screen from the browser menu, so it opens straight into the deck next time.

The tablet stays paired. To remove it, tap **Remove** next to it in **Deck on the network**: it is disconnected at once.

## What it does

The same as the deck on the PC, with every monitor at once:

- **Left:** the app in focus, **Previous**, **To panel**, **Free all** and **Move…**.
- **Right:** each monitor with its panels, and below, **Free** and **🗂 Pages**.
- **Tap an app** to bring it to the focus. In a panel in tabs, tapping a hidden tab shows it in its panel; a closed pinned app opens in its panel; a free one comes to the front.
- **Move…:** tap the app, then the panel where it goes, the focus, or **Free** to free it.
- **⛶** enters and leaves fullscreen.

In landscape it uses two columns, like the deck on the PC; on a phone or a tablet in portrait, one.

Not in this version yet: the Pin, Minimize and Close modes, the main screen layout and the power buttons. Use the deck on the PC for those.

## Browser pages

**🗂 Pages** only appears if you turn on **Show the browser pages** in **Deck on the network**. It's off by default because the page titles travel over your network without encryption. Private windows never show their titles.

## Keep the screen on

For now the page can't keep the tablet's screen on (browsers only allow it over an encrypted connection). Raise the screen timeout in the tablet's settings.

## If the tablet doesn't connect

- **Is the network private?** The section **Deck on the network** says so if Windows marks it as public.
- **The Windows Firewall** must allow FocusWShell on **private** networks. If you first turned it on while the network was marked as public, Windows allowed it for public networks only, and doesn't ask again. Fix it in **Control Panel → Windows Defender Firewall → Allow an app or feature…**: tap **Change settings**, find **focuswshell.exe** and check **Private** (uncheck **Public**). Or, in PowerShell as administrator:

  ```powershell
  Get-NetFirewallRule -DisplayName 'focuswshell.exe' | Set-NetFirewallRule -Profile Private
  ```
- **Same Wi-Fi:** the tablet must be on the same network as the PC.

## Privacy and security

- FocusWShell doesn't connect to the internet. With the deck on the network on, it listens on your local network, on port 8765, and only for the devices you paired.
- It only listens on networks that Windows marks as private or domain, never on a public one (a café, an airport), and only answers addresses of the local network.
- After 5 wrong pairing codes, the code is cancelled, and that address has to wait 10 minutes.
- It only accepts requests from its own page: another website open on the tablet can't talk to your PC.
- Each device has its own key, which only the device keeps. Removing it cuts it off at once.
- The connection is not encrypted (plain HTTP). Use it on a network you trust, such as your home Wi-Fi.
