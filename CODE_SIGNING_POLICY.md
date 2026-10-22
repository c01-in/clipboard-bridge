# Code signing policy

## Current status

Windows 0.2.1.0 is published in [Microsoft Store](https://apps.microsoft.com/detail/9PJT9PTSMCJQ). GitHub offers the complete Store-signed MSIX and Microsoft's signed online EXE installer. Both are retrieved unchanged from Microsoft. The full package is built on GitHub Actions before Store submission; the offline-package workflow checks its checksum against Microsoft's HTTPS Store catalog before adding it to the existing release. Downloaded Store packages are not installed or executed for testing. The online installer retains its verified Microsoft publisher and timestamp. This does not sign our old standalone EXE or earlier GitHub downloads, which remain unsigned. See [Store package and automation](docs/microsoft-store.md).

The separate SignPath Foundation integration for standalone distribution is proposed. Approval and production signing credentials are not configured. Preparing that integration does not make earlier downloads signed, and a build review publishes no new version.

If approved, the signing attribution will be: **Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).** This is a proposed provider, not a claim that sponsorship has already been granted.

## People and approvals

- Maintainer, committer and reviewer: [c01-in](https://github.com/c01-in).
- Release signing approver: c01-in, subject to SignPath onboarding and its account requirements.
- Contributors submit changes through pull requests; the maintainer reviews them before inclusion in a signed release.
- Signing must follow the provider's MFA and manual release-approval requirements. Tokens may submit requests; they must not bypass required approvals.

## Signing scope and build origin

Only this project's own Windows application binaries, the Windows Share MSIX identity and the final Windows installer may be signed through this project. Third-party .NET runtimes and dependencies retain their original signatures and licenses. Android uses its existing, separate signing key.

All signing inputs must be built from the public repository on GitHub-hosted runners, uploaded as GitHub Actions artifacts, and submitted through the official, commit-pinned SignPath action. Locally built uploads are not accepted as release-signing inputs. Artifact configurations in [.signpath](.signpath) allow only explicit first-party filenames and product metadata.

Windows signing happens in two stages: sign the binaries and Share identity, package those verified files into the installer, then sign the installer. Both stages require the provider's approval policy. The pipeline checks the trusted signature, publisher and timestamp before accepting results; a missing or invalid signature stops a signed build. Final checksums are generated after signing.

Manual build reviews default to `sign=false` and `publish=false`. They neither request signatures nor publish GitHub releases or submit to Google Play. Publishing requires the explicit repository setting `WINDOWS_SIGNING_MODE`: `microsoft-store` publishes the verified Microsoft online installer; after the matching Store version is available, the offline-package workflow adds the complete Store-signed MSIX to the same release. `signpath` requests trusted signing of our standalone installer. Android remains signed with its persistent release key in both modes. Unsigned standalone EXE/ZIP review artifacts are excluded from Store-mode public downloads and checksums. An unset or unknown publishing mode fails. A requested signed build cannot silently fall back to unsigned publication when signing fails.

## Privacy and reporting

Clipboard and file contents travel between devices chosen and paired by the user. Google push infrastructure is used for wake-ups. See [PRIVACY.md](PRIVACY.md), including third-party services, and [SECURITY.md](SECURITY.md) for reporting vulnerabilities. Signing uploads contain public-source build products, never pairing data, Android signing keys, Firebase credentials or the user's transferred files.

## Signing setup

See [docs/windows-signing.md](docs/windows-signing.md) for onboarding, repository configuration and build-review instructions.
