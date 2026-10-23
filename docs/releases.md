# One-click releases

Use **[Actions → Publish → Run workflow](https://github.com/c01-in/clipboard-bridge/actions/workflows/release.yml)** on `main`. This is the single release entry point for GitHub, Google Play and Microsoft Store. Pushing the matching `v0.x.y` tag also starts it.

## Prepare the version

Commit the same visible version in `Directory.Build.props` and `android/app/build.gradle`, increase Android's `versionCode`, and include these four files:

- `docs/release-notes/v0.x.y.md`: GitHub release notes.
- `docs/release-notes/v0.x.y.play.txt`: Google Play What's new, 1–500 characters.
- `docs/release-notes/v0.x.y.play-listing.json`: versioned Play product descriptions.
- `docs/release-notes/v0.x.y.store-listing.json`: versioned Store description and release notes.

## What the button does

1. Check the version, tracked-file privacy and descriptions; run the existing standard tests once in the cloud.
2. Build both apps on GitHub Actions. Reuse the persistent Android signing key and package one shared Windows runtime.
3. Publish the signed Android APK/AAB, library sources, Microsoft-signed online installer and checksums to GitHub Releases.
4. Submit the same AAB and descriptions to Google Play, and the already-built MSIX and descriptions to Microsoft Store. Neither publisher rebuilds the apps.

Failures stop the affected job and appear in GitHub's workflow notification email. Existing versions and tags are never overwritten. A store accepting a submission is separate from approving it. An existing Google review is preserved; the workflow summary explains when a submission must wait.

Microsoft signs the full MSIX after certification. A separate automatic **Fetch Microsoft Store full package** workflow waits for that exact version, verifies Microsoft's catalog checksums, and adds it to the same GitHub release. The main Publish workflow does not wait for this certification. Downloaded Store packages are never installed or executed for testing.

## Development and recovery

Local builds are welcome for development and device checks. Official release builds run on GitHub Actions. There is no required local CI or pre-commit test hook. Pull requests and a manual **Build and privacy checks** run provide separate cloud checks; a normal release needs only **Publish**. An emulator is not started by the release workflow.

The manual Google Play publisher and Store full-package workflow are recovery tools for an existing release: they reuse its artifacts without rebuilding. See [Google Play API setup and review handling](google-play-ci.md) and [Microsoft Store setup](microsoft-store.md).

Publishing credentials are configured once as GitHub Secrets. Both `GOOGLE_PLAY_UPLOAD_ENABLED` and `MICROSOFT_STORE_UPLOAD_ENABLED` must be `true`. Customer pairing data, clipboard content and personal credentials are never public artifacts. Forks need their own store accounts and signing identity.
