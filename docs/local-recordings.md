# Local audiobook recordings

For recordings distributed outside Audible, open **Unmapped**, select the files
for one recording, and choose **Create local recording**. Review the title,
author, narrators, publisher, language, and year before creating it. The author
must already exist in your library.

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

The API exposes `GET /api/v1/localrecording?fileIds=1&fileIds=2` for a preview
and `POST /api/v1/localrecording` to create it. The request accepts `fileIds`,
`authorId`, `title`, `subtitle`, `narrators` (an array), `publisher`, `language`,
and nullable `year`. Duration is read from the files. The response is a book
resource. Only unmapped existing audio files are accepted; validation is repeated
at creation, and a concurrent mapping change rolls back the entire operation.

An audio file without an ASIN is not automatically attached to an unconfirmed
catalog recording, even when its title and author match. Confirm its edition
through manual import or create a local recording. Existing confirmed local
recordings retain their identity during rescans.
