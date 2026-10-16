# Clipboard Bridge

[简体中文](README.md)

Automatically send Windows clipboard text or one image to Android. On Android, tap Send in the app to place the clipboard on Windows. Devices discover each other and pair with confirmation on both screens; no addresses or pairing links to enter.

## Download the 0.1.1 preview

- [Android APK](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.1/ClipboardBridge.apk)
- [Windows x64 installer](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.1/ClipboardBridge-Windows-x64-Setup.exe)
- [Manual ZIP, library sources and checksums](https://github.com/c01-in/clipboard-bridge/releases/tag/v0.1.1)

Users do not need SDKs, local builds or phone debugging. Windows installation requires administrator permission. The installer is not Authenticode-signed.

## Use

1. Install Windows and open Clipboard Bridge settings from the Start menu.
2. Install and open Android, allow notifications through the in-app prompt, and connect both devices to the same Wi-Fi.
3. Select the discovered computer, compare the six-digit code, and confirm on both screens. Do not approve a different code.
4. Copy text or one image on Windows for automatic sending, or use `Ctrl+Alt+V` or the tray menu.
5. On Android, copy content and tap Send clipboard to Windows. Success appears only after Windows actually writes its clipboard and acknowledges it.

Supports UTF-8 text and one image, up to 16 MiB. Images are transported as PNG. Android image paste support depends on the receiving app. Oversized content is rejected rather than truncated or resized.

## Push without Firebase setup

Version 0.1.1 uses UnifiedPush's embedded receiver, Web Push and VAPID. Each Windows computer generates its own private key and sends directly to Google; existing Google Play Services wakes Android.
No Firebase project, service-account JSON, shared sender key or maintainer relay is required. The Android app needs no permanent connection or foreground receive service.

Long text and images are fetched directly from Windows over LAN or Tailscale. Initial discovery requires the same Wi-Fi broadcast domain; add a Tailscale address in advanced settings before crossing networks.
Google connectivity is still required. This transport does not fix Google network failures. See [push and migration](docs/webpush.md).

## Pause

- Windows tray pause affects automatic Windows sending only. Manual sending and receiving from Android remain available.
- Android's main-screen Clipboard sync switch pauses new sending and receiving without deleting pairing. The choice persists across app and device restarts.
- Resume creates a new receive session. Previously queued updates are discarded instead of overwriting the clipboard later. Sending becomes available after the computer confirms registration.

Android shows status, the sync switch and Send, plus pairing when needed. Change computer, Reconnect and About are in the overflow menu. Notification permission is requested in the app; system settings are needed only after a permanent denial or disabled channel.

Both apps follow the system language: English, Simplified/Traditional Chinese, Spanish, Portuguese, French, German, Japanese and Korean. Other languages fall back to English.

## Upgrade and privacy

Windows and Android share version 0.1.1. Android internal version codes keep increasing and the existing signing key is reused. Update Windows first, then Android and open it once; existing pairing migrates automatically.
Legacy Firebase configuration is preserved locally in `configuration.pre-webpush.dpapi` and is no longer used for push.

Keys, subscription endpoints and runtime data stay local and are excluded from source and public installers. Clipboard content is AES-GCM encrypted, but Google can observe push metadata. Use trusted LAN/Tailscale networks. See [SECURITY.md](SECURITY.md).

Our code is [MIT](LICENSE). UnifiedPush libraries retain LGPL-2.1; the full license is inside the APK and original library sources accompany each Release. See [third-party notices](THIRD_PARTY_NOTICES.md).

## Development

- [Build, install and uninstall](docs/build.md)
- [GitHub Actions releases](docs/releases.md): matching `v0.x.y` tags automatically test, sign, package and publish both apps.
- [Protocol](docs/protocol.md), [troubleshooting](docs/troubleshooting.md), [contributing](CONTRIBUTING.md)

83 protocol, IPC, pairing, Web Push, pause revision, migration, locale and actual HTTP download checks pass. All six short native probe messages arrived, including process termination and forced deep Doze; see [probe evidence](experiments/webpush-probe/RESULTS.md). Production pairing migration, persistent pause, obsolete-session rejection, and text/image transfers in both directions also passed; see [0.1.1 device evidence](docs/verification-0.1.1.md).
All vendors, prolonged sleep, reboot recovery and cross-network combinations have not been validated. This is a preview, not a guarantee of long-term delivery.
