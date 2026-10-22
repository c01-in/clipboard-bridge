# Contributing to Clipboard Bridge

Bug fixes, documentation, translations and usability improvements are welcome. For a bug, describe how to reproduce it and what you expected to happen. For a substantial feature or protocol change, open an issue first so its scope and compatibility can be discussed.

## Understand the current behavior

- Windows sends copied text or one image automatically, with a tray action and `Ctrl+Alt+V` for manual sending.
- Android reads the clipboard only for a foreground Send action. The system Share target also accepts explicitly supplied content: one shared image goes to the computer clipboard; shared plain text requires a Send tap. Neither entry needs a background clipboard listener.
- Version 0.2.0 sends multiple photos, videos, documents and other files in both directions. Android uses its system picker or Share target; Windows uses its tray picker, native Share target or the desktop installer’s classic Send to entry. File-mode transfers save original bytes to storage, never the clipboard; even one image stays a file when selected through Send files.
- Windows receives into Downloads or a folder selected in Settings. Android 10+ defaults to Downloads/Clipboard Bridge; Android 8–9 require a receiving-folder selection. Both endpoints report progress and require a saved-file receipt for success. The Windows send window closes three seconds after success, while failures and cancellations remain visible.
- Android receives through Google Play Services without a permanent app-owned foreground service or connection. Active file transfers use temporary foreground work and progress notifications, which stop afterward. Pairing and pause behavior must be preserved.
- Pairing uses local discovery and a six-digit code confirmed on both devices. Normal installation and use do not require ADB or wireless debugging.
- No app account, Firebase project, service-account file or maintainer relay is required. Each computer generates its own VAPID identity and sends encrypted Web Push directly to Google. Google Play Services and Google network access are still required.

Read the [README](README.md), [protocol](docs/protocol.md), [file-transfer behavior](docs/file-transfers.md), [Windows Share integration](docs/windows-sharing.md), [push and migration guide](docs/webpush.md) and [security notes](SECURITY.md) before changing pairing, delivery or stored configuration. The Store edition uses a per-user broker and encrypted state, while the standalone desktop edition uses a Windows service. Both reuse one bundled .NET runtime; do not add duplicate runtime copies.

## Set up and check your changes

Use Windows 10/11 x64, PowerShell 7+, .NET SDK 10.0, JDK 21 and Android SDK Platform 36 with Build Tools 36.0.0. Set `JAVA_HOME` and `ANDROID_HOME` to your own installations. The repository includes the Gradle wrapper; see the [build guide](docs/build.md) for installation and packaging details.

Run the checks relevant to your change from the repository root. The following commands match the builds and checks used by CI:

```powershell
dotnet build .\windows\Service\Service.csproj -c Release
dotnet build .\windows\Tray\Tray.csproj -c Release
dotnet build .\windows\Cli\Cli.csproj -c Release
dotnet run --project .\windows\Tests\Tests.csproj -c Release
.\android\gradlew.bat -p .\android assembleDebug lintDebug --no-daemon --console=plain
.\scripts\Check-Publish.ps1 -IncludeHistory
```

Debug builds and checks require no maintainer credentials or release signing key. A locally signed release uses your own persistent key; it cannot update an APK signed with the project's release key. Do not delete an existing installation's data or signing material just to make a test pass.

Android debug builds use `com.colin.clipbridge.debug` and separate storage, so they can coexist with the release app. For emulator checks of the system share target and image URI permissions, see [Android sharing verification](docs/android-sharing.md). Instrumentation fixtures are packaged only in the test APK.

## Verify behavior on real devices

For delivery changes, test with your own Windows PC and Android device on the same Wi-Fi, with working Google connectivity. Pair through the normal app flow; there is no Firebase configuration to import. Record the app versions, Android and Google Play Services versions, network conditions, exact steps and observed result.

Keep these outcomes separate in your report:

1. Google accepted the push request.
2. The receiving device wrote the clipboard and returned an authenticated acknowledgment.
3. The destination app successfully pasted the text or image.

A successful build or push submission alone does not prove clipboard delivery. Test both directions and any affected pause/resume, upgrade or pairing behavior. Clipboard input scripts overwrite the current clipboard and may trigger automatic sending; use only your own test content and devices. State any behavior you could not verify, especially prolonged sleep, reboot recovery or vendor-specific restrictions.

For file-transfer changes, verify the bytes saved at the real destination, SHA-256, multiple files, an empty file, a file over 16 MiB, cancellation, name collisions and the chosen receiving folder. Verify clipboard/file routing separately. A received HTTP chunk is not a successful saved transfer. Record system Share activation separately from protocol tests, and distinguish emulator wake-up substitutes from real Google push delivery.

## Submit a pull request

- Describe the problem, resulting behavior and actual validation. Include relevant CI results and disclose failed or unrun checks.
- Keep the change focused. Update or remove obsolete code and documentation when replacing a behavior; do not add simulated success paths to satisfy checks.
- Preserve existing pairing, stored credentials and signing identities during upgrades. Document any necessary migration.
- Use English for default UI text and documentation. Update all affected supported locales, keeping resource keys and format placeholders consistent.
- For every Android release, update the matching Play product title/short/full descriptions and What’s new alongside GitHub notes. From 0.2.0, tag-owned `.play-listing.json` and `.play.txt` files are required; CI commits them with the AAB and rejects invalid or mismatched text. Keep current-source features separate from downloadable releases and store approval.
- Never commit `local/`, `dist/`, signing keys, passwords, Firebase credentials, push subscription endpoints, pairing secrets, raw device logs or screenshots containing private data. Review the diff and run the publish check before pushing; the scan is not a complete security audit.
- Our application code is MIT-licensed. Preserve third-party attribution and license requirements, including Apache-2.0 for the UnifiedPush connector and LGPL-2.1 for the embedded FCM distributor. See [third-party notices](THIRD_PARTY_NOTICES.md).

## Releases and security reports

The latest public GitHub installers remain 0.1.4. Source/review builds are 0.2.0, and Windows 0.2.0.0 has been submitted to Microsoft Store certification; it is not live yet. Maintainers use the separate [release channels](docs/releases.md) and [Store workflow](docs/microsoft-store.md). Store update uploads remain disabled until the initial publication is complete; standalone signed EXE publication requires approved SignPath configuration. Do not push a release tag as a documentation or build-review step.

Android and Windows visible versions must match, and Android's internal `versionCode` must keep increasing. Contributors do not need access to release secrets. A GitHub Release or successful CI run does not imply Google Play or Microsoft Store approval.

For security issues, follow [SECURITY.md](SECURITY.md). Keep real clipboard content, device identifiers and credentials out of public issues; provide redacted reproduction details instead.
