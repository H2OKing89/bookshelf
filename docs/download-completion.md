# Download completion and source retention

A download is complete when its intended book identities and all discovered or
declared recording parts have verified, committed library files. Multiple parts
of one book cannot stand in for another book. Parts from different editions are
not combined to manufacture a complete recording.

## Import and completion are separate

Successful files can commit while another book or part remains unresolved.
Tracked imports keep independent source copies through staged publication and
database commit. The download stays pending or failed until completion can be
verified; it does not authorize client removal or source retirement merely
because some import results succeeded.

Automatic import and tracked manual import use the same completion verifier.
Manual file selections remain bound to their own download IDs, including when
several jobs contain the same book. Selecting a subset cannot hide other eligible
book files still present in that download's source inventory.

The inventory covers supported audio and ebook files at the mapped local output
path. It does not establish an unknown publisher total or discover files absent
from the local download. Client-side file inventory and pack membership remain
separate work. Unknown scope permits import while withholding cleanup.

## Durable evidence

The verifier checks distinct intended book IDs, coherent edition IDs, unique part
coverage, source paths and statistics, and live committed file IDs and paths.
The current import result exposes its committed file only after database
publication. Retry evidence may also come from history tied to the same download
and client; unrelated, grabbed-only, stale or earlier-attempt imports cannot
substitute for current evidence.

Before completion is published, the verifier synchronously stores a versioned
`importReceipt` in the existing download-history data. The receipt records the
download/client, mapped output, book/edition/file identities, normalized observed
part totals, source paths and statistics, and explicit Copy intent. This adds
history data without changing file identities or requiring a database migration.
An event subscriber's swallowed write error cannot acknowledge this save.

Receipt-write or import failures leave a retryable state and retain sources.
Incomplete parts do not authorize a completion event. Successful completion
clears its pending warning and repeated verification does not republish the same
live transition.

## Restart and cleanup

A saved `Imported` marker alone is insufficient. Restart, completion events and
periodic client cleanup validate the receipt against live files, identities and
the current source inventory. A changed output path invalidates the previous
path's cached evidence. Missing library files, changed associations, unexpected
source files and malformed receipts keep cleanup pending.

The client must report `Completed` before completion can retire sources, mark the
task imported or remove it. Queued, paused, downloading, warning and failed
statuses cannot borrow an older completion receipt. Cleanup retries source
retirement after a restart; a failed deletion cannot be bypassed by removing the
client task instead.

Older completion markers have no receipt and remain available in history. They
are held for rescan or review instead of granting cleanup automatically. A fresh
scan can use bound legacy file history when its live files and complete observed
inventory establish the missing proof; identities are not guessed from titles.

Tracked source retirement checks the whole inventory before deleting any proven
source file. It refuses library overlap, untrusted links, paths outside the
download, changed source statistics and missing or mismatched destinations.
Folders, archives and sidecars are not deleted by the file receipt. Configured
download-client cleanup can remove its completed task after revalidation.

Explicit manual Copy saves its retention policy synchronously before any selected
tracked file is imported. That policy is bound to the download, client and mapped
output, so it survives a partial import or restart before completion. It also
preserves sources in the completion receipt and removes a completed client task
with `deleteData=false`. Clients that cannot move their files keep
their source copies. Existing client removal settings still apply. Untracked
imports retain their existing import-mode behavior, and failed-download removal
keeps its independent existing policy.


## Blocked imports and file diagnostics

A recoverable import problem now uses queue state `importBlocked`; the existing
state numbers remain unchanged and the new state is appended. API v1 consumers
should recognize the additional string value or handle unfamiliar states.
Legacy incomplete-import history restores as blocked, retaining its messages
and typed rejection codes. A real failed/encrypted client download remains
eligible for the existing failed-download handling even after an import block.

With completed-download handling enabled, blocked imports retry after 30, 60,
120 and 240 seconds, then at most once every five minutes. The worker does not
advance that deadline merely by polling. Client/output changes and saved
settings or relevant metadata refreshes reset the delay. Restart restores the
block with its initial delay; retry timing is transient and adds no migration.
Disabling completed-download handling stops automatic import retries.

A valid committed completion receipt retries verification and source retirement
without running the importer again. A prior receipt hidden by an incomplete
marker can resume only after the same live-file, scope, path and grab checks;
it is synchronously re-acknowledged before cleanup. Unchanged blocked warnings
do not produce repeated incomplete-history events. Retrying post-completion
cleanup does not announce the same live completion again. These checks retain
the existing source/client cleanup authority and explicit Copy policy.

When no files import, diagnostics inspect a bounded local inventory without
following links, then may request the client's file list if no accepted media is
accessible. qBittorrent supplies bounded relative names and optional sizes; other
clients safely default to an empty list. Invalid paths, oversized inventory and
client failures preserve the original import warning. Remote inventory is passed
only to the extension classifier: it is never mapped to a local filesystem path,
a book identity or deletion permission.

The classifier recognizes supported audio and ebook containers, including EPUB,
kepub and PDF, separately from archives, executables, potentially dangerous
extensions, unsupported files and harmless cue/m3u/nfo/image sidecars. It uses
filename extensions rather than claiming a malware/content scan. Incidental
sidecars do not reject a valid book, and they cannot be imported as standalone
books. This work adds explanations and recovery, not D08's automatic per-indexer
failure, blocklist or redownload policy.

Under **Settings → Media Management → Advanced**, **Rejected File Extensions**
sets `userRejectedExtensions` in API v1 Media Management configuration. The
comma-separated value accepts optional dots and compound extensions; for example
`.MP3, m4b, .mp3` normalizes to `mp3, m4b`. Matching ignores case. Paths, wildcards,
malformed tokens and oversized values are rejected before saving. The default
is empty; an explicit empty string clears the policy, while omitted/null values
preserve it. New imports, including explicit manual imports, enforce the policy
before staging. Existing-file rescans retain their files and identities.

## Verified import completion webhooks

Edit a Webhook connection in **Settings → Connect** and enable **On Verified
Import Completion** to receive the `ImportComplete` event with `schemaVersion`
`1`. It uses its own setting. The **On Release Import** setting still controls
legacy `Download` payloads.

An `ImportComplete` payload contains a stable `eventId`, the completion time,
client identity, and indexer and release source when known. The `recordings`
array identifies the work, edition, recording, Audible region where applicable,
and committed parts. Each part can include size, quality, safe source and
imported filenames, and measured audio properties with freshness status.
`customFormatScores` captures the active profile, each replaced file's score,
and the newly committed file's score at import. Older or unavailable evidence
leaves the score data absent. The payload omits directories, raw tags, provider
settings, credentials, and request URLs.

Bookshelf claims the delivery attempt after it verifies the committed files and
handles source retirement. A partial import, rollback, or invalid receipt cannot
trigger this event. Claims are scoped to the client and completion, so cleanup
retries and restarts do not repeat the attempt. A crash or provider failure after
the claim can leave the event undelivered.
