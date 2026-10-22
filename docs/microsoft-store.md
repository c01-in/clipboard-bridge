# Microsoft Store

Status: product reserved and publisher directory associated. Dedicated automation
credentials passed real OAuth and read-only Store API access to this product,
including after the temporary setup administrator role was revoked. Version
0.2.0.0 was submitted on 21 October 2026 (America/Los_Angeles). Partner Center
and the Store API now confirm that the first submission is **Published**.
The public listing and WinGet's Microsoft Store catalog were verified on
21 October 2026 (America/Los_Angeles).

**[Get Clipboard Bridge from Microsoft Store](https://apps.microsoft.com/detail/9PJT9PTSMCJQ).**
The website's download is a small Microsoft-signed online EXE that installs the
current Store package. New GitHub releases mirror that unmodified installer
after verifying its trusted Microsoft signature and timestamp. It requires
Internet access and Microsoft Store components; it is not an offline EXE.
The earlier standalone EXE is not signed by this process.

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

The desktop edition and Store edition currently have separate encrypted state.
Keep a backup and re-pair when changing distribution channels. Do not silently
stop or remove a working desktop service to make a Store test pass.

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

The **Microsoft Store build and deployment** workflow defaults to build-only.
It produces the full MSIX and tests the per-user broker without publishing.
The package includes the signed Android companion; validation downloads it from
the packaged broker and checks its length and SHA-256. Select `verify_api` for a
read-only API check before the first publication, with `publish` left disabled.
After authentication and the first Store publication are verified, set
`MICROSOFT_STORE_UPLOAD_ENABLED=true`. Matching `v0.x.y` tags then trigger Store
update submission; manual publication also requires a matching existing tag.
Upload success does not mean certification passed or that the app is live.

References: [Microsoft Store automation](https://learn.microsoft.com/en-us/windows/apps/publish/msstore-dev-cli/github-actions),
[submission API prerequisites](https://learn.microsoft.com/en-us/windows/uwp/monetize/create-and-manage-submissions-using-windows-store-services),
[packaging WinForms](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app),
[Windows App SDK deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture).
