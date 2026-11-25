# Audiobook import extension design (D30)

**Decision, 2026-11-24:** retain Bookshelf's own transfer and completion
pipeline. Do not port Sonarr's script-controlled import override. Existing
post-commit notifications are the right boundary for integrations that do
not need to change media. A future transformation extension may propose
verified staging outputs, but must never own the import transaction, recording
identity, library destination, or client cleanup decision. This is a design
review, not a new executable extension protocol or setting.

## Findings from the pinned Sonarr v5 comparison

The sole upstream comparison is Sonarr v5.0.0.2941 at
`c018578e4b79ee048575c02d7a8aa2b929e4b837`.

| Upstream behavior | Bookshelf decision |
| --- | --- |
| [ImportScriptService](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/ScriptImportDecider.cs) executes an external process with source/destination and episode metadata. Its response recognizes one media file, optional extras and move statuses. | Do not import that path-based response or its environment contract. A single returned media file cannot represent a multipart recording or a release covering multiple recordings. |
| [EpisodeFileMovingService](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeFileMovingService.cs) can treat the script's move decision as the transfer outcome. | An extension must not signal successful transfer. Bookshelf must own publication, database commit, and verification of live files. |
| The upstream script can return paths for extras or suppress normal extra import. | Continue to classify and import allowed supporting files through Bookshelf's existing extras pipeline. No arbitrary extra-output authority. |

## Existing boundaries that must be preserved

- [ImportApprovedBooks](../src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs)
  first resolves book and edition identities, collects qualified parts, and
  stages replacement media. It publishes `TrackImportedEvent` only after file
  IDs are committed, followed by per-book and batch events. A committed batch
  can contain successful files while another book or part remains unresolved.
- [UpgradeMediaFileService](../src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs)
  validates destinations, preserves old files until the replacement recording
  is published and committed, and has a rollback boundary. An extension cannot
  bypass that boundary or choose the final path.
- [DownloadImportCompletionService](../src/NzbDrone.Core/Download/DownloadImportCompletionService.cs)
  verifies the whole observed source inventory, expected book identities,
  coherent editions and part coverage, current source statistics and live
  committed file IDs. It stores the receipt synchronously before completion.
  [CompletedDownloadService](../src/NzbDrone.Core/Download/CompletedDownloadService.cs)
  can then publish `DownloadCompletedEvent`. These checks, not a process exit
  code or a batch event, establish download completion.
- [CustomScript](../src/NzbDrone.Core/Notifications/CustomScript/CustomScript.cs)
  is already a configured notification provider. Its Download event uses
  committed per-book import data. It is not a transfer extension or a whole
  download completion receipt. Its script runs with the service's process
  permissions; notification failure is handled separately in
  [NotificationService](../src/NzbDrone.Core/Notifications/NotificationService.cs).
  No read-only execution guarantee is implied by that existing integration.

See [download completion](download-completion.md) and
[identity preservation](identity-preservation.md) for the current import and
cleanup contracts. Preserve them for tracked and untracked manual imports,
explicit Copy, local and Audible recordings, and distinct marketplace editions.

## Proposed extension tiers

### A. Post-commit observer (preferred)

Use the existing Connect notification for trusted integrations that simply
index, notify or inspect a committed book. It must not edit, move or delete
imported files, change their identity, or call download-client cleanup. There
is no need to replace the importer for these activities.

If a new batch-aware observer is needed, use the committed file/edition IDs
from `BookImportBatchCompletedEvent` and a versioned notification envelope
whose delivery status is explicitly independent of import success. A
`recordingFilesCommitted` observation carries the exact committed file set;
it must not call the recording or the download complete. A separate
`downloadVerified` observation is available only after a synchronously saved,
valid import receipt. Where restart/retry delivery is required, design a
persistent event ID and bounded delivery state; a retry of that event cannot
rerun import or retire sources. The receiver must be able to deduplicate an
at-least-once delivery. Do not infer a per-download notification from the
per-book event count.

### B. Staged media transformation (future, separate opt-in)

Examples include container normalization of a confirmed audio part or
metadata/chapter-preserving format conversion. This tier is **not enabled**
and is justified only by a concrete product need. Initially admit a
one-to-one source-part/output mapping for every approved part in a recording.
Several outputs are normal for several input parts/recordings, but they are
not permission to split or merge media, add parts, combine unrelated
recordings, or fill in missing inventory. Combining or splitting source files
would require a separately designed receipt revision and preservation proof.
Current receipt rows bind a source to a live file and must not be forged to
simulate a many-to-many mapping.

The app creates an immutable work description from already approved import
decisions and exact catalog/local recording identities. The worker sees only
opaque app-issued recording and source tokens plus its own staging inputs.
Neither a title, ASIN string supplied by the worker, nor a source filename
may establish an application identity. The app retains the authoritative
book/edition IDs, marketplace/local identity, source/client/output context,
part index and total, size and modification evidence, quality profile and
Copy intent. Do not pass authorization credentials, host settings, database
access, provider secrets or client cleanup capabilities to the worker.

The proposed version-one response below describes **candidate outputs**, not
committed files. It is illustrative design material, not a supported API.

```json
{
  "schemaVersion": 1,
  "requestId": "owned-proposal-41",
  "decision": "proposeOutputs",
  "recordings": [
    {
      "recordingToken": "recording-a",
      "outputs": [
        { "sourceToken": "part-a1", "relativePath": "audio/part-01.m4b" },
        { "sourceToken": "part-a2", "relativePath": "audio/part-02.m4b" }
      ]
    },
    {
      "recordingToken": "recording-b",
      "outputs": [
        { "sourceToken": "part-b1", "relativePath": "audio/part-03.m4b" }
      ]
    }
  ]
}
```

An explicit `useOriginals` decision carries no output entries and selects the
normal import path before any publication. A failed or malformed response is
not `useOriginals`: block that transformation attempt for review or the
existing retry policy, retain all sources, and never silently commit an
unverified output. The proposed response contains no final path, client
status, deletion instruction, recording or part reassignment, or successful
completion marker. Validate the version, request correlation, exact set of
recording/source tokens, unique output paths, and complete one-to-one mapping
against the immutable work description. Unknown fields and ambiguous or
incomplete proposals must be rejected, not guessed. The server, not the
worker, chooses destination names from the normal audiobook naming logic.

## Required execution and ownership design

1. **Private workspace and capability boundary.** Use a new app-owned staging
   area per attempt, a read-only copy of the approved input, writable output
   confined to that area, and explicit resource limits. Do not give the worker
   direct writes to the download source, library, database, configuration or
   existing app staging areas. An in-process plugin or an arbitrary executable
   running with the same OS privileges cannot provide that isolation merely by
   checking returned paths; a suitably isolated execution environment is a
   prerequisite. If the actual supported platform cannot enforce the boundary,
   transformation support remains unavailable there. No implicit capability
   increase is permitted via environment, network, or inherited descriptors.
2. **Bounded proposal.** Only regular media files in the owned output area can
   become candidates. Validate normalized relative paths and actual file
   identity/ownership, reject links and aliases, duplicates, collisions,
   incomplete coverage, excessive inventory/size/output and unsupported
   formats. Bound execution time, concurrency, storage, response size and
   captured diagnostics. Cancellation terminates/reaps the owned worker and
   cleans only its own staging files. Do not treat worker-reported metadata or
   process success as verification.
3. **Independent media and identity checks.** Re-probe all output files using
   Bookshelf's bundled MediaInfo and supported tag readers. Preserve source
   provenance and the exact approved edition and part mapping. Compare the
   recording, ordered parts, chapters/runtime and retained metadata with the
   original evidence; treat missing, contradictory or materially altered
   identity/part evidence as a reviewable block. Apply the user's existing
   quality policy to actual output, not a claimed codec or score. Any
   intentional lossy conversion/change of format requires explicit product
   policy and must not silently replace a higher-quality recording. No automatic
   new author/edition is inferred from an extension result.
4. **App-owned publication.** Prepare every candidate in the existing staging
   transaction. Recheck input and destination state immediately before publish.
   Publish a coherent recording as a group, retain prior files until database
   commit, and roll back the group's staged publication on a failed part or
   commit. Only committed file IDs/paths may enter normal history and
   notifications. A book can commit independently while another remains
   unresolved; this never establishes download-wide completion.
5. **Receipt and restarts.** Maintain original input provenance and actual
   verified outputs in the receipt. A process exit, incomplete batch, stale
   receipt, or missing output cannot acknowledge download completion. A retry
   must revalidate the same source/client/recording scope and live output
   evidence; do not rerun a completed transformation solely due to restart.
   Uncertain/changed evidence returns to review. Existing Copy intent and
   source retirement checks remain solely with the normal completion verifier
   and client status/cleanup policy. Never let an extension acknowledge or
   remove a download task itself.
6. **Default/configuration compatibility.** The proposed feature is opt-in,
   scoped to authorized administrators and explicitly supported platforms.
   Disabled/unsupported configurations retain the ordinary importer exactly.
   Do not change existing `Custom Script` connection settings, rename event
   fields, enable arbitrary process execution from downloaded content, or use
   source filenames as executable instructions. The actual runtime feature
   needs a separate implementation review, settings/API compatibility plan,
   and independently validated worker isolation before enablement.

## Acceptance matrix for any implementation

| Case | Required outcome |
| --- | --- |
| Disabled extension; explicit use-originals | Current ordinary import, explicit Copy and source retention are unchanged. |
| Two parts in one ASIN/marketplace; second recording in same download | Independently mapped, complete recording groups; each output has current input provenance; no duplicate download completion. |
| US and UK variants, local non-Audible recording, similar title or conflicting tags | No identity merge, part reassignment or region guessing. Conflicting evidence remains reviewable. |
| Missing/unexpected token, malformed version, duplicated path, non-media or unowned output | No publish, history success, notification success or cleanup for that recording. Source retained. |
| Proposed merge/split, unapproved extras, unsupported codec or altered duration/chapter mapping | Reject under initial one-to-one policy; do not invent receipt evidence. |
| One part fails media probe or publication; DB commit fails | Prior recording intact, owned candidate cleanup/rollback, no false success. |
| Another recording succeeds in same source download | Successful recording may retain its committed files; incomplete download remains blocked for cleanup. |
| Worker fails, times out, is cancelled or attempts to use resources outside its permitted workspace | Bounded termination; fail-closed staging, no writes to originals or app state; retry may use reverified inputs. |
| Concurrent duplicate attempts or changed input/destination | Exact attempt ownership, collision avoidance and revalidation; no unrelated file changes or duplicate history. |
| Restart after staging, publication or committed receipt | Recover only owned attempt state; no re-import or duplicate notification from a proven receipt; uncertain state requires verification. |
| Explicit Copy or completed seeding task | Source preserved according to existing policy; script cannot authorize retirement or client removal. |
| Post-commit observer failure/retry | Import remains committed; notification state is separate, exact event identity retained; no extra transfer. |
| Unsupported worker platform, absent opt-in | Transformation unavailable; current behavior remains unchanged. |

The future implementation must reproduce these cases with disposable synthetic
audio and ebook media, current importer/receipt regressions on SQLite and
PostgreSQL, real isolated workers on every supported platform, and current
package/API/browser proofs. Test output paths with ordinary filename and
filesystem edge cases and actual cancellation/cleanup. A schema-only test or
mocked successful worker cannot prove OS confinement or atomic media behavior.

## Decision and next trigger

D30's design/study is complete. Keep current import logic and existing Connect
notifications unchanged; do not add a `UseScriptImport` option or port the
upstream move-status protocol. Reopen implementation only for a concrete
post-processing need, with an agreed initial media transformation and worker
isolation supported by the actual release platforms. Broader merging, splitting
or transcode/quality-policy semantics are not approved by this design.
