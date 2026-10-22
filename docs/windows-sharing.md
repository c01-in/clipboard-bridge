# Windows Share

On Windows 11, select one or more files in File Explorer, choose **Share**, then **Clipboard Bridge**. Images, videos and other files use the file-transfer protocol and are saved on the paired phone; sharing files does not replace its clipboard. The standalone desktop installer also offers classic **Send to → Clipboard Bridge**; the Store edition uses the native Share panel without that legacy shortcut. Folders are not supported.

The sending window shows transfer progress. It closes three seconds after the phone confirms that every file was saved. Failure and cancellation remain visible until dismissed.

The desktop installer registers a small external-location MSIX identity with the Windows Share contract. The main binaries and the single private .NET runtime remain in the normal installation directory. The Store edition instead declares the same Share contract in its full MSIX and runs its broker as the signed-in user; it needs no elevated Windows service. The target accepts `StorageItems` and all file extensions because this is a general file mover. Folders and empty selections are rejected; no item is silently dropped. Ordinary local files are opened through the authenticated broker as the logged-in user. Provider-backed files without a readable path are staged with bounded memory and removed when the share window exits. Their staging root can be set in the private `share-staging.txt` preference; it defaults to the process temporary directory.

## Local preview and signed distribution

The current local Windows 11 preview uses Microsoft's unsigned identity-package mechanism (`AllowUnsigned` and the dedicated unsigned-publisher OID). Registration does not enable Developer Mode or add a certificate to a trust store. This is a local validation profile, not a production signing claim.

Windows 0.2.0.0 is now in [Microsoft Store certification](microsoft-store.md), not live. Microsoft signs the Store MSIX after approval. This does not sign earlier GitHub installers. The separate SignPath integration for standalone EXE distribution remains proposed, without approved production access. That pipeline signs the identity and first-party binaries before packaging and signing the installer; `Package.ps1 -ForPublication` rejects missing trusted signatures. No Windows PFX private key is imported by GitHub Actions. Local certificate signing remains available through `CLIPBRIDGE_WINDOWS_SHARE_CERT_THUMBPRINT`. See [code signing policy](../CODE_SIGNING_POLICY.md) and [signing setup](windows-signing.md). Android signing is separate and unchanged.

References: [Microsoft Share Target guidance](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-receive), [unsigned packages for local testing](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package).
