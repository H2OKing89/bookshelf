# Bookshelf

An audiobook collection manager based on Readarr. Bookshelf searches the Audible
catalog through [Audnexus](https://audnex.us/), stores recording metadata, and
uses Usenet and BitTorrent indexers to find monitored audiobooks. Goodreads and
Hardcover are not used for catalog search or refresh.

Each recording has a stable identity consisting of its Audible marketplace and
ASIN. Alternate narrators, languages, and rerecordings stay separate. The
calendar uses the recording's release date. Metadata includes narrators,
runtime, descriptions, covers, publisher, language, ratings, and series.
Library scans recognize audio files; new installations get an Audiobooks
quality profile.

Book naming supports normalized titles, primary series, padded positions,
narrators, and ASINs, with two optional audiobook presets. See
[Audiobook file naming](docs/audiobook-naming.md) for formats and examples.

## Run locally

The server listens on port 8787. Build the frontend with `yarn build` and the
server with `dotnet build src/NzbDrone.Console/Readarr.Console.csproj`. Serve the
built UI from the server output's `UI` directory, then run the generated Readarr
executable with `-nobrowser -data=/path/to/config`.

For reverse proxy deployments, configure the
[trusted proxy addresses](docs/trusted-proxies.md) before upgrading. Changes to
this setting require a server restart.

## Catalog settings

Under **Settings → Metadata → Audiobook Catalog**, select an Audible marketplace
and use `https://api.audnex.us` or a self-hosted Audnexus API. No Audible account
or API key is needed for Audnexus. Search by title, author, or `asin:B08G9PRS1K`.
Audnexus searches return up to ten keyword matches and are not complete author
bibliographies. Existing recordings are retained when absent from results.

For paginated author release discovery, enable **Discover Author Releases** in
the **audible-rs** section. Configure:

- The agent's HTTPS base URL (HTTP is supported on localhost).
- An absolute server-side file path containing an audible-rs token with the
  `api` scope. The token itself is not stored in Bookshelf settings.
- An optional agent account name. The selected catalog marketplace is also sent
  to the agent; it must be configured for that account.

The agent must have an available account session for unattended polling.
Bookshelf issues catalog GET requests through `/v1/api/request`; it does not
invoke downloads, change owned content, or manage account locks. Author refresh
reads pages starting at zero and rejects missing/repeated pages and catalogs
larger than 1,000 recordings instead of accepting a partial snapshot. Catalog
omissions never automatically delete saved audiobook records.

Book and author refresh use the existing scheduler and monitoring controls.
Choose **All** or **New** for an author's new items to monitor discovered
recordings. Without audible-rs, discovery remains limited to Audnexus keyword
results. Catalog availability, preorder coverage, and metadata freshness depend
on the upstream marketplace.

## Metadata normalization

Published titles remain intact for matching and reference. The API adds
`displayTitle` for consistent series labels, such as `The Primal Hunter #4`.
Labels are derived only when the published title agrees with the recording's
series name and position. Distinctive titles, conflicting numbers, and volume
ranges retain their published titles. The UI shows the published title on the
book details page and in title-link tooltips.

Subtitles are stored separately. Names and genres receive Unicode/whitespace
normalization and case-insensitive deduplication; publisher comma spacing and
language codes are standardized. A sparse response for the same ASIN and
marketplace retains richer stored metadata, including genres and subtitles.
Recording identity and published titles are never inferred from these labels.

An Audible marketplace does not select a recording language. Each author's
metadata profile controls allowed languages; set it to `eng` for English-only
discovery. Disallowed languages are skipped before detail and chapter enrichment.
Existing file-backed recordings remain available for review. Verified English
author credits and established Latin display names take priority over native
profile spellings for the same author identity; native names remain aliases.

Audio identification reads embedded `ASIN` and `AUDIBLE_ASIN` tags, bracketed
filename ASINs, and the nearest identified recording folder, including disc
subfolders, even when title and author tags are complete. An exact recording and marketplace take
priority over fuzzy album/title matching. Conflicting identifiers require review,
and a missing exact ASIN cannot silently select a different recording.
For a bare ASIN, lookup tries the preferred marketplace first, then other supported
marketplaces after confirmed missing results. A successful lookup retains its
verified marketplace; provider failures stop discovery rather than imply absence.
Use `audible:uk:B0F14RFHS6` to require an explicit marketplace. Selecting a book or
edition also fixes its recording identity; selecting an author does not establish
the recording's marketplace. Regional recordings can share an established owning
author only when their explicit contributor ASIN matches a unique saved owner.
Matching author names alone never establish that relationship.

Series tables keep each recording as its own row and show its ASIN and catalog
marketplace. Numbered audiobook series distinguish volume counts from recording
counts and report how many volumes have files. "Missing recording" applies to
that row's ASIN; another recording of the same volume can already have a file.
The marketplace label identifies the catalog used for the lookup.

Adding or importing a verified ASIN can create its missing author directly from
the recording's catalog credit and the selected library settings. It does not
require a separate author profile or a complete bibliography lookup before the
recording can be added. Reviewed author ID and spelling corrections are saved in
Metadata Overrides; they preserve each recording's ASIN and marketplace.

Automatic and forced audio retagging check the file's current recording identifiers
before changing or scrubbing tags. A conflict or a different selected recording
prevents the write and excludes the file from retag previews. Monitoring and
metadata edits preserve native audiobook file links.

Recording refresh also enriches complete-looking catalog entries with Audnexus
details and chapters. Successful lookups and missing records are cached for a
day; transient enrichment failures are retried after a minute. Provider requests
keep the existing timeout, throttling and cooldown controls. Chapters are fetched
for recording details and author refresh, rather than each keyword search result.
The configured Audnexus URL is used for every request, including chapters. A
self-hosted instance can remove reliance on the public endpoint, but requires
separate deployment and maintenance.

The importer retains all author, narrator and recognized contributor roles on
the recording, plus external contributor IDs when supplied. The library still
has one owning author per book; refreshing reordered coauthor credits preserves
that owner. Translator and editor credits cannot become the owning author merely
by appearing first. Both provider series links are retained and detail metadata
can correct the primary series. Series are matched by provider identity rather
than similar names.

Full publisher summaries are preferred to catalog extracts, with HTML and
standalone retailer calls to action removed. Typed genre and tag IDs are retained
alongside the existing flat genre list. Copyright year, adult-content status,
literature type, author genres, similar-author references, and chapter timings
are stored when supplied. Unknown values remain unknown. Book details show
recording credits, classification and an expandable chapter list; the API exposes
chapter metadata on edition resources and `GET /api/v1/book/{id}/chapters`.
Chapter lists are excluded from book-list responses.

Catalog requests ask for cover sizes up to 2400 pixels and choose the largest
returned variant. Refreshes preserve larger variants of the same cover asset;
they do not manufacture URLs or upscale a small image. Missing upstream images,
biographies, credits or chapters cannot be recovered by this importer alone.

Book and edition `releaseDate` values are calendar dates serialized as
`YYYY-MM-DD`. Legacy ISO timestamp input is still accepted. Storage uses
midnight UTC as a convention; calendars and feeds do not convert the release
day between timezones. Refresh existing authors to repair previously stored
metadata and dates.

The observed native Audible placeholder date `2050-12-30` is retained separately
as `sourceReleaseDate` and flagged with `releaseDateIsPlaceholder`, while the
calendar release date remains unknown. Other future dates remain valid. The
metadata migration normalizes existing native English language codes and this
specific placeholder; other enrichment arrives through normal refreshes. Back
up the database before upgrading. Older binaries do not understand the new
columns, so rolling back a deployed upgrade requires restoring its backup.

Run .NET with ICU/globalization support enabled. Invariant globalization mode
disables the Unicode normalization used for metadata names.

## Existing libraries and lists

This is an audiobook catalog, not an automatic Goodreads database migration.
Existing records and files are retained, but legacy numeric catalog IDs cannot
be refreshed through the new providers. Add recordings through audiobook search
or import an audio library to obtain native IDs. Existing custom quality
profiles remain available; choose an audio profile for audiobook roots.

Goodreads list integrations are hidden and inactive. Other list sources are
matched against audiobook titles and authors, and ambiguous recording matches
are skipped. Legacy API field names used by import lists remain for wire
compatibility; they do not trigger Goodreads metadata requests.

Changing an audiobook's monitoring or metadata does not reassign its files.
Conflicting recording IDs require review instead of an automatic title match.
For libraries with curated filenames, Settings → Metadata → Audio File Metadata
provides **Prefer filename ASIN** (off by default). This uses a bracketed ASIN
from the filename or nearest identified recording folder over embedded ASINs.
Filename and folder IDs must agree; files without path IDs keep the strict tag
checks. The selected recording must still match that exact ASIN. This preference
does not change media tags or bypass the stricter retagging checks.
API clients adding verified recordings can use `POST /api/v1/book?doRefresh=false`
to skip the queued author refresh; recording metadata is still fetched and
validated synchronously. The default remains `doRefresh=true`.

## Support

File an issue or start a discussion in the repository.

## Development roadmap

The [Sonarr v5 modernization roadmap](docs/sonarr-v5-modernization-roadmap.md)
compares this fork with a pinned Sonarr v5 source revision. Its
[porting backlog](docs/sonarr-v5-porting-backlog.md) tracks reusable platform and
UI improvements, book-specific adaptations, dependencies and validation gates.

## License

This derivative of [Readarr](https://github.com/Readarr/Readarr) and
[Prowlarr](https://github.com/Prowlarr/Prowlarr) is licensed under
[GPLv3](LICENSE.md).
