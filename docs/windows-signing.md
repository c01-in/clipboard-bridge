# Windows signing with SignPath

Status: application/integration preparation; no production signing credentials or completed signing request yet. The hosted service and application-review process are separate: service availability does not guarantee acceptance of a new open-source project.

The **Microsoft Store full MSIX**, version 0.2.0.0, is published. The new Windows download uses Microsoft's signed online installer for [our Store product](https://apps.microsoft.com/detail/9PJT9PTSMCJQ). It fetches the current Store version; it is not an offline or version-pinned standalone installer. Earlier GitHub EXEs remain unsigned. Store signing does not activate the proposed SignPath route described below. See [Store status and API workflow](microsoft-store.md).

## Onboarding

Apply through [SignPath Foundation](https://signpath.org/apply). If its embedded form fails to render, contact `info@signpath.io` with the repository, MIT license, existing releases, project purpose and [code signing policy](../CODE_SIGNING_POLICY.md), and ask whether new Foundation applications are being accepted. Do not claim a download history, reputation, MFA status or approval that has not been verified.

Ask SignPath to configure the repository `c01-in/clipboard-bridge`, GitHub-hosted build origin and a manually approved production signing policy. The two proposed artifact configurations are [.signpath/applications.xml](../.signpath/applications.xml) and [.signpath/installer.xml](../.signpath/installer.xml). Provider-side configuration and acceptance are required; committing XML alone does not configure the service.

## Repository configuration after approval

| Kind | Name | Value |
|---|---|---|
| Actions secret | `SIGNPATH_API_TOKEN` | Submitter token restricted to this project/policy |
| Actions variable | `SIGNPATH_ORGANIZATION_ID` | Assigned organization UUID |
| Actions variable | `SIGNPATH_PROJECT_SLUG` | Assigned project slug |
| Actions variable | `SIGNPATH_SIGNING_POLICY_SLUG` | Approved manual signing policy |
| Actions variable | `SIGNPATH_APPLICATIONS_CONFIGURATION` | Application-binary/MSIX configuration slug |
| Actions variable | `SIGNPATH_INSTALLER_CONFIGURATION` | Installer configuration slug |
| Actions variable | `SIGNPATH_PUBLISHER` | Exact X.509 certificate subject supplied by SignPath |

The MSIX publisher must match that certificate subject exactly; do not guess it from the provider's display name. Private keys stay with SignPath. No PFX or Windows signing private key needs to be stored in GitHub. Keep any necessary local account/credential records outside the repository's tracked files and outside disposable caches.

## Build without publishing

Run **Build, sign and optionally publish installers** in GitHub Actions on `main`, leave `tag` empty, and leave `sign` and `publish` unchecked. This builds both applications, verifies Android signatures, tests a fresh Windows install/uninstall on the runner, and uploads review artifacts. Missing SignPath credentials do not fail this mode. Existing Android signing secrets are still needed to build the normal Android companion included with the Windows installer.

After approval/configuration, repeat with `sign=true`, `publish=false` to exercise real signing without releasing. SignPath can require two approvals: first for the application binaries/MSIX, then for the final installer containing them. Each request may wait up to two hours; its link is available in the action log and final step summary. A signing timeout is a real failure, not permission to publish unsigned files.

Public releases use `WINDOWS_SIGNING_MODE=microsoft-store`: CI downloads the unmodified installer from `get.microsoft.com` for product `9PJT9PTSMCJQ`, verifies a valid, timestamped Microsoft Corporation signature, and publishes it as `ClipboardBridge-Windows-Store-Setup.exe`. The unsigned standalone EXE/ZIP are excluded from release assets. Android APK/AAB signing and build, privacy and installation checks still run. No Developer Mode, certificate import or disabling security software is required. Set `WINDOWS_SIGNING_MODE=signpath` only after the approved provider configuration is ready; failures never downgrade to unsigned publishing.

Publishing requires an existing matching `v0.x.y` tag and `publish=true`, or the existing tag-push release trigger. The Google Play job is explicitly disabled during build reviews even if Play upload is enabled for releases. A successful build review alone never publishes an app.

## Current limitations

The unsigned review path can be checked before approval. Trusted signing and returned-certificate validation can only be proven end to end after SignPath accepts the project and supplies production access. The current locally installed Share preview remains usable and is not replaced by this setup work.

Free alternative: Microsoft Store signs MSIX submissions after certification. This needs a Store-compatible package and separate review; it does not sign the existing standalone EXE for GitHub distribution. Certum's open-source code-signing offering requires payment. Sigstore has its own verification trust model and is not a substitute for a Windows-trusted Authenticode publisher.

References: [SignPath GitHub integration](https://docs.signpath.io/trusted-build-systems/github), [Foundation terms](https://signpath.org/terms), [service status](https://docs.signpath.io/status/), [Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), [Certum purchase process](https://support.certum.eu/en/how-to-buy-code-signing/), [Sigstore trust model](https://docs.sigstore.dev/).
