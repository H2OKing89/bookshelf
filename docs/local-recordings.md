# Local audiobook recordings

For recordings distributed outside Audible, open **Unmapped**, select the files
for one recording, and choose **Create local recording**. Review the title,
author, narrators, publisher, language, and year before creating it. Choose an
existing library author or **Create a local author** if the publisher or author
has no suitable catalog entry. A new local author must have its own folder
inside a configured audio root containing the selected files; it uses the
root's default profiles and starts unmonitored. No catalog lookup is needed.

Bookshelf reads the files' tags and creates a separate local book and edition.
It does not invent an ASIN or combine the recording with an Audible edition.
Catalog refresh preserves local metadata. Automatic searching starts disabled.
Creating a recording changes library associations only: file paths, bytes,
import dates, file IDs, and release provenance are preserved.

Select files from one folder, optionally with numbered `CD`, `Disc`, or `Disk`
subfolders. Multipart files must have consistent album tags. Parts are ordered
by disc, track, then filename; review the warning if track numbers are missing
or repeated. Separate performances and editions should be created separately.
Files with an ASIN or conflicting identifiers require catalog-match review.

## Correct local recording metadata

Open a local recording and choose **Edit local metadata**. Corrections to the
title, subtitle, ordered narrators, publisher, language, year and chapter timings
are stored in the library without rewriting the audio. Leave an unknown year
blank. Chapter timings use milliseconds from the beginning of the complete
recording. Existing field provenance distinguishes file tags, file-name evidence,
manual corrections, older metadata and unknown values.

The recording and file IDs remain unchanged. If another editor saves before
you do, reload the latest metadata before reapplying the correction. Refresh,
rescan and restart preserve these local corrections. Local recordings are not
silently converted into provider editions; a provider association is a separate,
future reviewed workflow.

## API

The API exposes `GET /api/v1/localrecording?fileIds=1&fileIds=2` for a preview
and `POST /api/v1/localrecording` to create it. The request accepts `fileIds`,
`authorId`, `title`, `subtitle`, `narrators` (an array), `publisher`, `language`,
nullable `year` and optional `chapterMetadata`. For a new local author, use
`authorId: 0` with `newAuthorName` and `newAuthorPath`. Duration is read from the files. The response is a book
resource. Only unmapped existing audio files are accepted; validation is repeated
at creation, and a concurrent mapping change rolls back the entire operation.

`GET /api/v1/localrecording/{bookId}` reads the stored metadata, immutable
identity, edit revision, ordered file IDs and provenance. Send the editable
metadata and current `revision` to `PUT /api/v1/localrecording/{bookId}`. A stale
revision returns HTTP 409. The update returns the refreshed book resource.

An audio file without an ASIN is not automatically attached to an unconfirmed
catalog recording, even when its title and author match. Confirm its edition
through manual import or create a local recording. Existing confirmed local
recordings retain their identity during rescans.
