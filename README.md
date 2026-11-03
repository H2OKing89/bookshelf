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

## Run locally

The server listens on port 8787. Build the frontend with `yarn build` and the
server with `dotnet build src/NzbDrone.Console/Readarr.Console.csproj`. Serve the
built UI from the server output's `UI` directory, then run the generated Readarr
executable with `-nobrowser -data=/path/to/config`.

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

Book and edition `releaseDate` values are calendar dates serialized as
`YYYY-MM-DD`. Legacy ISO timestamp input is still accepted. Storage uses
midnight UTC as a convention; calendars and feeds do not convert the release
day between timezones. Refresh existing authors to repair previously stored
metadata and dates.

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

## Support

File an issue or start a discussion in the repository.

## License

This derivative of [Readarr](https://github.com/Readarr/Readarr) and
[Prowlarr](https://github.com/Prowlarr/Prowlarr) is licensed under
[GPLv3](LICENSE.md).
