# Clipboard Bridge Privacy Policy

Effective date: 16 October 2026

This policy covers the Clipboard Bridge Android app and Windows companion maintained at [c01-in/clipboard-bridge](https://github.com/c01-in/clipboard-bridge).

## What the app does with your clipboard

Clipboard Bridge transfers text and one image between devices you pair and approve. Windows can send copied content automatically while automatic sending is enabled. Android reads clipboard content only when you tap Send in the foreground app. Receiving content can replace the destination device's clipboard.

Clipboard content can include sensitive information. Pair only devices you trust, and pause automatic sending when you do not want copied content transferred. The Android sync switch pauses new sending and receiving; the Windows tray pause controls automatic Windows sending.

## Data and where it goes

- **Clipboard text and images:** encrypted using a pairing key held by your paired devices. Short Windows-to-Android text may travel inside an encrypted Google push message. Longer text and images are retrieved directly from your computer over your local or privately connected network. The project maintainer does not operate a clipboard storage or relay server and does not hold your pairing key.
- **Pairing and discovery information:** nearby discovery and pairing exchange device/computer names, network addresses, pairing identifiers and cryptographic public keys. Confirm the code on both screens before approving a pair. Private pairing keys and push subscriptions are stored on your devices; Android configuration uses Android Keystore encryption and Windows configuration uses Windows DPAPI.
- **Google push registration and delivery:** the Android app uses the UnifiedPush embedded FCM receiver and existing Google Play Services. Google processes registration and delivery information needed for this service, such as push routing identifiers and network/connection metadata. Encryption of clipboard content does not hide push metadata from Google. Google Play Services is a separate installed component governed by [Google's Privacy Policy](https://policies.google.com/privacy).
- **Other apps:** when you paste or share a received clipboard item, the receiving app can access that item. Its handling is governed by that app's own practices.
- **Download and support links:** opening our GitHub links uses your browser; sharing a download link uses the app you choose in Android's share sheet. GitHub and the selected app apply their own privacy practices. Do not put clipboard content, pairing keys or push endpoints in a public support issue.

The app does not create a user account, include advertising or analytics SDKs, sell clipboard content, or require a Firebase project or service-account key from you.

## Permissions

Internet/network access enables pairing, encrypted transfer and Google push registration. Notifications show receiving and transfer results. AndroidX WorkManager contributes wake-lock, boot and foreground-service permissions for scheduled work and compatibility; this does not mean an always-running receive service. The Android app does not request accessibility, contacts, location, camera, microphone, broad photo-library or all-files access. It reads an image URI only when you choose to send that clipboard image.

## Retention and deletion

Pairing configuration and push subscriptions remain on each device until replaced or its app data is cleared. Pausing does not delete pairing. Windows retains encrypted pending transfers for retry and normally removes them after acknowledgment, replacement or expiry; the transfer expiry is ten minutes, but a stopped computer cannot run cleanup until the service resumes.

Android keeps received images in its private app storage so other apps can paste them. When a new image is received, older images more than 24 hours old are cleaned up; this is not a timer that guarantees deletion after 24 hours, and the current image can remain longer. Clearing Android app storage or uninstalling removes the app's local data. Content already pasted into another app or held in the system clipboard must be removed separately.

Windows uninstallation preserves its configuration/data directory. To remove those records, delete the data directory identified by your installation after uninstalling, including retained encrypted configuration backups. Do not delete unrelated folders. Clipboard history or copies saved in other apps are controlled separately by Windows and those apps.

Google controls retention of its service metadata under its own policies. We cannot erase data held by the system clipboard, another app, another device or Google by clearing this app's storage.

## Questions and updates

For privacy questions, use the [project's issue form](https://github.com/c01-in/clipboard-bridge/issues/new). Describe the question without disclosing personal clipboard contents or credentials; issues are public. This policy will be updated when data handling changes, with a new effective date.
