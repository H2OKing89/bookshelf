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
