# Microsoft Store

Status: product reserved and publisher directory associated. Dedicated automation
credentials passed real OAuth and read-only Store API access to this product,
including after the temporary setup administrator role was revoked. Version
0.2.0.0 and 0.2.1.0 were submitted on 21 October 2026 (America/Los_Angeles).
Partner Center and the Store API confirmed both submissions as **Published**.
The public listing and WinGet's Microsoft Store catalog were verified on
21 October 2026 (America/Los_Angeles).

**[Get Clipboard Bridge from Microsoft Store](https://apps.microsoft.com/detail/9PJT9PTSMCJQ).**
The website's download is a small Microsoft-signed online EXE that installs the
current Store package. New GitHub releases mirror that unmodified installer
after verifying its trusted Microsoft signature and timestamp. It requires
Internet access and Microsoft Store components; it is not an offline EXE.
The earlier standalone EXE is not signed by this process.

GitHub also provides the **complete Microsoft-signed MSIX**, including the shared
.NET runtime. It is built on GitHub Actions, signed through the Store, and retrieved
unchanged from Microsoft by the offline-package workflow. Publication requires a
matching checksum from Microsoft's HTTPS Store catalog. Downloaded Store packages
are not installed or executed for testing.
The online EXE remains available alongside it. See [the signing workflow](windows-signing.md).

## Package

The Store edition is a full MSIX, with package identity recorded in
[`windows/Store/identity.json`](../windows/Store/identity.json). The separate
external-location Share preview is not submitted to the Store.
The current Store package targets Windows 11 (build 22000) or later, on x64.

The tray starts the existing broker as the signed-in user. Windows manages login
startup and the native Share target. There is no elevated Windows service in the
Store package. Clipboard transfer, encryption and file transfer use the same
implementations as the desktop edition. Right-click **Share → Clipboard Bridge**
accepts multiple files; the standalone desktop installer also offers Send to.

The three executables share one copy of .NET Core, Windows Desktop and ASP.NET
Core. Identical application DLLs are merged. Windows Share uses the OS WinRT
implementation; the app does not add Electron, WebView2 or the Windows App SDK.
Windows App Runtime is not a replacement for the .NET runtime used by WinForms
and Kestrel. Removing .NET without replacing these components would break clean
installations. The current MSIX therefore still contains one shared .NET runtime.

Build and validate:

```powershell
./scripts/Build.ps1 -UseEnvironmentSigning
./scripts/Package-WindowsStore.ps1 -PackageRoot dist -OutputDirectory dist/store
./scripts/Test-WindowsStore.ps1 -PackageDirectory dist/store -ValidationRoot <test-state-directory>
```

Use `-LocalReview` only for an isolated, unsigned test package with a separate
identity. It is not a Store submission. Local validation confirmed installation
without developer mode or certificate-store changes, tray-driven broker startup,
owner IPC, and broker recovery after termination. The review package was removed
afterward. Store certification has passed; Store-delivered installation and update behavior have not yet both been verified on the user's everyday installation.

The desktop edition and Store edition have separate encrypted state. An explicit
channel migration can preserve pairing by backing up desktop state, stopping the
desktop broker, and re-encrypting the unchanged configuration from machine DPAPI
to current-user DPAPI in the Store package context. Copy the receiving-folder,
pause and share-staging preferences too. Confirm that the running Store broker
reports the original pairing before retiring the desktop service and its Share
preview registration. Copying files from an unpackaged process alone is not
proof: MSIX can redirect the Store app to a different private copy. Do not
silently stop or remove a working desktop service to make a Store test pass.

The next Store package localizes its shell name separately from the WinForms UI.
Package resources retain “Clipboard Bridge” alongside the localized app name, including
“剪贴板同步” in Simplified Chinese. Changing these signed resources requires a new
Store package; adding a second shortcut with the same application ID does not
make the already-published 0.2.3 English-only shell name searchable in Chinese.

### Generic icon in Explorer's quick Share submenu

Version 0.2.3 includes the image variants and indexed resources for the Share
target. An Explorer process that was already running before these resources were
updated can retain a generic document icon, even after the app is reinstalled.
On 23 October 2026, the correct app icon was verified in the actual quick Share
submenu for both installed distribution channels after refreshing Explorer.
The executable's icon and the full Share panel alone were not used as proof.

If this occurs after updating, finish any file operations and restart Windows
Explorer from Task Manager, or sign out and back in. App pairing is unaffected.
The installer does not forcibly restart Explorer or close the user's windows.

## API and Actions

The developer account must be associated with an owned Microsoft Entra workforce
tenant. Do not use an employer's guest tenant. Associate a dedicated Entra
application with the Store account and grant the documented Manager role.
Store API authentication needs these Actions secrets:

| Secret | Purpose |
|---|---|
| `MS_STORE_TENANT_ID` | Project-owned directory |
| `MS_STORE_CLIENT_ID` | Dedicated automation application |
| `MS_STORE_CLIENT_SECRET` | Application credential; record its expiry |
| `MS_STORE_SELLER_ID` | Partner Center seller identifier |

Keep an encrypted local recovery copy under the ignored `local/` directory, never
in Git, build artifacts or disposable caches. Do not store browser cookies or
temporary web-session tokens as an API credential.

Public distribution uses the Store product ID and the publisher display name
`c01-in`. Do not publish the maintainer's personal email, email-derived directory
domain, account login, or administration screenshots in source, release assets,
workflow logs, Store support fields, or download URLs. Use the approved support
alias or the public project support page for customer contact. The CI workflow
uses the tenant GUID from Actions secrets, not a personal directory domain.

Run `scripts/Test-MicrosoftStoreAccess.ps1` to verify real OAuth and read-only
access to this exact product. This must succeed before calling setup complete.
Microsoft requires the initial app submission, including age ratings, to be
created in Partner Center. The official CLI update flow requires a live app.

Use **[Actions → Publish](https://github.com/c01-in/clipboard-bridge/actions/workflows/release.yml)** for a new version. It builds both apps once, publishes GitHub downloads and submits the already-built packages and descriptions to both stores. Store API secrets and `MICROSOFT_STORE_UPLOAD_ENABLED=true` are configured in this repository. No separate Store build is required. Upload success and Store certification are separate.
From 0.2.2, each tag also carries
`docs/release-notes/v0.x.y.store-listing.json` with the English product description
and release notes. CI uploads the package with `--noCommit`, reads the resulting
draft, updates only its existing English description/release notes, then commits.
Other listing languages, images, pricing and policy fields are retained.
Metadata failure leaves the draft uncommitted instead of submitting stale text.

After a successful Publish run, the full-package workflow
starts automatically. It waits up to two
hours for the exact certified version in the public Store catalog, then checks
Microsoft's checksums and attaches the complete MSIX alongside the online EXE.
An older package is never substituted. If certification takes longer, rerun
**Fetch Microsoft Store full package** with the existing tag and publication
enabled after the version is public; no application rebuild is needed.

References: [Microsoft Store automation](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions),
[submission API prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services),
[packaging WinForms](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app),
[Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture).
