# Windows signing and downloads

Windows downloads use **Microsoft Store signing** for [Clipboard Bridge](https://apps.microsoft.com/detail/9PJT9PTSMCJQ). Users can choose a complete MSIX or Microsoft's small online EXE. Both install the Store edition, with one shared .NET runtime. They require Windows 11 or later, x64. Developer Mode and certificate import are not required.

The online EXE is downloaded unchanged from Microsoft and checked for a valid, timestamped Microsoft Corporation signature. It installs the currently approved Store version, which can lag behind the GitHub Android APK while certification is pending.

The full MSIX is built in GitHub Actions, submitted through the Store API and signed by Microsoft after certification. The automatic full-package workflow retrieves the exact release version from Microsoft's catalog and verifies its checksums before attaching it to GitHub. It never substitutes an older version. Downloaded Store packages are not installed or executed for testing.

Use **[Actions → Publish](https://github.com/c01-in/clipboard-bridge/actions/workflows/release.yml)** for a new release. It builds once and submits to both stores. Full-package retrieval runs separately so Store certification does not hold the main workflow open. If certification exceeds its waiting window, rerun **Fetch Microsoft Store full package** for the same tag with publication enabled; no rebuild is needed. See [release instructions](releases.md).

The download helper uses XML protocol templates from [StoreLib](https://github.com/StoreDev/StoreLib/tree/3f2a98ffede0bf3f78321c194e884fd0aaf14c29) (MPL-2.0), fetched at a fixed commit in CI. It uses no shared device token, user login or publisher credentials. The app itself does not include StoreLib. Microsoft's legacy CDN can return HTTP file URLs; packages are accepted only after matching metadata checksums, including SHA-256 from the HTTPS catalog. TLS verification remains enabled.

Earlier standalone EXEs are unsigned; Microsoft Store does not sign those installers. SignPath integration was explored but is not part of the active release workflow. It has no production credentials and does not gate publication.
