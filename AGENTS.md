# Android device deployment

- Install and update APKs only in the owner's primary Android user (`0`). Always use an explicit serial and `adb -s <serial> install --user 0 -r <apk>`; never omit `--user` or use `--user all`.
- Check `pm list users` before installation. Do not target a clone profile, Secure Folder, work profile, or any other secondary user, including for isolated test packages.
- Use `--user 0` explicitly for activity launches, instrumentation and permission grants as well.
- Prefer `scripts/Install-Android.ps1`, which fixes the install target to user 0. Do not add a fallback to another profile.
- Only operate on another profile when the human user explicitly requests that specific operation. Removing an accidental secondary-profile copy must not uninstall or clear the primary app or its pairing.
