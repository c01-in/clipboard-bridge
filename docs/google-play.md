# Google Play readiness review

Reviewed: 16 October 2026. Baseline: public 0.1.2, package `com.colin.clipbridge`, target SDK 36; the 0.1.3 changes add companion download/privacy links and AAB output.

**Finding: there is no established blanket ban on UnifiedPush, but this build is not yet confirmed ready for Google Play production.** Play Console accepted the signed 0.1.3 bundle (version code 7) and it has been published to internal testing. No blocking validation error or push-library incompatibility warning was shown. The release is explicitly marked **not reviewed**; this is not Google's confirmation of the native Web Push registration path or production approval.

## Internal test outcome — 16 October 2026

- The signed GitHub release AAB was uploaded successfully and published to the internal test track.
- Play App Signing manages a copy of the existing app signing key, imported using Google's encrypted PEPK export. The original remains private for signing GitHub APKs; the app signing certificate matches across both channels.
- Prepublication validation showed two warnings: no testers had yet been assigned, and no deobfuscation mapping file was attached. Tester access is configured separately in Play Console. The build does not enable R8/ProGuard minification, so it has no mapping file to upload.
- Internal testing can proceed before a full store review. Store listing, policy declarations and production eligibility checks remain separate work. Acceptance here does not establish official support for the registration broadcast or guarantee a later review outcome.
- Tester email addresses, developer account identifiers, signing material and private Console screenshots are not part of this repository.

## The actual libraries and registration path

| Component | Checked use |
| --- | --- |
| `org.unifiedpush.android:connector:3.0.10` | Receives and decrypts Web Push; Apache-2.0 text is included in the APK. |
| `org.unifiedpush.android:embedded-fcm-distributor:3.0.0` | Registers with existing Play Services; LGPL-2.1 text and original library sources accompany releases. |
| AndroidX WorkManager 2.10.1 | Short receiving/retry work, including an expedited-work compatibility path. |

The exact published 3.0.0 source archive was inspected. `Constants.kt` sets `ACTION_FCM_TOKEN_REQUEST = "com.google.iid.TOKEN_REQUEST"`; `Utils.registerFCM` sends that broadcast to `com.google.android.gms`, with `scope=GCM`, a `wp:` subtype and the VAPID public key as sender/subscription. The endpoint is `https://fcm.googleapis.com/fcm/send/...`.

[UnifiedPush documents this Web Push/VAPID integration](https://unifiedpush.org/kdoc/embedded_fcm_distributor/), and its [repository credits community registration documentation](https://github.com/UnifiedPush/android-embedded_fcm_distributor). That establishes how it works; it does not establish Google's approval. The app does not ship Google Play Services binaries or a shared Firebase credential.

[Google's SDK policy](https://support.google.com/googleplay/android-developer/answer/13326895?hl=en-EN) puts responsibility for integrated third-party code on the app publisher. [Device and Network Abuse](https://support.google.com/googleplay/android-developer/answer/16559646?hl=en) also covers API/service access contrary to applicable terms. [Google API terms, section 2(c)](https://developers.google.com/terms) require access through documented means. We have not located Google documentation promising support for this native registration broadcast with a per-computer VAPID identity.

**This is an unresolved support/terms and review risk, not evidence that Google has specifically banned this library.** Ask Play developer support/the relevant Google API team about this exact mechanism before treating it as approved for production. A Play scan passing would still not establish long-term API support. This review does not silently replace the transport or reintroduce end-user Firebase setup.

## What the current code already does

- Targets API 36, matching the [current mobile target requirement](https://support.google.com/googleplay/android-developer/answer/11926878?hl=en).
- Reads Android clipboard content in the focused Activity after a user taps Send; it does not use accessibility, shell permissions or background clipboard-reading workarounds.
- Encrypts clipboard payloads with keys held by the paired devices. It has no maintainer relay, advertising SDK or analytics SDK.
- Receives Google broadcasts behind `com.google.android.c2dm.permission.SEND`; incoming app payloads are cryptographically checked before application.
- Does not download Android self-updates or executable code. The companion link opens the public releases page; installing Windows on a separate computer is not an Android APK self-update.
- Includes license texts and library sources. Source and license availability must continue for Play distributions too.

## Required preparation and remaining checks

1. **Resolve the registration-path question.** Do not label the current transport Google-approved based only on the absence of a known ban.
2. **AAB and Play App Signing are configured.** From 0.1.3, build/release automation creates a signed `ClipboardBridge.aab` in addition to the sideload APK. This is an upload artifact, not a phone installer. [Google's setup guide](https://support.google.com/googleplay/android-developer/answer/9859152?hl=en) explains bundles and signing. The existing app signing identity was preserved for updates across channels. [Play App Signing documentation](https://support.google.com/googleplay/android-developer/answer/9842756?hl=en) describes importing an existing key for Google to manage while retaining local signing for other stores.
3. **Privacy and Data safety.** 0.1.3 adds an in-app link to the public [privacy policy](../PRIVACY.md). Before submitting, check its publisher/contact details against the actual store listing and complete the Data safety form for Google registration/delivery metadata as well as app behavior. Do not simply select “no data collected” because clipboard content is encrypted. Google's [Data safety guidance](https://support.google.com/googleplay/android-developer/answer/10787469?hl=en) excludes qualifying end-to-end-encrypted content, but not automatically all SDK metadata. The [User Data policy](https://support.google.com/googleplay/android-developer/answer/10144311?hl=en) still requires a public policy and an in-app entry.
4. **Review the merged manifest.** WorkManager adds `FOREGROUND_SERVICE`, `WAKE_LOCK`, `RECEIVE_BOOT_COMPLETED` and its SystemForegroundService; the connector adds an exported RaiseToForegroundService. The app has no permanent foreground receive service. Inspect actual API-specific use and Play Console declarations rather than assuming these permissions are absent or removing compatibility code without tests.
5. **Complete the store submission.** Supply screenshots, listing, content rating, audience, ads declaration, reviewer steps and a Windows companion link. A reviewer needs a Windows computer, the same Wi-Fi and Google connectivity to test pairing/transfer; no account credentials are needed.
6. **Check the developer account's testing requirement.** For personal accounts created after 13 November 2023, Google currently requires at least 12 opted-in testers for 14 continuous days before applying for production access. [Official requirement](https://support.google.com/googleplay/android-developer/answer/14151465?hl=en). The account's creation date and resulting production eligibility have not been established.

A Play app has been created and the 0.1.3 bundle published to internal testing. It has not passed a full production review. AAB generation, GitHub publication, CI success and internal test availability are separate from production approval.

## Companion download link

Android provides `https://github.com/c01-in/clipboard-bridge/releases` in its unpaired flow and overflow menu. Users can copy/share it and select the Windows installer on their computer. It is deliberately not an EXE direct link and does not use `/releases/latest`, which excludes this project's preview releases.
