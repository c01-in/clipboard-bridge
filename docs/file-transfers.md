# File transfers — 0.2.0 development preview

This feature is not in the published 0.1.4 installers. Update both endpoints to the matching development build to test it. A new public release still requires the maintainer’s approval.

## Clipboard or files

| Entry point | Result |
| --- | --- |
| Android: **Send clipboard** | Text or one image goes to the computer clipboard after a tap. |
| Android: **Send one image to clipboard** | The system picker allows one image; it goes to the computer clipboard. |
| Android: **Send files…** | The system picker allows multiple images, videos, documents or other files. All are saved as files. Even one image stays a file through this entry point. |
| Another Android app: share one image through **Clipboard Bridge** | The image goes to the computer clipboard. |
| Another Android app: share multiple images, a video, or attachments through **Clipboard Bridge** | Original files are saved on the computer. |
| Another Android app: share plain text through **Clipboard Bridge** | Opens an explicit Send clipboard button; no automatic text upload. A `.txt` attachment is a file. |
| Windows tray: **Send files…** | Select multiple files in the system picker and send them to Android storage. |
| Explorer: **Send to → Clipboard Bridge** | Sends the entire selected file list as one batch. On Windows 11, use **Show more options** for the classic Send to menu. |

Files never change the clipboard and are never opened or executed automatically. Original bytes are preserved; clipboard images retain PNG conversion and the 16 MiB limit.

## Storage and progress

Windows defaults to the signed-in user’s Downloads known folder, including redirected locations. Change it in **Settings → Save received files to → Change folder…**. The tray menu also opens this folder. The service stages bytes in its protected data directory; the user-session tray copies them to the destination, verifies SHA-256 and confirms the save. A persistent per-batch plan prevents duplicates after a tray restart. Existing files are never overwritten; collisions get numbered names. A folder change applies to new batches; an already planned batch keeps its destination.

Android 10+ defaults to **Downloads/Clipboard Bridge**, using MediaStore pending entries until each file is complete. The menu also offers a system folder picker. Android 8–9 require selecting a receiving folder first. No broad storage-access permission is requested.

Both endpoints show progress. Android displays a temporary foreground notification during file transfer and stops the service afterward. There is still no permanent Android clipboard listener or idle foreground service.

## Protocol and limits

`POST /api/files` exchanges AES-GCM-encrypted envelopes with the existing pairing key. Every request includes a fresh nonce, operation, transfer ID, generation and expiration; responses echo the nonce and ID. File index, offset and content are authenticated inside the envelope. Google receives only an encrypted wake-up, with no file names, paths or bytes.

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

On 21 October 2026, **112 Windows checks**, **19 Android instrumentation tests**, Android lint, signed APK/AAB builds and Windows packaging passed locally. The Android run used Android 15 / API 35 AOSP ATD and an isolated debug package. It sent an 18.25 MiB binary file, an actual PNG and an empty file through a separate app’s URI grants and the real foreground sender into the C# endpoint. The reverse test saved a 17.25 MiB binary file and an empty file into actual Android Downloads, verified SHA-256 and retried the completed receive without duplicating it. Cancellation and single-image/file-mode routing also passed.

The existing 16 local Google Play publishing safeguard scenarios passed; no live Play upload or new Google wake-up device test was performed. APK signing retains the existing certificate and versionCode increases to 9. The new Explorer Send to shortcut is included in the installer; its installation into the user’s daily Windows environment has not been tested in this run. Current installed production apps were left unchanged. The emulator and loopback test host were shut down afterward.

The current emulator automatically raised its requested 1 GiB memory to 2.5 GiB. Actual Activity views were drawn for layout inspection because the AOSP ATD display rendering is disabled; these are not full-screen device screenshots or store marketing images.

Run `dotnet run --project windows/Tests/Tests.csproj` for actual HTTP, encryption, named-pipe and destination-writer checks in an isolated temporary directory.

For Android/C# interoperability, start the test-only host:

```text
dotnet run --project windows/Tests/Tests.csproj -- --file-integration-host <new-absolute-test-directory>
```

Install the debug APK and instrumentation APK on an emulator, then run:

```text
adb -s <emulator> shell am instrument -w -r -e fileIntegration true com.colin.clipbridge.debug.test/androidx.test.runner.AndroidJUnitRunner
```

The test host listens only on Windows loopback port 45879 with a public test key. It substitutes only Google wake-up delivery. File requests, separate-app URI grants, the foreground service, Windows writes and Android Downloads writes use the production implementations. This does not verify new file wake-ups through Google on a physical phone. Never expose the test host or include it in an installer.

For separate-drive builds, set `CLIPBRIDGE_BUILD_ROOT` for .NET/Android intermediates and `CLIPBRIDGE_DIST` for packages, plus `TEMP`/`TMP`, `GRADLE_USER_HOME` and `NUGET_PACKAGES`. Keep signing keys and credentials outside disposable caches.
