# Google Play publishing from GitHub Actions

Google Play provides an official publishing API. It uses OAuth, not a plain API key. After one-time Google-side authorization, CI can upload an existing AAB and submit a track edit without operating Play Console. Google still controls review and approval.

## Current deployment status

The publishing workflow uses signed AAB assets from GitHub Releases, so uploading an existing release does not require another build. The dedicated publisher is configured for this repository, with an encrypted GitHub secret and local DPAPI backup; automatic Play uploads are enabled. Forks must configure their own publisher. [The first real API submission](https://github.com/c01-in/clipboard-bridge/actions/runs/38984131014) uploaded and committed 0.1.4 (version code 8) to production on 16 October 2026. On 21 October 2026 (America/Los_Angeles), the official release API confirmed `RELEASE_LIFECYCLE_STATE_PUBLISHED` for 0.1.4. Each subsequent update still needs its own review; GitHub release publication does not mean Google Play approval.

## One-time authorization

1. In a Google Cloud project, enable **Google Play Android Developer API** (`androidpublisher.googleapis.com`). Create a dedicated service account for Play publishing. Do not reuse a Firebase sender account or grant project Owner/Editor.
2. In Play Console **Users and permissions**, invite the service account email. Scope access to **Clipboard Bridge** (`com.colin.clipbridge`). For the complete production update flow, grant app read access, **Release apps to testing tracks** (Google requires this to create any new release, including production), **Release to production**, and **Manage store presence** for product descriptions. [Google's release prerequisites](https://support.google.com/googleplay/android-developer/answer/9859348?hl=en) distinguish creating a release from publishing it. App-level **Admin (all permissions)** is an alternative only when the account owner explicitly chooses it; it does not require account-wide administration or access to other apps.
3. Configure one of the authentication methods below. Cloud IAM alone does not grant Play access, and the Android keystore is not a publishing credential. The developer account no longer needs to be linked to the Cloud project.

Official instructions: [Google Play API setup](https://developers.google.com/android-publisher/getting_started).

### Preferred: workload identity, without an exported private key

Configure a Google Cloud workload identity provider for GitHub's OIDC issuer and allow this repository to impersonate the dedicated Play service account with `roles/iam.workloadIdentityUser`. Restrict the provider to the exact repository ID/owner ID and trusted `refs/heads/main` or release tag refs; never trust all GitHub repositories or untrusted pull requests. Use service-account impersonation, since that account is the identity invited to Play.

Set these GitHub repository variables:

| Variable | Value |
| --- | --- |
| `GOOGLE_PLAY_WORKLOAD_IDENTITY_PROVIDER` | Full `projects/…/locations/global/workloadIdentityPools/…/providers/…` resource name |
| `GOOGLE_PLAY_SERVICE_ACCOUNT` | Dedicated service account email |

Run **Publish to Google Play**, with tag `v0.1.4` and mode `check-access`. After it succeeds, enable automatic release submission:

```powershell
gh variable set GOOGLE_PLAY_UPLOAD_ENABLED --body true --repo c01-in/clipboard-bridge
```

The workflow requests only the Android Publisher OAuth scope and creates no credential file. See [Google's authentication action](https://github.com/google-github-actions/auth) for provider setup.

### Alternative: dedicated service-account JSON key

If workload identity is unavailable, create a key for the dedicated publisher account. Keep the downloaded JSON inside ignored `local/`, never paste it into an issue, chat, build argument or public repository. On Windows, with repository administration access through `gh`, run:

```powershell
./scripts/Configure-PlayPublishing.ps1 -ServiceAccountFile ./local/play-publisher-key.json
```

The helper verifies authenticated Play read access before enabling automatic uploads. It saves the JSON as GitHub's encrypted `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` secret through standard input, and retains a DPAPI-encrypted backup at `local/play-publishing/service-account.clixml`, accessible to the current Windows user. This backup depends on the Windows account/profile: preserve its recovery separately. The helper does not delete the original download; retain it securely outside Git or remove it after confirming your recovery plan. The encrypted backup can be reused by running the helper without `-ServiceAccountFile`.

The helper refuses this installation's legacy Firebase sender identity. It does not grant Play permissions or upload an AAB. A successful read check does not prove release-write access; verify the first real submission. Revoke/rotate a JSON key if exposed. Workload identity takes precedence if both authentication methods are configured.

## Publish an existing release, including 0.1.4

Open [Publish to Google Play](https://github.com/c01-in/clipboard-bridge/actions/workflows/play-publish.yml), choose **Run workflow**, enter the existing tag, then select:

| Mode | Effect |
| --- | --- |
| `validate` (default) | Downloads the release AAB/checksum, verifies SHA-256/signature and versioned product/release text; no Google credential or upload |
| `check-access` | Also reads the track's release lifecycle states; no edit/upload |
| `submit` | Uploads/reuses the exact AAB, updates versioned product descriptions and track What’s new in one edit, validates and commits for review |

For production submission without rebuilding:

```powershell
gh workflow run play-publish.yml --ref main -f tag=v0.1.4 -f mode=submit -f track=production -f replace_review=false
```

Wait for the run and inspect its summary. Do not treat dispatching a workflow as completing it.

## Future releases

Keep Android/Windows versions aligned and increase Android `versionCode`. Each update must include both:

- `docs/release-notes/v0.x.y.play.txt`: English **What’s new**, 1–500 characters.
- `docs/release-notes/v0.x.y.play-listing.json`: product title, short description and full description, with the matching version and package. English `en-US` is required; explicit additional locales are supported. Title/short/full limits are 30/80/4,000 characters.

From 0.2.0, missing, invalid, oversized, duplicate-locale or mismatched product text stops publication. CI also verifies that the current source version includes these files. The workflow extracts both from the selected release tag, so a later `main` description cannot advertise new functions on an older AAB. Pre-0.2.0 tags keep the legacy notes-only path for no-rebuild uploads.

Push a new matching tag only after checks and channel prerequisites pass. The release workflow uses `WINDOWS_SIGNING_MODE=microsoft-store` to publish Microsoft's verified signed Store online installer for Windows; the alternative standalone SignPath route requires approved provider configuration. After GitHub assets are published, `GOOGLE_PLAY_UPLOAD_ENABLED=true` calls the Play workflow directly. This does not depend on a `release: published` event, which a release created using `GITHUB_TOKEN` would not trigger. Existing GitHub releases can still use the separate Play workflow without a rebuild.

The submission writes the listing and track notes into the **same edit as the AAB**. Existing promotional video, screenshots and locales not listed in the metadata are retained. An API rejection prevents the entire edit from committing; it does not submit the binary with stale descriptions. The service account requires **Manage store presence** for this app, in addition to its release permission. Google can accept listing/track draft writes and then reject `edits.validate` for missing permission: only a successful validation and commit establishes submission, not a draft PUT response. Failures identify the operation without printing private server messages. Do not grant account-wide administration or silently expand permissions to bypass a 403.

Version-specific 0.2.0 text is read from the release tag. Only a real `submitted` result with `committed=true` confirms Google accepted that release edit. Local HTTP fixtures cover serialization, existing-video retention, creation of a new locale, transaction ordering, invalid metadata, draft-write success followed by validation denial, listing permission failures and review protection. These tests do not establish live submission permission or approval.

Only trusted maintainers should modify workflows or push release tags. Ordinary PRs never receive the Play secret. There is no extra server, scheduled polling job or local build requirement.

## Review protection and honest status

- `artifact_validated`: release file and structure verified; no Play upload.
- `access_verified`: read access verified; no Play upload.
- `waiting_previous_review`: existing review preserved; this run did not commit a release. Run it again after that review finishes.
- `already_on_track`: identical version already configured; approval/public availability is not inferred.
- `submitted`: Google accepted the track edit. Review approval and public availability are separate.
- API/authentication/validation errors fail the job; they are not converted into a successful submission.

By default the workflow checks for an existing review before creating an edit, and commits with `changesInReviewBehavior=ERROR_IF_IN_REVIEW` to protect against concurrent reviews too. It never silently retries by canceling a review. The manual `replace_review` option explicitly uses `CANCEL_IN_REVIEW_AND_SUBMIT`, which cancels the existing review and sends **all pending app changes** for publishing; it can affect review timing. Use only when intentionally replacing that submission.

An uncommitted edit is deleted on failure; a confirmed commit is never automatically retried. If a network error makes the commit outcome unknown, inspect Play release state before retrying. Policy declarations, account verification, rejection resolution and some first-publication setup can still require Console actions. The workflow does not bypass them or Google's review.

Official reference: [commit behavior](https://developers.google.com/android-publisher/api-ref/rest/v3/edits/commit), [listing updates](https://developers.google.com/android-publisher/api-ref/rest/v3/edits.listings/update), [transactional edits](https://developers.google.com/android-publisher/edits), [release lifecycle states](https://developers.google.com/android-publisher/api-ref/rest/v3/applications.tracks.releases/list).
