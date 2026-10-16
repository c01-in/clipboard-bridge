# Android sharing — 0.1.4 candidate

The owner approved publication on 2026-10-16. This document records the implementation and verification for 0.1.4; GitHub build/publication and Google Play review status are tracked separately.

## Share through Clipboard Bridge

In another Android app, select text or one image, choose **Share**, then **Clipboard Bridge** (the localized app name, such as **剪贴板同步** in Simplified Chinese). The share entry uses the app's name rather than a destination platform. The current implementation sends automatically to the paired Windows computer and closes after it returns an authenticated clipboard-write acknowledgment. It does not open the main screen or require another Send tap. macOS support is not implemented in this candidate.

The share target reads the supplied text or temporary `content://` image URI. It does not read or overwrite the phone's clipboard. Images use the existing PNG conversion and 16 MiB limit. Other file types and multiple items are rejected. For an image accompanied by a caption or link, the image is sent; this version does not combine image and text into one clipboard item.

Pairing, Android pause and receive-session checks still apply. An unpaired, paused or reconnecting device gets an error and a way to open settings. A failure offers an explicit retry; activity recreation does not automatically submit the same content again.

The existing **Send clipboard to Windows** button remains available. No permanent Android service, background clipboard listener or new runtime permission is added. New share labels and errors are translated into all nine supported locales.

## Clipboard feedback

On Android 13 and later, a successful receive updates the in-app status and clears the app's progress notification instead of posting another “copied” notification. Copying the companion download link also relies on system feedback on these versions. Earlier Android versions retain the app's success feedback. Error notifications remain available.

The owner also reported two toasts while copying inside **WeChat**, before opening Clipboard Bridge: one with the WeChat icon, followed by an iconless **“已复制。”** message. **WeChat Keyboard** is active, so feedback from the input method is another hypothesis. The exact second message is absent from this application's strings, and Clipboard Bridge has no background clipboard listener or receive-success Toast. An emulator test verifies that ordinary copying in a separate-UID app does not trigger Clipboard Bridge sending or notifications. This does **not** establish the second toast's source on the owner's Samsung phone, reproduce WeChat Keyboard or other clipboard integrations, or prove that the reported device-specific symptom is fixed. The phone was unavailable for further diagnosis; no system or keyboard settings were changed.

## Verification

Android debug and release builds, the AAB and lint are checked separately from device behavior. `ShareTests` runs on an isolated debug package (`com.colin.clipbridge.debug`), preserving the release app's pairing and credentials.

```powershell
.\android\gradlew.bat -p .\android assembleDebug assembleDebugAndroidTest lintDebug --no-daemon --max-workers=1
adb -s <emulator-serial> install -r .\android\app\build\outputs\apk\debug\app-debug.apk
adb -s <emulator-serial> install -r .\android\app\build\outputs\apk\androidTest\debug\app-debug-androidTest.apk
adb -s <emulator-serial> shell am instrument -w -r com.colin.clipbridge.debug.test/androidx.test.runner.AndroidJUnitRunner
```

On 2026-10-16, all **12 instrumentation tests passed** on Android 15 / API 35 AOSP ATD. They cover text and image extraction, malformed and multiple inputs, share-target resolution, actual encrypted HTTP payloads and hash checks, PNG bytes, incorrect acknowledgments, pause and unpaired errors, activity recreation, separate-UID image URI grants, and ordinary copying in another app. The notification regression test explicitly enables notification permission, observes the progress notification, completes the receive feedback and waits for the notification's removal. All **85 Windows protocol, pairing, localization and download checks** also passed. Release APK/AAB builds and Android lint passed; the review APK retains the existing release signing certificate.

The protocol peer in these tests decrypts the real upload and returns a test acknowledgment. It is not the Windows desktop clipboard, a Google push server or a substitute for an end-to-end transfer test. No new Samsung device or long-sleep delivery claim is made.

The emulator used one virtual CPU, host CPU affinity limited to one core and BelowNormal priority. An explicit QEMU 1024 MiB limit was verified in guest `/proc/meminfo`; host working set was about 1.3 GiB. It was shut down after testing. Local logs and review artifacts are in ignored `local/share-verification/`.
