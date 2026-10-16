# Contributing to Clipboard Bridge

Bug fixes, documentation, translations and usability improvements are welcome. For a bug, describe how to reproduce it and what you expected to happen. For a substantial feature or protocol change, open an issue first so its scope and compatibility can be discussed.

## Understand the current behavior

- Windows sends copied text or one image automatically, with a tray action and `Ctrl+Alt+V` for manual sending.
- Android sends only when the user opens the app and taps **Send clipboard to Windows**. It receives through Google Play Services without a permanent app-owned foreground service or connection.
- Pairing uses local discovery and a six-digit code confirmed on both devices. Normal installation and use do not require ADB or wireless debugging.
- No app account, Firebase project, service-account file or maintainer relay is required. Each computer generates its own VAPID identity and sends encrypted Web Push directly to Google. Google Play Services and Google network access are still required.

Read the [README](README.md), [protocol](docs/protocol.md), [push and migration guide](docs/webpush.md) and [security notes](SECURITY.md) before changing pairing, delivery or stored configuration.

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

## Verify behavior on real devices

For delivery changes, test with your own Windows PC and Android device on the same Wi-Fi, with working Google connectivity. Pair through the normal app flow; there is no Firebase configuration to import. Record the app versions, Android and Google Play Services versions, network conditions, exact steps and observed result.

Keep these outcomes separate in your report:

1. Google accepted the push request.
2. The receiving device wrote the clipboard and returned an authenticated acknowledgment.
3. The destination app successfully pasted the text or image.

A successful build or push submission alone does not prove clipboard delivery. Test both directions and any affected pause/resume, upgrade or pairing behavior. Clipboard input scripts overwrite the current clipboard and may trigger automatic sending; use only your own test content and devices. State any behavior you could not verify, especially prolonged sleep, reboot recovery or vendor-specific restrictions.

## Submit a pull request

- Describe the problem, resulting behavior and actual validation. Include relevant CI results and disclose failed or unrun checks.
- Keep the change focused. Update or remove obsolete code and documentation when replacing a behavior; do not add simulated success paths to satisfy checks.
- Preserve existing pairing, stored credentials and signing identities during upgrades. Document any necessary migration.
- Use English for default UI text and documentation. Update all affected supported locales, keeping resource keys and format placeholders consistent.
- Never commit `local/`, `dist/`, signing keys, passwords, Firebase credentials, push subscription endpoints, pairing secrets, raw device logs or screenshots containing private data. Review the diff and run the publish check before pushing; the scan is not a complete security audit.
- Our application code is MIT-licensed. Preserve third-party attribution and license requirements, including Apache-2.0 for the UnifiedPush connector and LGPL-2.1 for the embedded FCM distributor. See [third-party notices](THIRD_PARTY_NOTICES.md).

## Releases and security reports

Maintainers publish preview installers through the [release workflow](docs/releases.md), using matching `v0.x.y` tags and the existing signing key. Android and Windows visible versions must match, and Android's internal `versionCode` must keep increasing. Contributors do not need access to release secrets. A GitHub Release or successful CI run does not imply Google Play approval.

For security issues, follow [SECURITY.md](SECURITY.md). Keep real clipboard content, device identifiers and credentials out of public issues; provide redacted reproduction details instead.
