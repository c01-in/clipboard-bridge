<div align="center">

<img src="docs/assets/clipboard-bridge-hero.png" alt="Clipboard Bridge: a Windows PC and Android phone sharing text and a photo in both directions" width="100%">

# Clipboard Bridge

**Windows → Android, automatically. Android → Windows, with a tap.**

Text and one image. Your own devices. Free and open source.

[![Build](https://github.com/c01-in/clipboard-bridge/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/c01-in/clipboard-bridge/actions/workflows/ci.yml)
[![Preview 0.1.2](https://img.shields.io/badge/preview-0.1.2-8B5CF6?style=flat-square)](https://github.com/c01-in/clipboard-bridge/releases/tag/v0.1.2)
[![MIT](https://img.shields.io/badge/app_license-MIT-14B8A6?style=flat-square)](LICENSE)

[English](README.md) · [简体中文](README.zh-CN.md) · [Downloads](#download-and-try-it) · [Quick start](#install-pair-copy) · [Docs](#build-and-contribute)

<sub>AI-generated concept illustration above; not an app screenshot.</sub>

</div>

<p align="center">
  <img src="docs/assets/no-account.svg" width="270" alt="No sign-up: no account to create">
  <img src="docs/assets/no-background.svg" width="270" alt="No need to keep the Android app open: Google Play Services wakes it on demand">
  <img src="docs/assets/no-firebase.svg" width="270" alt="No Google/Firebase developer setup: no project or service-account key required">
</p>

> [!IMPORTANT]
> **Still powered by Google FCM.** No sign-up or Firebase setup does **not** mean Google-free: Android needs working Google Play Services, and both devices need Google network access. **Regions or networks without access to Google are currently unsupported.** The app needs no permanent Android foreground service or app-owned connection; Google Play Services handles wake-ups.

## Download and try it

<div align="center">

[![Download for Windows](https://img.shields.io/badge/Download_for_Windows-x64_EXE-6366F1?style=for-the-badge)](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.2/ClipboardBridge-Windows-x64-Setup.exe)
[![Download for Android](https://img.shields.io/badge/Download_for_Android-APK-14B8A6?style=for-the-badge&logo=android&logoColor=white)](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.2/ClipboardBridge.apk)

**Install both. Pair once. Start copying.**

[Manual Windows ZIP, library sources & SHA-256 checksums](https://github.com/c01-in/clipboard-bridge/releases/tag/v0.1.2)

</div>

**Current release: 0.1.2 preview.** No SDKs, local builds or phone debugging needed. Windows installation requires administrator permission; the installer is not Authenticode-signed. Android requires Android 8.0 or later and Google Play Services.

## Made for the clipboard

| | What you get |
| :--- | :--- |
| **🖥️ Copy on Windows** | Text or one image is sent automatically. Prefer control? Use `Ctrl+Alt+V` or the tray menu. |
| **📱 Send from Android** | Copy content, open the app and tap **Send clipboard to Windows**. Android sending is an explicit action. |
| **🔗 Pair without typing** | Find the computer on the same Wi-Fi, compare the six-digit code and confirm on both devices. No IP address or pairing link to enter. |
| **⏸️ Pause when you want** | Pause automatic Windows sending from the tray, or pause new sending and receiving on Android without losing your pairing. |
| **🔐 Keep content encrypted** | Clipboard content uses AES-GCM. Pairing keys and the computer's push identity stay local. Google can still observe push metadata. |
| **🌍 Use your language** | Both apps follow the system language, with nine supported locales and English as the fallback. |

Supports UTF-8 text and **one image, up to 16 MiB**. Images travel as PNG. Android image paste support depends on the receiving app. Oversized content is rejected rather than truncated or resized.

## Install. Pair. Copy.

1. **Install both apps.** Open Windows settings from the Start menu and launch Android. Connect both to the same Wi-Fi and allow notifications through the Android prompt.
2. **Confirm your devices.** Select the discovered computer on Android, compare the six-digit code on both screens and confirm on both. Do not approve a different code.
3. **Try your clipboard.** Copy text or one image on Windows, then paste on Android. For the other direction, copy on Android and tap **Send clipboard to Windows**; success appears after Windows actually writes the clipboard and acknowledges it.

No app account, Firebase project, credential import or debug mode is required for normal use. Notification prompts, pairing confirmation and any Android installation permission still need your approval.

> [!TIP]
> **Start on the same Wi-Fi.** Clipboard content travels directly between your devices when needed. No additional networking app is required for normal local use. Google connectivity is still required.

## Push without a project to manage

Windows generates its own VAPID key and sends encrypted Web Push directly to Google. UnifiedPush's embedded receiver uses Google FCM infrastructure and existing Google Play Services to wake Android when needed.

**No maintainer relay. No shared sender secret. No Firebase service-account file.**

Long text and images are fetched directly from Windows over your local network. Initial discovery requires the same Wi-Fi broadcast domain. This transport does not repair Google connectivity failures. See [push and migration](docs/webpush.md).

<details>
<summary><strong>Optional: using your devices across networks</strong></summary>

For cross-network use, an existing private network such as Tailscale can carry the direct connection. Add the computer's reachable private-network address in advanced settings before leaving the LAN. This is optional and is not needed when both devices use the same Wi-Fi. Google connectivity is still required.

</details>

<details>
<summary><strong>Pause behavior & Android permissions</strong></summary>

- **Windows tray pause** affects automatic Windows sending only. Manual sending and receiving from Android remain available.
- **Android's Clipboard sync switch** pauses new sending and receiving without deleting pairing. The choice persists across app and device restarts.
- **Resume starts a fresh receive session.** Previously queued updates are discarded instead of overwriting the clipboard later. Sending becomes available after the computer confirms registration.

Android shows status, the sync switch and Send, plus pairing when needed. Change computer, Reconnect and About are in the overflow menu. Notification permission is requested in the app; system settings are needed only after a permanent denial or disabled channel.

</details>

<details>
<summary><strong>Nine system languages</strong></summary>

English · 简体中文 · 繁體中文 · Español · Português · Français · Deutsch · 日本語 · 한국어

Both apps follow the system language. Other languages fall back to English.

</details>

<details>
<summary><strong>Upgrading, privacy & licenses</strong></summary>

Windows and Android share version 0.1.2. Android internal version codes keep increasing and the existing signing key is reused. Update Windows first, then Android and open it once; existing pairing migrates automatically. Legacy Firebase configuration is preserved locally in `configuration.pre-webpush.dpapi` and is no longer used for push.

Keys, subscription endpoints and runtime data stay local and are excluded from source and public installers. Clipboard content is AES-GCM encrypted, but Google can observe push metadata. Use trusted local or private networks. See [SECURITY.md](SECURITY.md).

Our code is [MIT](LICENSE). The embedded UnifiedPush receiver retains LGPL-2.1; the connector uses Apache-2.0. Both full licenses are inside the APK and original library sources accompany each Release. See [third-party notices](THIRD_PARTY_NOTICES.md). Version 0.1.2 corrects the license packaging in 0.1.1 without changing delivery behavior.

</details>

## Tested, with limits stated

**85 automated checks pass**, covering protocol, IPC, pairing, Web Push, pause revision, migration, licenses, locales and actual HTTP downloads.

- **Native push probe:** six short messages arrived, including process termination and forced deep Doze. [Probe evidence](experiments/webpush-probe/RESULTS.md)
- **Production device tests:** pairing migration, persistent pause, obsolete-session rejection, and text/image transfers in both directions passed. [Device evidence](docs/verification-0.1.1.md)
- **Release automation:** tests, Android signing, packaging, fresh Windows installation and uninstallation run before publishing. [GitHub Actions](https://github.com/c01-in/clipboard-bridge/actions/workflows/release.yml)

All vendors, prolonged sleep, reboot recovery and cross-network combinations have **not** been validated. This is a preview, not a guarantee of long-term delivery.

## Build and contribute

| Start here | Read more |
| :--- | :--- |
| [Build, install & uninstall](docs/build.md) | [Protocol](docs/protocol.md) |
| [GitHub Actions releases](docs/releases.md) | [Troubleshooting](docs/troubleshooting.md) |
| [Contributing](CONTRIBUTING.md) | [Security](SECURITY.md) |

Matching `v0.x.y` tags automatically test, sign, package and publish both apps. Users download installers; maintainers do not need to build every release locally.

---

<div align="center">

**One clipboard bridge. Two devices.**

[Download the preview](#download-and-try-it) · [Report an issue](https://github.com/c01-in/clipboard-bridge/issues) · [Read the Chinese guide](README.zh-CN.md)

</div>
