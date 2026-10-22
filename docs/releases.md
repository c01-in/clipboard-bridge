# Automated builds and releases

Users install the Android APK or Microsoft-signed Windows online installer from [GitHub Releases](https://github.com/c01-in/clipboard-bridge/releases), without a build environment. Windows is also available directly from [Microsoft Store](https://apps.microsoft.com/detail/9PJT9PTSMCJQ). Versions remain 0.x previews. The current version is **0.2.0**; GitHub publication is separate from Google Play submission and approval.

Windows **0.2.0.0 is published in Microsoft Store**. The official Store installer downloads the current Store version rather than pinning a version in an offline bundle. See [0.2.0 feature notes](release-notes/v0.2.0.md) and [Store setup/status](microsoft-store.md).

| Channel | Current state and requirements |
| --- | --- |
| GitHub APK / Windows download | Android uses its persistent release signature. The Windows download is the unmodified Microsoft-signed Store online installer. Old standalone EXEs remain unsigned and are not the new download. |
| Google Play | Existing API publisher configured. From 0.2.0, the release AAB, product descriptions and What’s new are submitted together. Existing-review protection still applies. Approval is separate. |
| Microsoft Store | Windows 0.2.0.0 published; public catalog verified. Real API authentication verified. Store update uploads remain separately gated pending update-path validation. Microsoft signs the Store MSIX and its official online installer, not our own standalone EXE. |

## One-time maintainer setup

1. Keep the persistent Android signing key and its encrypted backup in ignored `local/signing/`.
2. With repository administration access through GitHub CLI, run `scripts/Configure-CISigning.ps1`. It saves the existing keystore/password as encrypted `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD` repository secrets via standard input.
3. Configure the dedicated Google Play publisher once using [Google Play CI setup](google-play-ci.md). This repository has verified publisher access and automatic Play uploads enabled; forks need their own authorization. The first real API submission of 0.1.4 succeeded; Google review and approval remain separate.
4. Restrict workflow modifications, release tags and secret access to trusted maintainers. Never expose publishing secrets to untrusted PRs. Firebase credentials, device tokens, clipboard data and pairing keys are not build inputs.

## Each release

Keep the visible versions in `Directory.Build.props` and `android/app/build.gradle` identical. Increase Android's internal `versionCode` for every upload, even if the visible version decreases. Keep the original signing identity for updates. Update README feature/routing descriptions and add all version-specific marketing files before tagging:

- `docs/release-notes/v0.x.y.md`: GitHub feature notes, requirements and tested limits.
- `docs/release-notes/v0.x.y.play.txt`: English What’s new, 1–500 characters.
- `docs/release-notes/v0.x.y.play-listing.json`: from 0.2.0, the matching version/package, English product title, short description and full description. Title/short/full limits are 30/80/4,000 characters. Additional locales may be included explicitly.

The Play workflow reads notes and product descriptions from that release's Git tag, rather than silently using text for a newer version from `main`. Older tags before 0.2.0 retain the existing notes-only compatibility path. `Test-GooglePlay.ps1` checks that current source versions have matching, valid text. Do not label review/source builds as downloadable releases or store approval.

After committing and checking CI, push the matching new tag. For example, **after updating source versions** for the next release:

```powershell
git tag v0.2.1
git push origin v0.2.1
```

Before publishing, explicitly configure `WINDOWS_SIGNING_MODE=microsoft-store` for the Microsoft-signed Store online installer, or `signpath` for trusted signing with the approved provider configuration. An unset mode fails; a signed build cannot fall back to unsigned output. The release job validates versions and privacy, runs protocol/localization/download checks and Android lint, builds signed Android APK/AAB files, packages Windows installers, checks their actual signature state, and checks a fresh standalone Windows install/service/download/uninstall on a disposable runner. In Store mode it then downloads and verifies the official Microsoft-signed online installer; the unsigned standalone EXE/ZIP are excluded from release assets and public checksums. Fresh standalone-runner checks do not claim Store installation was tested. The GitHub prerelease includes selected installers, checksums, library sources and a signing notice. Failures prevent publication. A manual unsigned build review leaves both `sign` and `publish` disabled. Temporary signing files are cleaned up. Store app updates use the separate [Microsoft Store workflow](microsoft-store.md).

When `GOOGLE_PLAY_UPLOAD_ENABLED=true`, a following job submits the exact release AAB, product descriptions and What’s new to Google Play through the official API, in one edit. A description-update failure prevents committing the release; existing promotional video, screenshots and unmentioned locales are retained. Existing reviews are preserved by default. If an earlier review is pending, the summary reports `waiting_previous_review`; it does not claim submission or retry on a schedule. Approval still belongs to Google. The publisher needs Play store-presence edit permission as well as release permission; a 403 is a failure, not permission to omit descriptions.

Do not overwrite existing tags or assets. To upload an existing release such as 0.1.4 to Play, use **Publish to Google Play** with that tag and mode `submit`; it downloads the existing AAB instead of rebuilding. See [workflow modes, authorization and review handling](google-play-ci.md).

## Local reproduction

```powershell
./scripts/Build.ps1 -JavaHome 'C:\Tools\jdk-21' -AndroidSdk 'C:\Tools\android-sdk'
./scripts/Package.ps1 -AndroidSdk 'C:\Tools\android-sdk'
./scripts/Test-GooglePlay.ps1
```

Public outputs are explicitly selected under `dist/release/`. They exclude `local/`, credentials, runtime configuration, pairing data and debug PDBs. `ClipboardBridge-Android-Library-Sources.zip` contains original UnifiedPush library sources and licenses; full app source is available at the release tag.

The new Windows download is Microsoft's Authenticode-signed Store online installer. It needs Internet access and Microsoft Store components. The older standalone setup is unsigned and requests administrator privileges. Both editions include one shared .NET runtime; they use separate encrypted state and must be re-paired when switching. Do not run both brokers on the same port.

CI and HTTP-fixture tests do not replace real-device permission, pairing, clipboard or long-term delivery checks. AAB validation and Google accepting an upload do not establish policy approval; see the [Google Play review](google-play.md).
