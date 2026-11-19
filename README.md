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

Use the .NET 10 SDK selected by [global.json](global.json) (10.0.404 baseline).
The application targets .NET 10, with runtime 10.0.15 bundled in self-contained
release packages. See the [dependency audit](docs/dependency-audit.md) for
package updates, native requirements and remaining release gates.

The server listens on port 8787. Build the frontend with `yarn build` and the
server with `dotnet build src/NzbDrone.Console/Readarr.Console.csproj`. Serve the
built UI from the server output's `UI` directory, then run the generated Readarr
executable with `-nobrowser -data=/path/to/config`.

Run the [local package validation](docs/local-validation.md) checks against a
complete package to verify startup, backup/restore, upgrades and media handling
with fresh synthetic data. GitHub Actions is not required.

For reverse proxy deployments, configure the
[trusted proxy addresses](docs/trusted-proxies.md) before upgrading. Changes to
this setting require a server restart.

## Accounts and password recovery

Upgrades retain existing Forms, Basic and None authentication settings, API keys
and saved accounts. Accounts share administration of the same library; Bookshelf
does not provide roles or separate per-account libraries.

Under **Settings → General → Security**, an authenticated Forms or Basic account
edits its own username and password. API-key-only access to a home with multiple
saved accounts shows **Account to edit**; select one before changing credentials
or the authentication method. Other accounts are retained. General settings can
still be saved without a selection when credentials and the authentication method
are unchanged. A home with one account keeps its existing editing behavior.

To recover a password, stop the server and run the packaged executable against
its existing configuration home:

```sh
Readarr -passwordreset -data=/absolute/existing/home
```

Use the same database configuration and environment as the server, including any
PostgreSQL settings. Enter the existing username or `id:GUID` account identifier,
then enter and confirm a new nonblank password. Input is hidden in an interactive
terminal. The utility allows one attempt within a total two-minute prompt window;
it reports a generic failure and exits if selection or confirmation fails.

Recovery requires an existing home with `config.xml` and a database schema
compatible with this binary. It does not create an account, initialize a home or
run database migrations. It changes only the selected account's password and
preserves its identity, username, authentication mode, API key and library. Start
the server again and sign in with the new password. Recovery uses no reset token
or HTTP reset endpoint.

Forms sessions are checked against the saved credentials. A password change or
recovery invalidates that account's previous sessions; the first upgrade to this
check also requires existing Forms sessions to sign in again once. API keys are
unchanged.

## Database log storage

Database logging is enabled by default. To stop storing diagnostic events in the
log database, stop Bookshelf, add this setting inside your existing `config.xml`
`<Config>` element, then restart:

```xml
<LogDbEnabled>False</LogDbEnabled>
```

Alternatively, set `Readarr__Log__DbEnabled=false` in the server's environment
before starting it. The environment value takes precedence over XML. Set the
effective value to true, or remove both overrides, and restart to enable database
logging again.

Disabled mode leaves the existing log database untouched. The Events list is
empty, while file/console logging, audiobook library/download history and the
metadata cache remain available. Application update-install history also lives
in the log database; that history and the corresponding update-installed
notifications pause while this mode is disabled. Reenabling reuses the existing
database; normal log retention still applies. PostgreSQL still needs the main
and cache databases; an unused log database is not opened or created.

This setting controls log storage. The existing `LogSql` setting independently
controls diagnostic SQL-statement tracing.

## PostgreSQL connection strings

Existing PostgreSQL host/user/password/port/database settings continue to work.
You can instead supply complete Npgsql connection strings through XML or the
server environment:

| Database | XML setting | Environment variable |
| --- | --- | --- |
| Main library | `PostgresMainDbConnectionString` | `Readarr__Postgres__MainDbConnectionString` |
| Metadata cache | `PostgresCacheDbConnectionString` | `Readarr__Postgres__CacheDbConnectionString` |
| Diagnostic logs | `PostgresLogDbConnectionString` | `Readarr__Postgres__LogDbConnectionString` |

Set main and cache strings together. Also set the log string when database
logging is enabled, which is the default. With `LogDbEnabled=false`, the log
string is ignored, even if it remains in the configuration. A partial active
set is rejected before opening a database; it never falls back to SQLite.
Without active strings, existing SQLite/component selection stays unchanged.

Remove the individual `PostgresHost`, `PostgresPort`, `PostgresUser`,
`PostgresPassword`, `PostgresMainDb` and `PostgresCacheDb` settings when switching
to strings, including environment overrides and explicitly saved default/empty
values. Remove `PostgresLogDb` too when logging is enabled. Mixing the two forms
is rejected. Point the strings at the existing databases to retain your library,
cache and logs, then restart Bookshelf.

Every active string must explicitly name its `Host` and `Database`. For example,
a main string can contain:

```text
Host=postgres.example;Port=5432;Database=bookshelf-main;Username=bookshelf;Password="example;with ""quotes""";SSL Mode=VerifyFull;Root Certificate=/config/postgres-ca.crt
```

Use the corresponding database names for cache and logs. Npgsql quoting handles
semicolon/quote characters in values; XML settings additionally need normal XML
escaping. TLS and other provider options are retained, with `Enlist=false` forced
for Bookshelf's database operations. See the official
[Npgsql parameters](https://www.npgsql.org/doc/connection-string-parameters.html)
and [TLS settings](https://www.npgsql.org/doc/security.html) for supported options;
`VerifyFull` validates the server certificate and hostname.

Environment strings override the matching XML strings and are not copied into
`config.xml` by default generation or unrelated settings saves. Strings are not
returned by the host-settings API. XML strings remain part of the existing
configuration backup contract. Connection/configuration failures use generic
messages and safe SQLSTATE codes, without raw provider exceptions or credentials.

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
