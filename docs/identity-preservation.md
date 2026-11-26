# Audiobook preservation contract

Every importer or metadata port must preserve the distinction between a work, a
recording and its files. Recording identity includes the full marketplace and
ASIN, or a persistent local recording ID for a non-Audible release. Similar titles,
author names, narrators or series positions cannot authorize an identity change.

## Replacement and ownership

Bookshelf retains its staged replacement protocol. Prepare and tag independent
copies, publish the complete batch while retaining recoverable old files, commit
database rows, then publish import history/events and retire authorized old files.
Stage, tag, publication or database errors must preserve the original library and
sources. A partial import cannot erase unrepresented recording parts.

An upgrade's retirement set belongs to the incoming edition. Other editions,
regional releases and narrations survive, even when attached to the same book.
An unknown edition grants no old-file retirement authority; conflicting known
edition/book identities fail. A path shared with another stored file ID cannot be
overwritten or retired. Part coverage and duration consolidation are evaluated
within each recording, so another recording's runtime cannot cover a missing part.

Calibre deletion removes a whole Calibre record, including its formats. Before
retiring that record, Bookshelf checks files in the configured Calibre root.
Any retained file referencing the same Calibre ID prevents deletion, whether it
belongs to another part, edition or author. An unavailable ownership inventory
retains the record. Author deletion likewise uses file ownership, not an entire
author directory.

This differs from the pinned Sonarr v5
[`UpgradeMediaFileService`](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs#L37),
which retires old episode files before moving their replacements. The v5 source
is a comparison reference; Bookshelf keeps its existing staged protocol.

## Public synthetic recipe

[`audiobook-identities-v1.json`](../scripts/validation/fixtures/audiobook-identities-v1.json)
is a checked-in recipe for generating a legacy-home snapshot. It contains no
private database, media, credentials or provider responses. A genuine older
package creates the schema and salted account through its application services;
the fixture then seeds only that new, stopped home through SQLite.

The recipe includes:

- US and UK identities sharing a synthetic ASIN but retaining different regions.
- A separate narration of the same title and an independent local recording.
- Coauthor credits and a saved canonical author/alternate-ASIN mapping.
- A publisher-pack recording, a fractional series position and an unknown date.
- Multipart file IDs, edition ownership, original paths and acquisition history.
- Explicit `false`, zero, null dates and chapter metadata.

The snapshot preserves original baseline columns exactly while allowing additive
migrations. It covers authors, books, editions, files, history, accounts, series,
series links, saved identity overrides and selected configuration values. Owned
source/library bytes are hashed independently. Fixed synthetic IDs make failures
reproducible; paths and credentials are generated inside a fresh private home.
The recipe records a pack's existing range position. Work memberships start
empty in the older package; the separate `recordinglifecycle` proof creates
reviewed work relationships after migration rather than inferring the range.

## Publisher-pack work coverage

On an audio recording's **Work coverage** tab, choose the exact edition and
select **Edit work coverage**. Add a new work with its title, author, series and
volume/position label, or link a work already reviewed for another recording.
Mark each link confirmed or uncertain. Fractional positions and side stories
can be described without expanding a text range into unverified volumes.

A pack and each standalone or regional recording keep their own recording IDs
and files. The tab shows direct file ownership separately from related confirmed
work coverage. A related file is counted only once even if its recording covers
several works. Having an associated file is not proof of complete audio parts;
acquisition, monitoring and removal still operate on the exact recording.
Changing or removing a membership changes only that relationship, not the
linked work's other recordings or any media. A revision mismatch requires
reopening coverage before saving.

The API uses `GET /api/v1/recordingwork/book/{bookId}` to choose an edition,
`GET /api/v1/recordingwork/works?term=...` to find reviewed works, and
`GET`/`PUT /api/v1/recordingwork/{editionId}` to read and replace that exact
edition's reviewed membership set. A replacement supplies its revision and
memberships, each with an explicit confirmation value and either an existing
`workId` or a `newWork` description. See the [API reference](../src/Readarr.Api.V1/openapi.json).

## Hosted upgrade, API and browser gate

Run the explicit gate with a complete current package, a distinct genuine older
package supporting the audiobook schema, and a local Playwright installation:

```sh
python3 scripts/validate-local.py \
  --runtime /path/to/current/package \
  --baseline-runtime /path/to/genuine/older/package \
  --playwright-module /path/to/node_modules/playwright \
  --checks identity \
  --output _temp/new-identity-proof
```

The output directory must be new. The harness accepts package paths, never an
existing application home. It starts only owned loopback servers and browser
contexts, blocks external browser requests, and preserves input package hashes.
Playwright and its browser must already be installed; the check does not install
dependencies or contact a metadata provider or download client.

The baseline application creates an original backup. The candidate opens that
same synthetic home; authenticated APIs verify exact recording and file IDs,
credits, chapter data and saved preferences. Real Forms login and detail-page
navigation run at desktop and 390px phone width. API requests use the configured
API key; a Forms cookie alone must not bypass the API-key boundary. Browser
credentials and keys travel through stdin, never command arguments or environment
variables. Browser processes run in an owned group cleaned up before its leader
is reaped, including on timeout.

After a candidate restart, the fixture changes monitoring through the API and
restores the application's original baseline backup. Exact data, credentials and
media must return. This is restoration of baseline data by the candidate, not a
promise that an older binary can open a newer schema. The source tests below
separately exercise import failure and filesystem/database rollback.

## Source preservation matrix

Run these fixtures with the repository's usual offline Core filter and owned
PostgreSQL socket where the full suite requires one:

| Contract | Production boundary and fixtures |
| --- | --- |
| Stage/tag/publication/database rollback; unrepresented parts | `UpgradeMediaFileServiceFixture`, `ImportApprovedTracksFixture` |
| Edition-scoped replacement, shared paths, per-recording duration, shared Calibre records | `UpgradeMediaFileServiceFixture` |
| Foreign file ownership and shared author directories | `MediaFileDeletionService.DeleteAuthorFilesFixture` |
| Local recording stays local; regional recording and established author survive refresh | `AudiobookRefreshFixture` |
| ASIN/marketplace matching and recording parts | `AudioRecordingIdentifierFixture`, `IdentificationServiceRecordingFixture` |
| Persistent local identities | `LocalRecordingRepositoryFixture`, `LocalRecordingServiceFixture` |
| Canonical author identities and observed aliases | `AudiobookAuthorIdentityFixture`, `AudiobookMetadataOverrideFixture` |
| Native metadata, unknown dates, contributor/chapter persistence | `AudiobookMetadataPersistenceFixture`, `AudiobookCalendarDateFixture`, `AudiobookRichMetadataFixture` |
| Completion and cleanup after committed identity/part proof | `DownloadImportCompletionServiceFixture`, `DownloadCompletionCleanupFixture` |

The hosted fixture verifies persistence, upgrade, API and browser projection. It
does not pretend to perform a real metadata-provider refresh: the source fixtures
exercise refresh services with controlled providers, including cancellation and
missing/regional responses. No test requires the user's private library.


## Rejection codes and upgrade preference

Download and import decisions carry stable string `code` identifiers alongside
readable `reason` and the existing temporary/permanent `type`. Changing or
localizing an explanation does not classify a rejection. Unknown future strings
round-trip unchanged; legacy unclassified reasons use `unknown`.

API v1 release results retain their `rejections` string array and add
`rejectionDetails` objects. Manual import retains its rejection objects with an
additive `code`; queue status messages retain `messages` and add `rejections`.
Codes survive incomplete-import history and restart. This is an additive wire
change; clients can continue showing readable explanations. Codes themselves do
not authorize removal, blocklisting or redownload actions.

Automatic imports compare custom-format preference only within the selected
recording. At the same quality, every incoming part must meet every existing
part's score; additional low-scoring parts cannot average away a downgrade.
Higher quality retains the existing quality-first ordering, while a same-quality
proper/repack revision does not bypass custom-format protection. Existing-file
reidentification does not replace media and skips this replacement check.

The guard previews the intended filename through the real filename builder and
the same acquisition metadata used to publish the file. Retained scene names,
release groups and indexer flags keep their meaning after a rename. A filename
rule that loses a preferred token reports `custom_format_lost_after_rename`; an
ordinary lower score reports `custom_format_downgrade`. Existing files are scored
from their own metadata, without borrowing another recording's provenance.

Manual preview exposes the same explanation. An explicitly submitted manual
import retains its existing override behavior; it still obeys the staged
ownership, database and source-retention boundaries. The guard does not add a
silent automatic override.

## Upgrading an existing name-only author identity

An author originally discovered without an ASIN can later acquire an explicit
catalog identity. A name search alone is not sufficient to promote the record.
The author catalog must return an exact previously stored native recording in
its original marketplace, whose saved edition confirms the recording ID and
whose primary author credit matches the existing name and supplies an ASIN.
Matching explicit credits must agree across the observed owned recordings and
stored credits. An ambiguous name, contradictory author ASIN, local recording,
wrong-market edition, missing evidence or existing canonical owner does not
authorize a promotion.

Catalog and individual recording lookups never persist the proposed upgrade.
A real author refresh rechecks the incumbent author/metadata IDs, supporting
recording and destination uniqueness across supported marketplaces in a database
transaction, then updates the existing metadata row. Numeric IDs, route slug,
local author preferences, folder, recording/file identities and history remain
intact. A change to the author identity alone does not force a tag rewrite.
Existing canonical or duplicate authors still require the reviewed **Reconcile
Author** workflow; promotion never merges them by name. Subsequent refreshes
reuse the canonical author and retain the original route slug.
