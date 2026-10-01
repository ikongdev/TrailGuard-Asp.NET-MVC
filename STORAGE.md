# TrailGuard upload storage

New uploads use `Storage:Provider`, independently of `Database:Target`. The checked-in default is Local with an explicit `development` namespace. No database connection, bucket creation, or remote health probe occurs when storage configuration is resolved. Storage failures never switch providers.

## Configuration

Example deployment settings (placeholders only; set these in server configuration, never frontend code):

```text
Storage__Provider=Supabase
Storage__Namespace=<stable-dataset-name>
Storage__Supabase__Url=https://<project-ref>.supabase.co
Storage__Supabase__SecretKey=sb_secret_<server-only-secret>
Storage__Supabase__PublicImagesBucket=trailguard-public-images
Storage__Supabase__PrivateDocumentsBucket=trailguard-private-documents
Storage__Supabase__TimeoutSeconds=20
```

The colon equivalents work in User Secrets. The existing `Storage:Supabase:Url` and `Storage:Supabase:SecretKey` names are preserved. Only modern `sb_secret_` keys are supported; do not supply the database password, publishable key, or a legacy JWT service-role key.

`Storage:Provider` accepts exactly `Local` or `Supabase`; absence defaults to Local, explicit blank is invalid. `Storage:Namespace` is required: 1–32 lowercase letters/digits/hyphens, starting with a letter/digit. Assign a different stable namespace to every independent dataset/environment. Changing Database:Target does NOT change the namespace. Before switching independent databases, explicitly select that dataset's namespace too. Never share a writable namespace between independent databases, even if their row IDs happen to match.

Bucket settings are optional with the exact approved names above as defaults. Other names are rejected. Timeout is optional, 20 seconds by default, accepted range 1–60. `Storage:Local:RootPath` optionally overrides `App_Data/uploads` relative to the application content root; the root must be disjoint from wwwroot. App_Data is ignored by Git and excluded from publish content. No separate legacy-root setting is needed: legacy references read only the current web root.

Keep namespace, project origin and bucket configuration stable. Even when new writes use Local, retain Supabase connection settings to read existing private cloud references. Keep the Local root available to read managed Local references after switching new writes to Supabase. This phase does not migrate either kind of file.

Limits are fixed safety rules, not configurable bypasses: 5 MiB (`5242880` bytes) per image/document, nine images per Add Trail request (one cover plus at most eight additional), 40 million decoded pixels per trail image. Edit Trail uses the same per-request additional-photo limit. MVC multipart, Kestrel, IISServerOptions and IIS request filtering allow 50 MiB (`52428800` bytes) aggregate. Any additional Azure ingress/proxy must allow at least that body size. Local multipart buffering may still use temporary disk; it is not persistent upload storage.

## Proposed buckets and policies — not applied

| Bucket | Public | Max file size | Allowed MIME types |
|---|---|---:|---|
| trailguard-public-images | Yes | 5242880 bytes | image/jpeg, image/png |
| trailguard-private-documents | No | 5242880 bytes | image/jpeg, image/png, image/webp, application/pdf |

All create/read-private/delete operations originate on the MVC server with its secret API key. No browser Supabase client or Supabase Auth integration is introduced. Do not add permissive `storage.objects` SELECT/INSERT/UPDATE/DELETE policies for `anon` or `authenticated` for these buckets. Audit/remove existing broad policies before rollout; absence of new policies alone cannot neutralize a pre-existing permissive one. Public image retrieval is intentionally anonymous through the public bucket. Private retrieval remains proxied through MVC, which authorizes the participant owner or the event's OrganizerId; Admin receives no new exception.

Bucket MIME/size restrictions supplement server validation, not replace it. Key-based server requests bypass RLS, so MVC authorization is mandatory.

Official references checked for this implementation:

- https://supabase.com/docs/guides/getting-started/api-keys (opaque secret keys use `apikey`, not Bearer)
- https://supabase.com/docs/guides/storage/uploads/standard-uploads (POST object, immutable names/no upsert)
- https://supabase.com/docs/guides/storage/serving/downloads (authenticated object GET)
- https://supabase.github.io/storage/ (Storage REST API)

## References and access

Generated object keys are `<namespace>/<category>/<32-lowercase-hex-GUID>.<normalized-extension>`. Categories are only trails, profiles, receipts and medical-clearances. Original filenames are never storage keys.

- Public Supabase: configured-origin `/storage/v1/object/public/trailguard-public-images/<key>`.
- Private Supabase: configured-origin `/storage/v1/object/authenticated/trailguard-private-documents/<key>`. This is an internal stored locator, never rendered into a document link.
- Public Local: `/media/uploads/<key>`, served only for the two public image categories.
- Private Local: `local:v1/<key>`, stored outside wwwroot and served only through DocumentsController.
- Legacy: `/images/trails/…`, `/images/profiles/…`, `/uploads/receipts/…`, `/uploads/medical-clearances/…` retain their original local semantics. Legacy documents use the confined/signature-checked reader. They are never deletion targets.

The parser uses exact configured prefixes plus an anchored filename grammar, rejecting alternate hosts/buckets/namespaces, traversal, encoding, query strings, fragments, extra segments and unknown extensions. Local paths reject symlinks/junctions. No arbitrary stored URI is used as an authenticated HTTP request. HTTP redirects and cookies are disabled; credentials are attached per request and redacted from HttpClient logs. Non-success provider response bodies are not surfaced.

DocumentsController authorizes before reading, retains safe filename/type headers, `private, no-store`, `nosniff`, restrictive CSP, X-Frame-Options and inline/download behavior. Bounded downloads are buffered into at most 5 MiB and exposed as a seekable stream to preserve MVC range handling. Provider responses/streams and timeout tokens are disposed. Organizer availability checks use the same reader, so missing/invalid/outage documents safely appear unavailable without clearing their database fields.

PublicImageDisplay maps missing Local/legacy references to null at rendering time, selecting each existing initials/no-image branch. Trail photo galleries retain their existing error handlers. It does not edit database references or contact cloud storage. Bundled landing/achievement assets are unchanged.

## Persistence and concurrency

UploadAttempt records only the request's own successful uploads. UploadPersistence explicitly begins a database transaction, saves, then commits. Before persistence or after an acknowledged rollback, disposal compensates only those new uploads. Once COMMIT is attempted, an exception is treated as uncertain: objects are retained and logged for manual reconciliation. Cleanup failure is logged and never reverses a successful save or replaces the primary error. A remote upload failure/timeout can itself leave an unreferenced object; its generated key is logged, no automatic cleanup task runs.

Synchronization added:

- Trail edit uploads first, then takes a PostgreSQL `SELECT ... FOR UPDATE` lock on that Trail row, rechecks activation and the original cover reference, and writes the cover/additional-photo graph within that transaction. A competing cover replacement fails with a generic reload message and compensates only its own uploads after rollback. This is a database lock, not an in-process mutex.
- Profile update wraps Identity's existing ConcurrencyStamp-based update in an explicit EF transaction. A stale update fails and rolls back before compensation. The old managed profile image is cleaned up only after confirmed commit.
- Receipt upload preserves the existing event-capacity lock and authoritative status/ownership reload. It cleans up a replaced managed receipt only after confirmed commit.
- Registration preserves acquisition order: event-capacity lock, then participant/event lock, then authoritative reloads. Medical upload and reference persistence remain within this workflow.

Photo/Trail deletion commits database changes before attempting deletion of managed additional-photo objects. Covers are retained conservatively even after replacement or deletion, because snapshots or legacy datasets may share them. Legacy files are never deleted, even after a row/reference changes. Event deletion/cancellation does not introduce a new document-retention policy. No bulk cleanup, migration utility or scheduled reconciliation is included.

No schema migration: existing string columns hold these references; generated public URLs are checked against the event snapshot's 300-character limit. No concurrency columns were added. Event covers still capture the trail reference; PreparationPlan stays text. ML, screening, workflow locks and capacity logic are unchanged.

## Verification and rollout limits

Database-free suite:

```powershell
dotnet run --project Verification/TrailGuardStorageVerification/TrailGuardStorageVerification.csproj
npm run build
dotnet build
git diff --check
```

The suite uses synthetic temporary local files, fake HTTP transport and fake transactions; it does not start TrailGuard, load User Secrets or connect to any database/project. It checks configuration/reference isolation, legacy protection, file limits, provider failures, credential headers, redirect settings, actual document-controller allow/deny behavior and response metadata, failure compensation and a simulated stale writer. The fake concurrency test is not a live PostgreSQL lock/Identity concurrency test.

Implementation verification: the storage suite passed 240 checks, including execution of MVC byte-range responses; normal `dotnet build` passed with zero warnings/errors; `npm run build` and `git diff --check` passed. An extra run of `TrailGuardV2SubmissionGuardsVerification` failed its existing organizer blank-reason assertion (Program.cs:153): its dummy localhost:1 fixture expects validation before any database transaction, while the unchanged organizer action starts the transaction first. No live database was reached. That unrelated fixture/action was not changed to make the check pass.

Before deployment, separately authorize and verify bucket settings/policies and a real upload/read/delete round trip. Test restart/instance changes, public images and anonymous denial of private raw URLs, owner/other participant/owning organizer/unrelated organizer/Admin cases, byte-range downloads, concurrent profile/cover replacements, receipt rejection/reupload, multi-image aggregate limits, and rollback/timeout behavior. Browser-check desktop/mobile galleries, existing image fallbacks, document unavailable state, keyboard/focus behavior and form errors. No running app should be stopped to obtain a build; use an external artifacts directory if its output is locked.

No existing files are copied to Supabase. Hosting without the old files intentionally shows fallbacks/unavailable documents; existing reference fields and medical-clearance requirements stay intact. User Secrets are not deployment settings: supply server environment settings explicitly. This phase creates no buckets/policies, connects to no live database/storage, and runs no migration or file-transfer operation.

## Implementation handoff � changed files

No files were deleted. Initial state: main at d0a0144, clean working tree. No commit/push, schema migration, package installation, live storage/database operation or application startup was performed. No pre-existing user changes existed to overwrite.

| Files | Purpose |
|---|---|
| Controllers/TrailController.cs | Storage-backed create/edit/photo deletion, shared-cover retention, decoder reuse, database row-lock/recheck |
| Controllers/SettingsController.cs | Profile storage, Identity concurrency and transaction-aware replacement compensation |
| Controllers/RegistrationController.cs | Receipt/clearance storage and compensation; original locks/status checks retained |
| Controllers/DocumentsController.cs | Authorized storage proxy and database-free authorization test seams |
| Controllers/OrganizerController.cs | Storage-backed document availability |
| Controllers/MediaController.cs (new) | Public Local image-only route |
| Services/IUploadStorage.cs (new) | Storage interface, document result, bounded byte reader |
| Services/UploadStorageOptions.cs (new) | Independent provider configuration and fixed upload/request limits |
| Services/UploadReference.cs (new) | Strict owned-reference grammar and confined local paths |
| Services/SupabaseFileStore.cs (new) | REST transport, opaque-key authentication, redirect isolation and timeout/disposal |
| Services/UploadStorage.cs (new) | Provider routing, private reads, Local operations and legacy read compatibility |
| Services/UploadAttempt.cs (new) | Request-owned uploads, rollback compensation and post-commit cleanup |
| Services/UploadPersistence.cs (new) | Transaction outcome/commit-uncertainty handling |
| Services/PublicImageDisplay.cs (new) | Safe display references and existing missing-image fallback selection |
| Services/DocumentUploadValidator.cs | 5 MiB limit; removed obsolete local filename generator |
| Program.cs | DI, authenticated HTTP header redaction and aggregate request limits |
| appsettings.json | Non-secret Local provider and explicit development namespace defaults |
| TrailGuard.csproj, .gitignore | Keep managed Local upload data out of Git/publish content |
| web.config (new) | Matching IIS request-filter limit for multi-image submissions |
| Views/_ViewImports.cshtml | Shared image-display helper injection |
| Views/Admin/Accounts.cshtml, Views/Admin/Index.cshtml | Preserve existing avatar fallbacks when legacy files are missing |
| Views/Event/Details.cshtml, Views/Event/Index.cshtml | Preserve existing event-cover/avatar fallbacks |
| Views/Organizer/PostEventAssessment.cshtml, Views/Organizer/RegistrationDetails.cshtml | Preserve participant avatar fallbacks |
| Views/Participant/Details.cshtml, Views/Participant/Events.cshtml, Views/Participant/Index.cshtml, Views/Participant/Trails.cshtml | Preserve event/trail/profile fallbacks |
| Views/Profile/Index.cshtml, Views/Settings/Index.cshtml | Preserve initials fallback without rewriting profile references |
| Views/Shared/Components/_NavbarAdmin.cshtml, _NavbarOrganizer.cshtml, _NavbarParticipant.cshtml | Preserve navbar initials fallbacks |
| Views/Trail/Index.cshtml | Preserve cover/modal fallback for missing legacy files |
| Views/Registration/Register.cshtml, Views/Registration/MyRegistrations.cshtml | Show accepted document types and approved size limit |
| Verification/TrailGuardStorageVerification/Program.cs (new) | 240 database-free storage checks |
| Verification/TrailGuardStorageVerification/TrailGuardStorageVerification.csproj (new) | Verification console project; no new packages |
| STORAGE.md (new) | Configuration, bucket/policy proposal, implementation behavior, verification and handoff |

Focused review locations: SupabaseFileStore.Request/CreateHandler (authentication and redirects); UploadReferences.Parse/ConfinedPath (reference/path isolation); DocumentsController.ServeAsync/CanAccess (authorization before storage); DocumentUploadValidator and TrailController.EditTrail (validation); UploadAttempt and UploadPersistence (compensation/uncertain commits); TrailController.EditTrail and SettingsController.UpdateProfile (concurrent replacements).

Final required checks: storage suite passed 240 checks; npm run build passed; normal dotnet build passed with 0 warnings/0 errors; git diff --check passed. An early compile error in the IIS options namespace was corrected before final checks. The additional existing submission-guard suite failure is recorded above; a source comparison confirmed the full organizer decision implementation is identical to HEAD. No locked-output workaround was necessary. Tailwind output was unchanged; the document-hint utilities were confirmed present.

Review evidence below is literal output. Plain git diff --stat excludes new/untracked files; all new files are explicitly listed separately.

### git status --short

```text
 M .gitignore
 M Controllers/DocumentsController.cs
 M Controllers/OrganizerController.cs
 M Controllers/RegistrationController.cs
 M Controllers/SettingsController.cs
 M Controllers/TrailController.cs
 M Program.cs
 M Services/DocumentUploadValidator.cs
 M TrailGuard.csproj
 M Views/Admin/Accounts.cshtml
 M Views/Admin/Index.cshtml
 M Views/Event/Details.cshtml
 M Views/Event/Index.cshtml
 M Views/Organizer/PostEventAssessment.cshtml
 M Views/Organizer/RegistrationDetails.cshtml
 M Views/Participant/Details.cshtml
 M Views/Participant/Events.cshtml
 M Views/Participant/Index.cshtml
 M Views/Participant/Trails.cshtml
 M Views/Profile/Index.cshtml
 M Views/Registration/MyRegistrations.cshtml
 M Views/Registration/Register.cshtml
 M Views/Settings/Index.cshtml
 M Views/Shared/Components/_NavbarAdmin.cshtml
 M Views/Shared/Components/_NavbarOrganizer.cshtml
 M Views/Shared/Components/_NavbarParticipant.cshtml
 M Views/Trail/Index.cshtml
 M Views/_ViewImports.cshtml
 M appsettings.json
?? Controllers/MediaController.cs
?? STORAGE.md
?? Services/IUploadStorage.cs
?? Services/PublicImageDisplay.cs
?? Services/SupabaseFileStore.cs
?? Services/UploadAttempt.cs
?? Services/UploadPersistence.cs
?? Services/UploadReference.cs
?? Services/UploadStorage.cs
?? Services/UploadStorageOptions.cs
?? Verification/TrailGuardStorageVerification/
?? web.config
```

### git diff --stat

```text
 .gitignore                                        |   1 +
 Controllers/DocumentsController.cs                |  21 +-
 Controllers/OrganizerController.cs                |  13 +-
 Controllers/RegistrationController.cs             | 117 +++++------
 Controllers/SettingsController.cs                 | 138 +++----------
 Controllers/TrailController.cs                    | 240 +++++++---------------
 Program.cs                                        |  14 ++
 Services/DocumentUploadValidator.cs               |  19 +-
 TrailGuard.csproj                                 |   2 +
 Views/Admin/Accounts.cshtml                       |   8 +-
 Views/Admin/Index.cshtml                          |   4 +-
 Views/Event/Details.cshtml                        |  16 +-
 Views/Event/Index.cshtml                          |   4 +-
 Views/Organizer/PostEventAssessment.cshtml        |   2 +-
 Views/Organizer/RegistrationDetails.cshtml        |   4 +-
 Views/Participant/Details.cshtml                  |  12 +-
 Views/Participant/Events.cshtml                   |   4 +-
 Views/Participant/Index.cshtml                    |   4 +-
 Views/Participant/Trails.cshtml                   |   6 +-
 Views/Profile/Index.cshtml                        |   4 +-
 Views/Registration/MyRegistrations.cshtml         |   1 +
 Views/Registration/Register.cshtml                |   2 +-
 Views/Settings/Index.cshtml                       |   4 +-
 Views/Shared/Components/_NavbarAdmin.cshtml       |   2 +-
 Views/Shared/Components/_NavbarOrganizer.cshtml   |   2 +-
 Views/Shared/Components/_NavbarParticipant.cshtml |   2 +-
 Views/Trail/Index.cshtml                          |   8 +-
 Views/_ViewImports.cshtml                         |   1 +
 appsettings.json                                  |   6 +-
 29 files changed, 236 insertions(+), 425 deletions(-)
```

### Untracked files created

```text
Controllers/MediaController.cs
STORAGE.md
Services/IUploadStorage.cs
Services/PublicImageDisplay.cs
Services/SupabaseFileStore.cs
Services/UploadAttempt.cs
Services/UploadPersistence.cs
Services/UploadReference.cs
Services/UploadStorage.cs
Services/UploadStorageOptions.cs
Verification/TrailGuardStorageVerification/Program.cs
Verification/TrailGuardStorageVerification/TrailGuardStorageVerification.csproj
web.config
```
