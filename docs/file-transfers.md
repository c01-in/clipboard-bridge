# File transfers — 0.2.2 preview

Update both endpoints to 0.2.2 for PC-issued file sessions. The [Microsoft Store](https://apps.microsoft.com/detail/9PJT9PTSMCJQ) online installer downloads its currently published package; the complete signed MSIX is attached to GitHub after the matching version is certified and verified against the public Microsoft catalog. Older 0.1.4 installers do not support file transfers. See [version notes](release-notes/v0.2.2.md) and [Store details](microsoft-store.md).

## Clipboard or files

| Entry point | Result |
| --- | --- |
| Android: **Send clipboard** | Text or one image goes to the computer clipboard after a tap. |
| Android: **Send one image to clipboard** | The system picker allows one image; it goes to the computer clipboard. |
| Android: **Send files…** | The system picker allows multiple images, videos, documents or other files. All are saved as files. Even one image stays a file through this entry point. |
| Another Android app: share one image through **Send to computer** | The image goes to the computer clipboard. |
| Another Android app: share multiple images, a video, or attachments through **Send to computer** | Original files are saved on the computer. |
| Another Android app: share plain text through **Send to computer** | Opens an explicit Send clipboard button; no automatic text upload. A `.txt` attachment is a file. |
| Android file manager: **Open with → Send to computer** | Sends the selected file to computer storage, including a single image or text document. |
| Windows tray: **Send files…** | Select multiple files in the system picker and send them to Android storage. |
| Explorer on Windows 11: **Share → Clipboard Bridge** | Sends one or more files to Android storage through the native Share target. |
| Explorer: **Send to → Clipboard Bridge** | Sends the entire selected file list as one batch. On Windows 11, use **Show more options** for the classic Send to menu. |

The classic Send to shortcut belongs to the standalone desktop installer. The Store MSIX declares the native Share target without that legacy shortcut. The Windows sending window closes three seconds after success; failure and cancellation remain visible. See [Windows Share](windows-sharing.md).

Android's share and Open with entries use the localized action name **Send to computer**; the launcher keeps the app name. Open with accepts only the selected `content://` file with its temporary read grant. Availability depends on the source app offering a system Open with chooser; web links and raw filesystem paths are excluded.

Files never change the clipboard and are never opened or executed automatically. Original bytes are preserved; clipboard images retain PNG conversion and the 16 MiB limit.

## Storage and progress

Windows defaults to the signed-in user’s Downloads known folder, including redirected locations. Change it in **Settings → Save received files to → Change folder…**. The tray menu also opens this folder. The broker stages bytes in its data directory; the user-session tray copies them to the destination, verifies SHA-256 and confirms the save. The desktop broker is a Windows service; the Store broker runs as the signed-in user without elevation. A persistent per-batch plan prevents duplicates after a tray restart. Existing files are never overwritten; collisions get numbered names. A folder change applies to new batches; an already planned batch keeps its destination.

Android 10+ defaults to **Downloads/Clipboard Bridge**, using MediaStore pending entries until each file is complete. The menu also offers a system folder picker. Android 8–9 require selecting a receiving folder first. No broad storage-access permission is requested.

Both endpoints show progress. Android displays a temporary foreground notification during file transfer and stops the service afterward. There is still no permanent Android clipboard listener or idle foreground service.

## Protocol and limits

`POST /api/files` exchanges AES-GCM-encrypted envelopes with the existing pairing key. Every request includes a fresh nonce, operation, transfer ID and phone generation; responses echo the nonce and ID. File index, offset and content are authenticated inside the envelope. Google receives only an encrypted wake-up, with no file names, paths or bytes.

### PC-issued sessions (0.2.2)

The updated Android client first requests an encrypted `session` response from the PC. The opaque token is bound to the transfer ID, pairing key and phone generation. All subsequent file operations carry that token. The PC sets a one-hour lifetime and checks elapsed time using its monotonic clock; repeating session creation does not renew it. Android uses the returned relative lifetime with its own elapsed-time clock for cancellation, never to authorize a request. Neither the phone's request timestamp nor its manifest timestamp decides validity.

This covers both upload and download. File wake-ups are checked against the PC's live offer instead of comparing the phone clock to a PC timestamp. Pausing, changing pairing/generation, expiry and restarting the PC service invalidate tokens. Incoming metadata survives restart and can obtain a fresh token for its remaining PC-side lifetime; outgoing open handles still require sending again after restart. Persistence uses the PC's own timestamp to recover a bounded remaining lifetime after restart.

A new deliberate send can replace an incomplete upload after two minutes without an accepted start or chunk/completion operation. The PC checks inactivity with its own elapsed-time clock, recovers last progress from its manifest and chunk timestamps after restart, and revokes the abandoned transfer's session tokens. An active upload stays protected. A fully uploaded batch awaiting desktop saving is never replaced by inactivity.

Updated Android file transfers require a PC build containing this session endpoint. An older Android client can still use the existing authenticated, timestamp-based protocol against the updated PC. Legacy validation remains isolated to requests without session tokens. HTTP rejection is reported separately from connection failure, including when a stale endpoint is tried afterwards. These changes ship together in 0.2.2; update both endpoints for the new session protocol.

- Each chunk contains at most **256 KiB** of file bytes; HTTP bodies are bounded before parsing. Videos are never loaded whole into memory.
- Android upload: `start`, `write`, `finish`, `commit`, `result`, `abort`. Final SHA-256 and size must match. Unknown source sizes are supported and checked at completion.
- Windows download: `offer`, `read`, `saved`, `reject`. Metadata and encrypted chunks are fetched directly from Windows.
- A repeated chunk is accepted only if its bytes match. Changed overlapping chunks, sparse offsets and excess declared sizes are rejected.
- Success requires actual destination saving and an authenticated receipt, rather than successful HTTP submission.
- Remote file names are never used as paths. Windows invalid characters, reserved names and collisions are handled before saving.
- Explorer source files are opened while impersonating the authenticated pipe client, using that user’s filesystem permissions.
- Android pause invalidates the generation and rejects new chunks. Windows automatic clipboard pause still permits deliberate file sending.

One incoming and one outgoing batch can be active on the computer. A batch supports **1–1,000 files** and expires after **one hour**. File transfers have no 16 MiB per-file limit; storage, connection duration and Android background-work limits still apply. Folders and automatic recovery of outgoing source handles after a process restart are deferred. After interruption, check the destination before explicitly sending again; already saved files are retained.

## Verification

The initial verification below predates the later native Windows Share integration and full Store MSIX. It records that run's results, not the current installation or publication status. Store packaging, real per-user broker/IPC, complete companion download and API checks are recorded separately in [Microsoft Store](microsoft-store.md). Store-delivered installs/updates and Google file wake-ups on all physical devices remain unverified.

On 21 October 2026, **112 Windows checks**, **20 Android instrumentation tests**, Android lint, signed APK/AAB builds and Windows packaging passed locally. Android 15 / API 35 AOSP ATD used an isolated debug package. It sent an 18.25 MiB binary file, an actual PNG and an empty file through a separate app’s URI grants and the real foreground sender into the C# endpoint. The reverse test saved a 17.25 MiB binary file and an empty file into actual Android Downloads, verified SHA-256 and retried the completed receive without duplicating it. Cancellation and single-image/file-mode routing are covered, including actual foreground-service exit after a notification cancel action and rejection of a stale cancellation for another transfer.

The existing 16 local Google Play publishing safeguard scenarios passed; no live Play upload or new Google wake-up device test was performed. APK signing retains the existing certificate and versionCode increases to 9. The new Explorer Send to shortcut is included in the installer; its installation into the user’s daily Windows environment has not been tested in this run. Current installed production apps were left unchanged. The emulator and loopback test host were shut down afterward.

The initial emulator run automatically raised its requested 1 GiB memory to 2.5 GiB. The final run used an explicit QEMU 1 GiB override, verified by guest MemTotal of 989,452 KiB; host working set was about 1.7 GiB. Actual Activity views were drawn for layout inspection because the AOSP ATD display rendering is disabled; these are not full-screen device screenshots or store marketing images.

Run `dotnet run --project windows/Tests/Tests.csproj` for actual HTTP, encryption, named-pipe and destination-writer checks in an isolated temporary directory.

For Android/C# interoperability, start the test-only host:

```text
dotnet run --project windows/Tests/Tests.csproj -- --file-integration-host <new-absolute-test-directory>
```

Install the debug APK and instrumentation APK on an emulator, then run:

```text
adb -s <emulator> shell am instrument --user 0 -w -r -e fileIntegration true com.colin.clipbridge.debug.test/androidx.test.runner.AndroidJUnitRunner
```

The test host listens only on Windows loopback port 45879 with a public test key. It substitutes only Google wake-up delivery. File requests, separate-app URI grants, the foreground service, Windows writes and Android Downloads writes use the production implementations. This does not verify new file wake-ups through Google on a physical phone. Never expose the test host or include it in an installer.

For separate-drive builds, set `CLIPBRIDGE_BUILD_ROOT` for .NET/Android intermediates and `CLIPBRIDGE_DIST` for packages, plus `TEMP`/`TMP`, `GRADLE_USER_HOME` and `NUGET_PACKAGES`. Keep signing keys and credentials outside disposable caches.
