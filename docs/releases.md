# Automated builds and releases

Users install the Android APK or Windows setup EXE from [GitHub Releases](https://github.com/c01-in/clipboard-bridge/releases), without a build environment. Versions remain 0.x previews. The current GitHub release is **0.1.4**; GitHub publication is separate from Google Play submission and approval.

Current source/review version: **0.2.0**. Its Windows 11 x64 full MSIX was submitted to Microsoft Store certification on 21 October 2026 (America/Los_Angeles), with automatic publication after approval. It is not live yet, and there is no 0.2.0 GitHub release. See [0.2.0 feature notes](release-notes/v0.2.0.md) and [Store setup/status](microsoft-store.md).

| Channel | Current state and requirements |
| --- | --- |
| GitHub APK / standalone Windows EXE | Public 0.1.4 assets remain unchanged. New trusted EXE publication requires approved SignPath credentials; these are not configured. A tag alone cannot make that path succeed. |
| Google Play | Existing API publisher configured. From 0.2.0, the release AAB, product descriptions and What’s new are submitted together. Existing-review protection still applies. Approval is separate. |
| Microsoft Store | First Windows submission in certification. Real API authentication verified; automatic update uploads remain gated until the first publication completes. Microsoft signs the Store MSIX, not GitHub EXEs. |

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

The standalone release job requires approved SignPath configuration when signing/publishing. Once configured, it validates versions and privacy, runs protocol/localization/download checks and Android lint, builds signed APK/AAB files, signs/packages Windows installers, and checks a fresh Windows install/service/download/uninstall on a disposable runner. It then publishes the GitHub prerelease with checksums and library sources. Failures prevent publication. A manual unsigned build review leaves both `sign` and `publish` disabled; it is not a new public release. Temporary signing files are cleaned up. Store builds use the separate [Microsoft Store workflow](microsoft-store.md).

When `GOOGLE_PLAY_UPLOAD_ENABLED=true`, a following job submits the exact release AAB, product descriptions and What’s new to Google Play through the official API, in one edit. A description-update failure prevents committing the release; existing promotional video, screenshots and unmentioned locales are retained. Existing reviews are preserved by default. If an earlier review is pending, the summary reports `waiting_previous_review`; it does not claim submission or retry on a schedule. Approval still belongs to Google. The publisher needs Play store-presence edit permission as well as release permission; a 403 is a failure, not permission to omit descriptions.

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
