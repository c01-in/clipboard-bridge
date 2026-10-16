# Automated builds and releases

Users install the Android APK or Windows setup EXE from [GitHub Releases](https://github.com/c01-in/clipboard-bridge/releases), without a build environment. Versions remain 0.x previews. The current GitHub release is **0.1.4**; GitHub publication is separate from Google Play submission and approval.

## One-time maintainer setup

1. Keep the persistent Android signing key and its encrypted backup in ignored `local/signing/`.
2. With repository administration access through GitHub CLI, run `scripts/Configure-CISigning.ps1`. It saves the existing keystore/password as encrypted `ANDROID_KEYSTORE_BASE64` and `ANDROID_KEYSTORE_PASSWORD` repository secrets via standard input.
3. Configure the dedicated Google Play publisher once using [Google Play CI setup](google-play-ci.md). No Play publishing credential is currently configured, so automatic Play uploads remain disabled until access is verified.
4. Restrict workflow modifications, release tags and secret access to trusted maintainers. Never expose publishing secrets to untrusted PRs. Firebase credentials, device tokens, clipboard data and pairing keys are not build inputs.

## Each release

Keep the visible versions in `Directory.Build.props` and `android/app/build.gradle` identical. Increase Android's internal `versionCode` for every upload, even if the visible version decreases. Keep the original signing identity for updates. Add version-specific GitHub notes in `docs/release-notes/v0.x.y.md` and English Play notes (at most 500 characters) in `docs/release-notes/v0.x.y.play.txt`.

After committing and checking CI, push the matching new tag. For example, **after updating source versions** for the next release:

```powershell
git tag v0.1.5
git push origin v0.1.5
```

The release job validates versions and privacy, runs protocol/localization/download checks and Android lint, builds and verifies signed APK/AAB files, packages Windows installers, and checks a fresh Windows install/service/download/uninstall on a disposable runner. It then publishes the GitHub prerelease with SHA-256 checksums and library sources. Failures prevent installer publication. Temporary signing files are cleaned up.

When `GOOGLE_PLAY_UPLOAD_ENABLED=true`, a following job submits the exact release AAB to Google Play through the official API. Existing reviews are preserved by default. If an earlier review is pending, the summary explicitly reports `waiting_previous_review`; it does not claim submission or retry on a schedule. Approval still belongs to Google.

Do not overwrite existing tags or assets. To upload an existing release such as 0.1.4 to Play, use **Publish to Google Play** with that tag and mode `submit`; it downloads the existing AAB instead of rebuilding. See [workflow modes, authorization and review handling](google-play-ci.md).

## Local reproduction

```powershell
./scripts/Build.ps1 -JavaHome 'C:\Tools\jdk-21' -AndroidSdk 'C:\Tools\android-sdk'
./scripts/Package.ps1 -AndroidSdk 'C:\Tools\android-sdk'
./scripts/Test-GooglePlay.ps1
```

Public outputs are explicitly selected under `dist/release/`. They exclude `local/`, credentials, runtime configuration, pairing data and debug PDBs. `ClipboardBridge-Android-Library-Sources.zip` contains original UnifiedPush library sources and licenses; full app source is available at the release tag.

The Windows installer is not Authenticode-signed and requests administrator privileges. Installed apps include their .NET runtime. Updates preserve service data; fresh installs use `%ProgramData%\ClipboardBridge`.

CI and HTTP-fixture tests do not replace real-device permission, pairing, clipboard or long-term delivery checks. AAB validation and Google accepting an upload do not establish policy approval; see the [Google Play review](google-play.md).
