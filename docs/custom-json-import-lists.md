# Custom JSON import lists

Bookshelf can read a user-managed HTTP(S) endpoint through its **Custom JSON**
import list. This is a Bookshelf-native list contract, not a Sonarr or legacy
Readarr feed. The endpoint controls list membership; Bookshelf continues to
control catalog lookup, exclusions, root folders, quality profiles, monitoring
and other import-list settings.

## Configure a list

1. Open **Settings → Import Lists**, choose **Add** and select **Custom JSON**.
2. Enter the full URL of your endpoint. Configure a root folder, profiles and
   monitoring options as for other lists.
3. For a private endpoint, enter its optional bearer token separately; prefer
   HTTPS for endpoints carrying credentials. Bookshelf sends the token only in
   an `Authorization: Bearer …` header. Do not include a credential, query
   parameter or fragment in the endpoint URL.
4. Select **Test** to check the complete contract, then save the list. The
   automatic sync schedule has a six-hour minimum refresh interval.

The host and path in the saved URL are the only destination the provider uses.
Bookshelf does not follow redirects or provider-supplied next-page URLs, and
does not reuse or store cookies for this provider. Its HTTP diagnostics redact
the request URL, redirect location and response bodies.

## Version 1 contract

Bookshelf requests the configured endpoint with `GET`, `Accept: application/json`,
and the query parameters `page` (starting at 1) and `pageSize` (100). Every
successful response must be HTTP 200, have a JSON content type and contain a
UTF-8 JSON object such as:

```json
{
  "schemaVersion": 1,
  "snapshotId": "example-revision-1",
  "page": 1,
  "pageSize": 100,
  "totalItems": 2,
  "complete": true,
  "items": [
    {
      "kind": "author",
      "authorId": "audible:us:B000000001",
      "author": "Example Author"
    },
    {
      "kind": "recording",
      "authorId": "audible:us:B000000001",
      "author": "Example Author",
      "title": "Example Recording",
      "recordingId": "audible:uk:B000000002",
      "editionId": "audible:uk:B000000002"
    }
  ]
}
```

All fields shown in the page are required. `snapshotId` is your endpoint's
opaque, nonempty revision identifier; assign a new identifier when the result
set changes and keep the same identifier and `totalItems` across all pages of
one revision. Use deterministic ordering so records do not move between pages.
The `complete` boolean must be true for each page; return a failure status or
`complete: false` when the source cannot supply the complete page.

`totalItems` counts the top-level entries in the entire list, not only the
current page. A non-final page must have exactly 100 entries; the final page
must have the remaining entries. A legitimate empty list supplies page 1 with
`totalItems: 0`, `complete: true` and `items: []`. A missing, null, truncated, or
otherwise inconsistent result is not considered a successful empty list.

### Item kinds

Each entry has a qualified `authorId` and an author label. Names and titles are
display labels, never substitutes for exact identities.

| `kind` | Other required fields | Meaning |
| --- | --- | --- |
| `author` | None | One exact author. Existing list monitor/add rules apply. |
| `recording` | `title`, `recordingId`, `editionId` | One exact audiobook recording/edition. |
| `work` | `workId`, `workTitle`, `recordings` | A source-local work grouping with one or more explicitly identified recordings. |

A work entry groups recordings, each carrying `title`, `recordingId` and
`editionId`, with the same author as the parent. For example:

```json
{
  "kind": "work",
  "authorId": "audible:us:B000000001",
  "author": "Example Author",
  "workId": "owned-source-work-1",
  "workTitle": "Example Work",
  "recordings": [
    {
      "title": "Example Work (US)",
      "recordingId": "audible:us:B000000002",
      "editionId": "audible:us:B000000002"
    },
    {
      "title": "Example Work (UK)",
      "recordingId": "audible:uk:B000000002",
      "editionId": "audible:uk:B000000002"
    }
  ]
}
```

`workId` is an identifier scoped to your feed, unique within the snapshot. The
provider expands the work's listed recordings into existing import-list
membership rows. It does **not** infer work memberships, select other editions,
create locally reviewed Work records or merge regional recordings. A work row
without explicit recordings is invalid. The same recording can appear in
several work rows; exact identity deduplication prevents duplicate additions.

### Qualified identities

- Use `audible:<marketplace>:<ASIN>`, such as `audible:us:B000000002` or
  `audible:uk:B000000002`. Identical ASIN text in different regions denotes
  distinct identities. Bare ASINs, title lookups, Goodreads/TV IDs and local
  numeric database IDs are not accepted.
- `recordingId` and `editionId` must be the same exact qualified audiobook
  identity, matching Bookshelf's current audiobook catalog model. A recording
  cannot be assigned conflicting author IDs within the snapshot.
- Author IDs can be existing qualified Audible author IDs, including the
  canonical `audible:<marketplace>:name:<escaped-name>` IDs generated by
  Bookshelf when the catalog lacks an author ASIN.
- Existing local IDs have the form `local:author:<32 lowercase hex digits>`
  and `local:recording:<32 lowercase hex digits>`. Copy these from the source
  catalog rather than inventing a local ID. Local IDs remain visible as raw
  unresolved membership evidence; an external list does not auto-create or
  remap a local recording by title.
- Exclusions match exact qualified identities through the same membership
  pipeline as other providers.

### Paging, limits and failure behavior

The version 1 provider accepts at most 1,000 top-level items (ten pages) and
1,000 expanded author/recording rows, with at most 1 MiB of decompressed
response bytes per page and a 30-second request timeout. Snapshot identifiers
are limited to 128 characters; author/title/work labels and work IDs to 512
characters; qualified IDs to 1,024 characters. Empty strings and control
characters are invalid in these metadata fields. Unknown fields, duplicate
JSON properties, unknown item types, unsupported schema versions and conflicting
recording identities are rejected rather than silently interpreted.

If a page is unavailable, contains invalid entries, advertises a changed
snapshot/total or fails any limit, Bookshelf discards the entire fetch. It does
not auto-add partial results or overwrite that list's last successful
membership. A complete empty list replaces only that list's snapshot.
See the saved list's **List membership** disclosure for the last successful
result. The [complete synthetic example](examples/custom-json-list-v1.json)
includes author, recording and work entries; the sample IDs are illustrative,
not a request to add real books.
