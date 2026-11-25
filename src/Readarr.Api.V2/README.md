# Bookshelf API v2

Bookshelf's v2 is an intentional, partial new contract for the Activity Queue.
The existing v1 API remains available, including all per-book queue reads,
numeric IDs, legacy action routes, and SignalR events. The application
discovery endpoint still reports v1 as the current general-purpose API, with
v1 and v2 listed as available.

Any configured URL base precedes the paths below. Both versions use the
existing API-key authentication. Retrieve the generated document from
`/docs/v2/openapi.json` using an API key; its server URL reflects the actual
listener's base path.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/v2/queue` | Filtered, paged downloads; one row per physical job |
| GET | `/api/v2/queue/status` | Counts and warnings per download, including pending releases |
| POST | `/api/v2/queue/remove` | Explicitly selected removal, category, ignore and optional blocklist/retry |
| POST | `/api/v2/queue/grab` | Grab selected pending releases once |

A tracked download's ID is the exact client ID plus download identifier
(`download:<client-id>:<download-id>`). A pending release uses its own
persisted release ID (`pending:<id>`). Entries without an unambiguous
identity remain visible but cannot authorize an action.

Each row contains a `recordings` array of exact library book IDs and
the currently resolvable recording identities. Coverage provenance is
explicit: recorded grab, recorded pending release, mapped candidates, or
unknown. A mapped candidate is not evidence that the recording was grabbed.
Missing library metadata does not silently substitute another recording.
Size, progress and remaining time belong to the download, not each of its
books.

Action requests contain a `targets` array of the returned `id` and
`revision`, not a numeric v1 ID or an independently supplied client or
recording. In a bulk request all targets are resolved and checked before
performing client operations. Duplicate/malformed targets are rejected with
HTTP 400; a stale or unavailable selection returns HTTP 409. Successful
requests return HTTP 204. Ordinary progress alone does not invalidate a
selection. The revision changes with action-relevant identity, coverage and
client state. Capabilities on each row determine which operations are
available. Retries request the recorded book IDs, not an inferred sibling or
the author's entire catalog. Pending releases have no client download to
remove; their blocklist operation does not initiate an automatic retry.

These checks do not promise atomic transactions across external clients with
independent failures. After an error, read the queue again and review it
before attempting another operation.

## Regenerate the references

Build a complete native Linux Bookshelf runtime, then run:

```sh
bash docs.sh /path/to/built/Readarr
```

The script starts an isolated, disposable home using that package, checks the
actual versioned routes and authentication, and replaces both committed
OpenAPI references from the listener. The static documents use an
origin-relative `/` server path; served documents advertise the configured
base path. The `apidocuments` local validation check compares the committed
references with a fresh instance of the package.
