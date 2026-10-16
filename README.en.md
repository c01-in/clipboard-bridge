# Clipboard Bridge

Copy on Windows, paste on Android. Copy on Android, tap Send in the app, then paste on Windows. Text and one image at a time, without a persistent Android receiver service.

[中文](README.md) · [Security](SECURITY.md) · [Contributing](CONTRIBUTING.md)

**0.1.0 preview.** Both apps use a 0.x public version while testing continues. Android internal version codes still increase, so existing internal builds can be updated in place.

- [Android APK](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.0/ClipboardBridge.apk)
- [Windows x64 installer EXE](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.0/ClipboardBridge-Windows-x64-Setup.exe)
- [All previews and checksums](https://github.com/c01-in/clipboard-bridge/releases)

Download and install without compiling or enabling debug mode. The Windows installer requests administrator permission and installs the service, tray and Start menu entry. It is not Authenticode-signed. No personal Firebase credentials are bundled: this preview still uses your own Firebase project for background delivery. A public zero-configuration push backend has not been deployed.

## Features

- Automatic discovery on the same Wi-Fi. No IP address, pairing link or debug mode is required. Compare the six-digit code and confirm on both screens.
- System-language UI: English, Simplified and Traditional Chinese, Spanish, Portuguese, French, German, Japanese and Korean. Other locales fall back to English. Windows uses the same translations.
- Windows tray clipboard monitoring, pause control and `Ctrl+Alt+V` manual sending.
- Android's **发送剪贴板到 Windows** (Send clipboard to Windows) button uploads encrypted text or one image over LAN/Tailscale. Success requires an actual desktop clipboard-write acknowledgment.
- Text, native clipboard images and one copied PNG/JPEG/BMP/GIF file; images are converted to PNG. Maximum content size: 16 MiB.
- AES-256-GCM encrypted content, integrity checks, expiry and sequence protection.
- Separate states for FCM submission and Android clipboard-write acknowledgment.
- LAN access, with optional user-configured Tailscale endpoints.
- Desktop echo suppression prevents received phone content from being automatically sent back. Pausing Windows automatic sending still allows incoming phone transfers.

![Tray icons](windows/Tray/Assets/icon-preview.png)

## Bring your own Firebase project

There is no shared hosted backend or maintainer credential in this repository.

1. Download and run the Windows installer, then choose Install or update. No SDK installation is required for normal use.
2. Install the Android APK. On first opening, the app explains notifications and requests Android permission. Only if Android can no longer show a prompt does it offer notification settings. The main screen shows Send and status; Change computer, Reconnect and About are in the overflow menu. Source builds are optional:

```powershell
git clone https://github.com/c01-in/clipboard-bridge.git
cd clipboard-bridge
.\scripts\Build.ps1 -JavaHome 'C:\Tools\jdk-21' -AndroidSdk 'C:\Tools\android-sdk'
# Administrator PowerShell:
.\dist\scripts\Install.ps1
```

3. Create your own Firebase project. Register the Android package **`com.colin.clipbridge`** and download `google-services.json`. Ensure the FCM HTTP v1 API is enabled.
4. Create a dedicated service account in the same project with `roles/firebasecloudmessaging.admin`, generate its JSON key and keep it under `local/credentials/`. Do not put the private key on Android.
5. Install `dist/ClipboardBridge.apk` on Android, or use Install or update phone app in Windows settings to scan a download-only QR code. Under Advanced settings, import your service-account JSON and Android Firebase JSON once.
6. Open the phone app and Windows settings on the same Wi-Fi. Select the discovered device, compare the six-digit code and confirm on both screens. Allow notifications and any local-network permission requested by Android. Optional Tailscale addresses belong in Advanced settings.
7. Copy text, wait for acknowledgment and paste on the phone. Then test one image.
8. For the reverse direction, copy text or one image on Android, open the app and tap Send clipboard to Windows. Wait for confirmation, then paste on the computer. Use matching current previews on both devices; the Windows tray must be running. Existing pairing remains valid.

The Android Firebase configuration is transferred inside the confirmed encrypted pairing session, so builds do not require `google-services.json` in the source tree. Keep the same signing key for APK upgrades. Installation, pairing and normal operation do not require ADB.

Detailed setup, build and troubleshooting instructions are available in the [Chinese documentation](docs/firebase-setup.md), with links to official Firebase documentation.

## Important limits

- FCM and Google Play services must be available; instant delivery is not guaranteed.
- Longer Windows → Android text, images, acknowledgments and all Android → Windows sends require a reachable LAN or Tailscale connection. Reverse sending does not use FCM. Do not expose port 45871 directly to the public internet.
- One trusted phone per computer. Android sends require an explicit in-app tap; no Android background clipboard monitoring, arbitrary files, multiple images or iOS support.
- A destination app must support image pasting. Force-stopped Android apps need to be opened again before receiving push messages.
- Automatic sync can include sensitive copied text. Pause before copying secrets. Older internal pairing links remain secrets; this preview uses ephemeral key agreement and approval on both screens.

## Private local data

`local/` and `dist/` are ignored by Git. Runtime credentials and pairing live in `local/runtime/`; APK signing lives in `local/signing/`; original Firebase JSON files belong in `local/credentials/`.

Runtime Windows configuration uses machine-bound DPAPI plus filesystem permissions; Android pairing uses Keystore protection. Ignoring a file is not encryption. Read [SECURITY.md](SECURITY.md) before backing up or publishing files.

## Checks

```powershell
dotnet run --project .\windows\Tests\Tests.csproj -c Release
.\android\gradlew.bat -p .\android assembleDebug lintDebug
.\scripts\Check-Publish.ps1 -IncludeHistory
```

CI builds and checks the code without Firebase credentials. Live delivery and actual pasting require your own configured devices and are not implied by a passing CI build.

Checks compile the actual Android Java key-agreement implementation and verify interoperability with Windows, approval gates, replacement-key revocation, retry recovery, download behavior and all locale keys and placeholders. The new phone UI, permission prompts and discovery flow still require physical-device validation; earlier successful clipboard tests do not establish that result.

Pushing a `v0.x.y` tag triggers [GitHub Actions](.github/workflows/release.yml) to check, build signed apps, package and test the Windows installer, and publish a GitHub prerelease with SHA-256 checksums. Maintainers do not need to build each release locally. See [release setup](docs/releases.md).

## License

[MIT](LICENSE): free to use, modify and redistribute, including commercial use, with the license notice retained. Third-party dependencies keep their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
