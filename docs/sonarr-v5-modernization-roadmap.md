# Bookshelf modernization roadmap using Sonarr v5

Use Sonarr v5 as a donor for the shared application platform, services and UI
patterns. Keep Bookshelf's audiobook identities, metadata providers and import
safety as the governing model. Take small, tested changes with explicit database,
API and saved-setting compatibility decisions.

The comparison produces **87 numbered items**: 26 platform/storage/delivery
items, 30 acquisition/import/notification items, 22 frontend items and nine
book/audiobook adaptations. Some are preservation gates or optional designs,
rather than missing features. The [full porting backlog](sonarr-v5-porting-backlog.md)
contains source links, current local behavior, dependencies, relative effort and
acceptance checks. Its index is the implementation tracker.

A [focused release group comparison](sonarr-v5-release-group-comparison.md)
dated **2026-11-15** adds five separately tracked follow-up slices, RG01-RG05.
Group matching already exists, but parsing, manual-import round trips, stored-file
correction, pack evidence policy and naming have concrete gaps. These supplement
the original 87-item comparison; they do not replace its pinned baseline.

## Scope and pinned sources

Reviewed **2026-11-13, America/Chicago**. The only upstream comparison target is
**Sonarr v5**. This document does not claim that each useful implementation was
first introduced in v5; it establishes what exists in the inspected v5 source.

| Project | Inspected revision | Role |
| --- | --- | --- |
| Bookshelf | `38f48b1716fb51adb648c087c33ebe1fa620438e` | Current audiobook fork and behavior to preserve |
| Sonarr | [`v5.0.0.2941`](https://github.com/Sonarr/Sonarr/releases/tag/v5.0.0.2941), commit [`c018578e4b79ee048575c02d7a8aa2b929e4b837`](https://github.com/Sonarr/Sonarr/commit/c018578e4b79ee048575c02d7a8aa2b929e4b837) | Pinned code donor; GitHub marks this release as a prerelease |

Readarr's own [retirement announcement](https://github.com/Readarr/Readarr/blob/develop/README.md)
identifies unusable metadata and lack of maintenance as central problems. The
current Bookshelf README describes an audiobook-specific replacement catalog;
Sonarr's TV catalog cannot supply the missing book metadata.

The [comparison baseline](sonarr-v5-comparison-baseline.json) records the revisions,
file inventory and scope. A normalized-path inventory found 1,739 matching source
or frontend paths, including 1,035 differing text files. This is a useful review
surface, not a Git ancestry analysis or a count of safe cherry-picks. The count
normalizes Readarr/Sonarr names; book/episode-specific paths often do not match.

This was source inspection, repository metadata verification and planning. No
application implementation, database, media, credentials or running server was
changed for this comparison. Builds, dependency vulnerability scans, benchmarks
and upgrade experiments belong to the implementation gates below; they were not
performed by this research.

Those statements describe the **2026-11-13 comparison snapshot**. Subsequent
implementation and validation are recorded by date below and in the backlog;
they do not replace the pinned source evidence or comparison baseline.

## What the comparison shows

| Area | Current Bookshelf | Inspected Sonarr v5 | Recommendation |
| --- | --- | --- | --- |
| Runtime | `net6.0`; CI and packaging still use .NET 6 paths | `net10.0`, SDK `10.0.403` | Migrate the runtime, native dependencies, build and artifacts coherently: P01/P09/P12/P21/P22 |
| Password storage | Unsalted SHA-256 comparison | Salted PBKDF2-HMAC-SHA512, stored work factor, legacy upgrade and fixed-time comparison | Adapt the migration behavior with existing-password compatibility: P02 |
| Proxy/auth boundaries | Forwarded-header known-network/proxy lists are cleared | Explicit trusted proxy handling and local-auth safeguards | Port as a tested unit: P03/P04/P08 |
| Database writes | Ordinary update helpers can suppress SQL errors; import-specific transactions are stronger | Failure-propagating execution and transactional bulk updates | Fix the ordinary repository path while preserving staged imports: P26/D01 |
| Restore | Existing backup/restore support, with weaker payload checks | Validates database/XML payloads before replacement | Adapt restore validation and failure cleanup: P07 |
| Queue and downloads | Per-book queue rows and older tracking/client selection | Per-download multi-item rows, richer rejection/state handling and failover | Adapt to complete recordings and parts: D02-D12/D18 |
| Quality | Custom formats, groups, cutoffs and scores already exist; quality-/duration-based size enforcement is disabled, while the global indexer maximum already applies | More upgrade thresholds, profile size preferences and specifications | Extend existing policy with audiobook evidence: D05/D13-D16/B04 |
| Frontend | React 17, Redux, router 5 and Webpack; mixed JS/TS | React 18, TanStack Query, Zustand, router 7, Vite and predominantly TypeScript | Migrate one resource or primitive at a time: U01-U22 |
| Import safety | Staged independent copies, transactional associations and rollback | Retires old files before transferring the replacement | Retain Bookshelf's design: D01 |
| Product model | Authors, books, recordings, local IDs, narration, ASINs and multipart audio | Series, episodes, season packs and video media | Adapt concepts only; preserve book-native ownership and identity: B01-B09 |

The runtime choice should be .NET 10 LTS, matching the inspected donor, with a
current supported patch at implementation time. Microsoft's [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
lists .NET 6 as out of support since November 12, 2024 and .NET 10 as supported.
Installing a newer SDK alone does not update a self-contained `net6.0` runtime.

## Preserve these Bookshelf contracts

D01 and B01 are gates for every port, including a UI-only change that can trigger
a mutation.

1. **Recording identity:** the full Audible marketplace and ASIN identify an
   Audible recording. Local recordings retain their own immutable local IDs.
   A similar title, author or series position cannot authorize reassignment.
2. **Work versus performance:** UK/US editions, different narrators, full casts,
   rerecordings and publisher packs can cover the same work while remaining
   separate recordings. Display grouping is not a bulk-edit or deletion identity.
3. **File ownership:** deletion follows owned file records, not an author-folder
   name. Retain the protections for shared folders and shared author catalogs.
4. **Import commit:** old media and download sources remain recoverable until the
   complete replacement and its database associations succeed. Multipart parts,
   file IDs, acquisition history and provenance survive re-identification.
5. **Metadata authority:** marketplace is not language. Unknown dates and media
   properties remain unknown. Sparse or failed provider responses do not erase
   richer data; missing catalog results do not imply permission to delete books.
6. **User choices:** preserve settings, profiles, monitoring, credentials, API
   keys, explicit false/zero values, ordering and saved layouts. Password and
   schema upgrades need tested compatibility paths.
7. **Measured audio:** retain bundled MediaInfo, its source/version information,
   all relevant streams and TagLib fallback. Do not replace audio facts with TV
   resolution/source/HDR rules or unverified release-name claims.

Bookshelf already has PostgreSQL, SQLite backup, custom formats, profile cutoffs,
delay profiles, failed/completed download handling, import lists, notifications,
virtualized tables, saved filters, pending-change prompts, metadata overrides and
local recordings. The backlog names specific gaps in those systems rather than
proposing them as entirely new features.

## Recommended execution order

Phases are release gates, not deadlines or a requirement to finish every optional
item. Small accessibility and parser changes can proceed alongside the platform
work when their prerequisites are met. Do not combine runtime, router, state,
build-tool and book-model migrations in one change.

### Phase 0: Reproducible baseline and porting contract

- [x] Capture the current synthetic upgrade, identity and failure fixtures: B01/D01.
- [x] Record runnable build/test commands and a browser harness that does not
  depend on a private live library; extend P21 as the platform migration proceeds.
- [ ] Set the fork's source/artifact/update-channel rules before publishing any
  modernization release: P25.
- [ ] Record the donor commit, paths and adaptation decision for each selected item.

Exit: another developer can reproduce the existing behavior in disposable homes,
and each planned port has a defined source and compatibility boundary.

### Phase 1: Data integrity, authentication and recovery

Start with P26: make ordinary database write failures observable and bulk writes
atomic. Preserve the newer import-only transaction path and add failure-injection
tests before changing repository behavior.

Then take P02/P03/P05/P06/P07/P08, with P04/P24 where applicable: compatible
password migration, proxy trust, live API-key validation, bounded redaction,
restore validation, login throttling and URL-base boundaries. These do not
require a frontend architecture rewrite. P25 remains a release blocker.

Exit: wrong credentials, failed writes, invalid restores and untrusted forwarding
do not produce success or bypass the intended boundary. Existing valid logins,
API keys, reverse proxies and saved homes have explicit migration tests.

### Phase 2: Supported platform and delivery artifacts

Implement P01/P09/P10/P11/P12, then complete P21/P22 with P13 as the selected
SQLite compatibility work. Update project targets, SDK pinning, CI, Docker,
native assets, serialization and migration tooling in coherent slices.

Do not replace Bookshelf's schema with Sonarr migrations. Append the next unused
Bookshelf migration numbers; the 2026-11-13 inspected tree ends at migration 047,
and the first implementation batch adds migration 048.
Preserve the main, log and cache database choices. Optional PostgreSQL strings,
log-DB policy, OIDC, certificates and lifecycle/profiling work are separately
tracked as P14-P19/P23 rather than being implicit requirements of the runtime bump.

Exit: the exact self-contained artifact starts, upgrades a copied synthetic old
home, passes its HTTP tests, packages its native tools and survives restart and
rollback on the declared supported platforms. A new SDK with old packaged runtime
paths is not completion.

### Phase 3: Acquisition, import and quality correctness

Take the smaller concrete fixes first: D03 client-tag isolation, D02 typed
rejections, D04 completion accounting investigation, D05 custom-format downgrade
checks, D10 tracked-download reconciliation and D21 parser deltas.

Then D06-D14/D17/D29 cover file inventory, rejection/failure policy, client
failover, blocked-state recovery, manual-intervention notifications, score
thresholds, language rules, disk checks and qBittorrent seed semantics. D15/D16
need a book-native duration/size policy; B04 adds audio-specific scoring after
those evidence and upgrade rules are sound.

The release group audit adds RG02 (manual-import group preservation and score
preview) and RG01 (selective parser improvements) to this phase. Keep D05/D13 as
the score-enforcement work. RG03 adds stored-file visibility/correction; RG05
covers naming compatibility. RG04's pack-specific group precedence belongs with
B03's book-native pack design. Do not copy Sonarr's parser wholesale: it changes
18 of the 23 existing Bookshelf group-parser fixture expectations.

Exit: completion means the intended recording/parts committed successfully;
failed or ambiguous adds do not cause duplicate downloads; retries and removal
are explicit; scores explain the decision without TV-specific constants.

### Phase 4: Book-native reconciliation and metadata operations

Use B02/B05/B07/B08 for supported author reconciliation, probe revisioning,
provider-health evidence and guided unmapped/conflict review. These convert
recurring operator repairs into reviewable product workflows.

B03 publisher-pack work membership and B06 local-recording/local-author editing
are separate cross-layer designs. B09 statistics should count recordings, works,
files and bytes according to the agreed model, not copy TV categories.

Exit: every operation shows the exact affected identity, revalidates stale state
and preserves the contracts above. Provider failure is distinguishable from a
confirmed missing or conflicting record.

### Phase 5: Frontend modernization in independent slices

Tactical U01-U05 accessibility fixes can begin earlier. Upgrade React with U06
separately from state, router and build tooling; U07 establishes typed boundaries.
Use U08/U09 for one read-only transport/query slice, then U10/U11 for paging and
SignalR cache consistency. Only move U12 local preferences and U13 selection when
their legacy migration is covered.

U14/U15 handle settings and dirty-state protection. U16 router replacement, U19
mobile search cards, U21 virtualization changes and U22 Vite are distinct changes
with their own URL, layout and asset gates. U17/U18/U20 improve queue stability,
filters and search freshness without erasing Bookshelf-specific behavior.

Exit per slice: one authoritative owner of server state; correct cancellation,
reconnect, preferences, selection, validation, keyboard use and phone behavior;
existing direct links and root/subpath deployment remain usable.

### Phase 6: Operational capabilities selected by need

D18-D20 improve queue representation, pending-policy invalidation and quality
sorting. D22-D27 add reliable list snapshots, safe tag/cleanup policy, a book-native
custom-list schema and richer completion/health notifications. Consider D28/D30
only for a demonstrated integration need; arbitrary import scripts must not
bypass the transaction boundary.

Exit: failed list/provider requests cannot erase membership or change user
ownership; each external effect follows a committed operation and has a stable
identity and bounded retry policy.

## First implementation batch

### 2026-11-14 implementation record

P26 and P02 have implementation and regression fixtures in the uncommitted
`fix/sonarr-v5-foundation` working tree, based on
`38f48b1716fb51adb648c087c33ebe1fa620438e`. P26 is **Verified** locally; P02
remains **In progress** pending a supported ARM work-factor benchmark.
No live development or production home, server, credentials or library was
changed. The pinned comparison above remains the original research snapshot.

For P26, [BasicRepository](../src/NzbDrone.Core/Datastore/BasicRepository.cs)
propagates write failures, rejects missing update rows and commits general bulk
updates atomically. Failed writes do not emit success events. The existing
[import transaction path](../src/NzbDrone.Core/MediaFiles/MediaFileRepository.cs)
remains in place. New [repository failure fixtures](../src/NzbDrone.Core.Test/Datastore/BasicRepositoryWriteFixture.cs)
and [API error fixtures](../src/NzbDrone.Api.Test/ErrorManagement/ReadarrErrorPipelineFixture.cs)
cover the changed boundary; existing [staged-import](../src/NzbDrone.Core.Test/MediaFiles/ImportApprovedTracksFixture.cs)
and [recording-identity fixtures](../src/NzbDrone.Integration.Test/ApiTests/BookControllerRecordingIdentityFixture.cs)
remain preservation gates.

For P02, [UserService](../src/NzbDrone.Core/Authentication/UserService.cs) uses
PBKDF2-HMAC-SHA512 with 220,000 iterations, a random 16-byte salt and a 32-byte
derived key. Successful legacy SHA-256 and lower-work-factor logins upgrade the
saved derivation; wrong passwords never migrate it. Fixed-time comparisons and
bounded stored-metadata validation protect verification. Saved user identities,
API keys, authentication defaults and both Forms and Basic remain supported.
[Conditional credential updates](../src/NzbDrone.Core/Authentication/UserRepository.cs)
prevent a login rehash from overwriting a concurrent reset or rename, with one
bounded reload and reverification after a conflict. Password-preserving settings
saves update the username alone.

[Migration 048](../src/NzbDrone.Core/Datastore/Migration/048_add_salt_to_users.cs)
adds nullable salt/work-factor columns without rewriting existing users or
library records. Rollback requires the matching **pre-upgrade database backup
and runtime**, because the older runtime cannot verify newly derived passwords.
[Host configuration](../src/Readarr.Api.V1/Config/HostConfigController.cs) returns
a password mask, preserves omitted or unchanged passwords and requires
confirmation for a new password. Authentication password fields are excluded
from configuration dictionaries, and [User SQL diagnostics](../src/NzbDrone.Core/Datastore/Extensions/BuilderExtensions.cs)
redact derivation fields. The [service](../src/NzbDrone.Core.Test/Authentication/UserServiceFixture.cs),
[migration](../src/NzbDrone.Core.Test/Datastore/Migration/048_add_salt_to_usersFixture.cs),
[concurrency](../src/NzbDrone.Core.Test/Authentication/UserCredentialConcurrencyFixture.cs),
[settings](../src/NzbDrone.Integration.Test/ApiTests/HostConfigPasswordFixture.cs)
and [Forms/Basic fixtures](../src/NzbDrone.Integration.Test/Authentication/PasswordAuthenticationFixture.cs)
provide the focused regression evidence. Validation passed:

- Offline core suite: **3,301 passed, 55 existing skips**.
- Focused API, settings and authentication fixtures: **81 passed**.
- PostgreSQL repository, credential migration/concurrency and import fixtures:
  **125 passed, one existing skip**.
- Packaged linux-x64 candidate: **seven HTTP smoke scenarios passed** in a
  disposable home, including bulk rollback, legacy upgrade, Forms/Basic login,
  settings preservation, confirmed reset and restoration with the previous
  binary/database/configuration. The original password worked after rollback.

These suites overlap. One legacy Forms login took 138 ms on the development
x64 host; this single smoke measurement does not establish ARM or load behavior.
Other RID/artifact acceptance remains part of P01/P21/P22. Host-config settings
still save before credentials, so a credential conflict may return 409 after
other settings were persisted; this batch does not provide a transaction across
the configuration file and user database.

That batch is followed by **P03**, the proxy/auth boundary unit recorded below.
P03 was followed by **P05**, current API-key validation, then P06 and P07,
recorded below and followed by P08. The next code item is **P09**, beginning
with runtime-independent dependency security updates.
P25 and supported runtime work follow the agreed phase order; D03/D04/D05 remain
near the front of the acquisition queue. This does not require the entire UI rewrite.

### 2026-11-14 proxy/authentication boundary

P03 is **Verified** locally in the same uncommitted working tree. Forwarded request
addresses, schemes and hosts are accepted only from loopback or configured
trusted proxy peers; complete trusted chains are consumed from the nearest
peer outward. Any remaining client-address header prevents local-network
authentication bypass. Empty or quoted-empty headers remain unresolved, and a
missing transport address cannot be supplied by an arbitrary forwarded header.
The handler reads authentication-required settings per request, avoiding stale
bypass decisions across DI registrations after settings change.

The advanced Host setting **Trusted Reverse Proxies** maps to API
`trustedNetworks`, XML `TrustedNetworks` and environment
`Readarr__Server__TrustedNetworks`. Empty means loopback-only trust; changes
require a restart. Existing API clients omitting the property preserve its
value, while explicit empty clears it. Invalid API lists fail before writes;
invalid startup entries warn and are ignored. Validation supports IPv4/IPv6
addresses and aligned networks, with ambiguous addresses and ranges that cover
all IPv4 peers rejected. The actual CGNAT choice now survives host settings
round trips; its default remains false. Outbound proxy settings, credentials,
library identities, media and import transactions retain their existing behavior.

The [trusted proxy deployment guide](trusted-proxies.md) covers existing proxy
upgrades, exact peer addresses, environment precedence, HTTPS header handling
and multi-hop trust. The [P03 record](sonarr-v5-porting-backlog.md#p03) links the
source and regression fixtures. Focused validation passed 93 parser/configuration
cases, 36 host/middleware/DI cases, 161 API/authentication cases and 32 frontend
cases, with no failures or skips in these focused suites. Frontend lint and the
production webpack/type build passed. The self-contained linux-x64 candidate
passed **39 packaged checks** through a local HTTPS proxy and disposable home,
including spoofed and unresolved headers, configuration preservation/rejection,
Secure Forms login cookies, API authentication, SignalR negotiation and an
actual WebSocket JSON handshake and clean close under `/bookshelf`. Live
authentication-required changes worked without restarting, and all owned test
processes/listeners were stopped. These checks include the framework forwarding
environment flag on the current bare host-builder path; they do not constitute
a full browser or cross-platform audit. P09 remains a deployment blocker; the
live development server and production have not been changed.

### 2026-11-14 current API-key validation

P05 is **Verified** locally in the same uncommitted working tree. The
[API-key handler](../src/Readarr.Http/Authentication/ApiKeyAuthenticationHandler.cs)
reads the effective configuration key when authentication runs and compares
read-only UTF-16 byte spans in fixed time. This preserves exact ordinal matching,
including malformed code units, without encoding-replacement collisions or
temporary byte arrays. Null/blank configuration values fail authentication.

Query/header/Authorization precedence, first-value and empty-value behavior,
the existing literal `Bearer ` parsing, claims/tickets and 401/403 responses are
unchanged. SignalR retains `access_token`. The old handler normally refreshed
its constructor snapshot on the next request already; this port does not claim
a previous restart-required rotation bug. Existing request authentication caches,
environment-key precedence and established connections retain their behavior.

[Fifty new handler cases](../src/NzbDrone.Integration.Test/Authentication/ApiKeyAuthenticationFixture.cs)
passed within **211 affected API/authentication cases**, with no failures or
skips. The HTTP project build and self-contained linux-x64 publish passed.
The packaged candidate passed **89 HTTPS-proxy checks** in a disposable home,
including the real key-reset command, old-key rejection/new-key acceptance on
fresh API and SignalR requests through all transports, unchanged process/user/
other settings, and a new-key WebSocket JSON handshake and clean close. Three
debug log files contained neither synthetic key. P03 proxy/authentication
regressions also passed, and all owned test processes and listeners were stopped.

This does not establish a remote timing exploit, a full browser/platform audit,
or forced revocation of an already authenticated request/connection. No live
development or production key, server, configuration or media was changed.
P09 remains the deployment blocker; this batch was followed by **P06**.

### 2026-11-14 bounded log redaction

P06 is **Verified** locally in the same uncommitted working tree. The
[redactor](../src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs) preserves
Bookshelf's broader provider/token/Goodreads rules and adds the donor's relevant
email/query/announce refinements. Each regex has a 100 ms timeout, with a
one-second cleansing budget checked throughout replacement. Input and output
are capped at 65,536 UTF-16 characters. Failed, oversized or partially processed
payloads become one fixed placeholder; raw-prefix truncation and rendering
retries cannot reveal an unrecognized secret fragment.

Configured file, console, debugger, update-file, syslog and database outputs use
protected rendering; emergency exception console output is protected too.
Normal layouts, levels/colors, rotation, cached NLog message snapshots and real
database errors are preserved. Database fields have separate budgets. The regex
budget cannot preempt arbitrary rendering code, and NLog's own pre-target/internal
diagnostics remain a third-party boundary; this is not universal detection of
unknown secret formats. Existing stored logs are not rewritten.

Validation passed **214 focused Common cases**, **3,310 offline Core cases**
(55 existing skips) and **211 API/authentication cases**, with no failures.
Coverage includes real regex timeouts, expansion limits, rendering exceptions,
real file/database output, emergency handlers and configured loopback UDP syslog.
The self-contained linux-x64 candidate passed **108 packaged checks** with Trace
logging: ordinary context survived, oversized redaction output became only a
placeholder, and API keys/probe secrets were absent from four text logs and 52
database rows. Prior HTTPS proxy, login, key-rotation and WebSocket checks also
passed, and all owned processes/listeners were stopped.

The [P06 record](sonarr-v5-porting-backlog.md#p06) links implementation and tests.
No live development or production log, setting, home, key or media was changed.
P09 remains the deployment blocker; this batch was followed by **P07**.

### 2026-11-14 validated backup restoration

P07 is **Verified** locally in the same uncommitted working tree. Direct
XML/SQLite and ZIP payloads are validated before publishing configuration or a
pending database. Archive metadata is bounded before the ZIP reader allocates
from entry counts, including ZIP64; extraction checks paths, types, sizes and
checksums. XML and read-only SQLite validation check document structure,
integrity, native book tables and known migration IDs, including legacy ID 158.
Uploads and staging use separate private directories. Synchronous publication
failures roll back attempted replacements or retain recoverable originals and
report failure.

Startup revalidates the pending database, keeps the old main/cache databases and
sidecars in a unique recovery group, and rebuilds the cache after a database
restore. Configuration-only restores preserve the cache and any pending
database; logs and media are not replaced. PostgreSQL configuration-only
restores remain available; SQLite database restoration under PostgreSQL is
rejected. These are structural compatibility checks, not a complete audit of
library data or every configuration value.

Validation passed **3,399 offline Core cases** (55 existing skips), including
**89 restore/startup cases**, and **224 API/authentication cases**, with no
failures. The self-contained linux-x64 candidate passed **51 packaged checks**:
11 invalid uploads left configuration, database rows, pending files and temporary
state intact; current and genuine pre-P02 application-generated backups restored
recording identities, user Identifier, API key, history and owned fake-media
hashes. Cache rebuild, log preservation and configuration-only behavior passed.
All owned processes were stopped. The genuine legacy check covers the available
reviewed pre-P02 artifact (schema 47 plus legacy migration 158), rather than all
historical Readarr releases. The [P07 record](sonarr-v5-porting-backlog.md#p07)
links the implementation and regression fixtures.

Recovery covers synchronous failures on local application-owned files. It does
not make multi-file restoration power-loss atomic or automatically undo a later
migration failure; retained recovery copies support manual recovery. No live
development or production home, backup, credentials or media was changed.
P09 remains the deployment blocker; this batch was followed by **P08**.

### 2026-11-15 bounded password login lockout

P08 is **Verified** locally, following foundation commit `a80a97d6c`. Five failed
Forms/Basic password checks from a resolved client address cause a five-minute
lockout. Attempts during lockout skip password verification and do not extend
it. Success clears failures; inactive failure sequences expire. Per-source
serialization and pinned entries close concurrent counter races without holding
a global lock while hashing. Tracking is capped at 4,096 addresses; at capacity,
new sources fail closed until space opens, preserving active tracked lockouts.
Counters are in memory and reset on restart. Existing API keys, cookies, None
and External behavior remain available.

Forms feedback uses an encoded UTC `lockedUntil` timestamp and independently
encoded return URL. The login page announces lockout and re-enables submission
at expiry, including after browser history restoration. Basic remains HTTP 401
with `Retry-After`; malformed headers fail safely. P03's trusted-proxy resolution
attributes failures, with no use of raw forwarded addresses. The
[P08 record](sonarr-v5-porting-backlog.md#p08) links implementation and fixtures.

Validation passed **225 authentication/API tests**, **39 frontend tests**,
**50 packaged checks**, and **nine isolated Chromium checks** of built login HTML.
These include trusted-proxy client separation, spoofed forwarded headers,
Forms/Basic behavior, unchanged API access, capacity/concurrency and clock-based
expiry, plus phone keyboard/status/history behavior. Production frontend build
and self-contained linux-x64 publish passed. All owned processes were stopped;
no running development/production server, home, key or media was changed.

P09 is next. Its security-compatible dependency slice can proceed on the current
runtime; broader framework/database/native upgrades retain their P01/P12/P13
requirements. Existing dependency advisories continue to block deployment.

### 2026-11-15 dependency security maintenance

P09 is **In progress**. Its runtime-independent security slice replaces the
Six Labors dependency path with bundled Magick.NET 15.1.0 and PdfPig 0.1.16,
preserving raster resizing and PDF metadata without a license key. Mail and
cryptography/token packages receive compatible security updates; old dependency
edges are removed and remaining vulnerable transitive versions are centrally
pinned. Restore now audits all dependencies at low severity and above without
suppressions.

The dated [dependency audit](dependency-audit.md) records the baseline nine
affected package IDs / 16 advisories, final zero-match result for the captured
NuGet feed, exact inventory scope, notices and validation: 691 Common and 3,439 Core tests (35/55 existing skips), 267 API tests,
64 focused media cases, 22 published-codec checks on glibc x64, 40 each on musl
x64/ARM64, and 50 authentication/51 restore package checks.
SDK 10.0.403 now enforces the NuGet audit policy through the repository SDK
configuration and CI; application targets remain net6.0. Full .NET 10 runtime,
database/native-platform and release acceptance remain in
P01/P12/P13/P21/P22/P25. No running development/production server, home or media
was changed. The earlier package-advisory blocker is resolved for the captured
inventory; remaining runtime and release gates still apply.

The three Linux self-contained candidates were published and exercised in
private homes. ARM64 MediaInfo 26.10 was built from the pinned sources and
verified directly with version and WAV JSON under QEMU. Startup OS detection
now follows v5's .NET API approach (commit `698a8d594`), eliminating an external
`uname` dependency. Full ARM child-process/import acceptance, Windows/macOS
execution and the missing Intel macOS image asset remain platform gates.

P01 is **In progress** for its SDK prerequisite. SDK 10.0.403 is selected by
`global.json` and mise; CI installs it alongside the old test runtime and derives
the SDK path from the selected executable. Locked restores and six test-wrapper
status regressions protect the dependency gate. This does not upgrade the
application from net6.0 or complete P01's runtime acceptance.

### 2026-11-15 .NET 10 runtime and framework alignment

P01's runtime implementation is committed as `d572aaa2b`, with the packaged
API-body-binding correction in `2b0edf8ef`. All 25 projects now target .NET 10;
SDK **10.0.404**, runtime **10.0.15**, active output paths, explicit executable
self-contained publishing and the canonical Linux packaging move together.
The previous .NET 6/SDK-only statements above describe earlier dated snapshots.
The Sonarr donor remains the same pinned v5 revision. The
[dependency audit](dependency-audit.md#net-10-runtime-alignment-2026-11-15)
records current versions, primary compatibility sources and the new clean-source
inventory: **145 package IDs / 145 exact components / 25 locks / zero matching
NuGet advisories**, with no advisory suppressions.

TimeProvider, modern PFX loading, explicit proxy-network trust, SQL array
expression lowering, stream reads and log-target lifecycle have compatibility
coverage. The hosted HTTP tests retain JSON request-body binding when DryIoc
also registers API resource types. No database migration is introduced. Existing
password formats, API keys, defaults, recording/part identities and media
ownership remain upgrade constraints.

Both solution configurations built without warnings, all locks passed clean
locked restore/audit, and the canonical locked backend build produced all three
Linux packages with runtime/updaters, native dependencies, notices and bundled
MediaInfo. Offline suites passed **4,551 cases**, with **103 not-executed TRX
records** (runner console summaries report 99 skips), including five hosted
binding cases. Eight real coverage cases and six shell exit-status cases also
passed. Genuine same-home .NET 6-to-10 preservation passed **24/24**;
current/genuine-legacy restoration passed **51/51**, and packaged lockout **50/50**.
Native probes passed **35** glibc-x64 and **55** musl-x64 checks. The actual
Dockerfile uses Alpine **3.24.4** helper builds and LinuxServer Alpine **3.24**;
its x64 S6/API/native-format/shutdown probe passed **54/54** checks.

ARM64 native probing passed **33** checks under explicit QEMU. Default HTTP
startup failed twice with unchanged candidates: target signal 11, then a managed
AccessViolation during Dapper DynamicClass parameter setup for
InsertMissingQualityDefinitions followed by QEMU target signal 6. Both failures
are retained. A private compatibility experiment with
`DOTNET_TieredCompilation=0` and `DOTNET_EnableWriteXorExecute=0` passed **53/53**
checks, including the 33 native checks, HTTP on runtime 10.0.15, preserved API
key and graceful shutdown. Candidate hashes remained unchanged and owned
containers were removed. These flags are not shipped.

Microsoft's [.NET 10 support notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#notes)
exclude QEMU for running .NET applications. These results do not identify the
failures' cause or establish default native ARM64 behavior. P01's implementation
and x64/musl-x64 acceptance are complete; it remains **In progress** solely for
default ARM64 startup acceptance on a **native ARM64 runner**. That runner is the
next step before closing P01.

Windows GUI compilation is not Windows execution; macOS/Windows runtime checks,
the missing Intel macOS image asset and the full matrix remain P22. Legacy Azure
is manual-only/default-off, and actual remote CI was not run. No live home,
server, credentials or media was changed.

### 2026-11-16 reusable local validation

P21's local portion now has a [single documented command](local-validation.md)
for package, startup/authentication, backup/restore and media checks, plus
explicit-baseline upgrades and optional native Docker validation. Its fixtures
and media probe live in the repository instead of depending on private machine
scripts. Inputs stay unchanged, output homes are new and private, failure and
interruption propagate, and owned processes/containers are cleaned up.

Fresh artifact preparation exposed a shared-output publication race; commit
`8b7e07065` serializes backend publication. The locked backend/frontend/package
build then passed. Results and scope are recorded in the
[validation guide](local-validation.md#verification-record).

GitHub workflow work is deferred by the billing constraint. P21 is **In
progress**; PostgreSQL and remaining delivery gates are still pending. P01's
native ARM64 check and P09/P12/P13/P22/P25 work remain open. This local slice
does not deploy the running server or use production library data.

### Remaining deployment gates

The 2026-11-14 package-advisory findings were addressed by the 2026-11-15
security slice above; their dated evidence remains in the P09 backlog record.
The new .NET 10 snapshot also has zero matching advisories across 25 projects,
without suppressions or warning exceptions. P01 awaits default HTTP startup
acceptance on a native ARM64 runner. P09 remains **In progress** for its broader
managed/database/native compatibility work. P12/P13 migration-engine/SQLite
changes, P21/P22 complete
artifact/platform acceptance and P25 release identity remain open. These local
commits have not been deployed or published.

## Porting and validation procedure

1. Select one backlog ID and inspect its pinned donor paths and tests. Re-read
   neighboring code; namespaces and similar filenames are not sufficient evidence
   that a patch is independent.
2. State the local compatibility decision: schema, API, settings/defaults, file
   ownership, identity and rollback. Resolve dependency IDs or record why an
   independently useful subset can be taken first.
3. Port the behavior and its useful regression cases in a focused branch. Adapt
   TV assumptions explicitly; retain local protections rather than replacing an
   entire subsystem to get one helper.
4. Test the smallest meaningful behavior set. For cross-layer work, add the
   relevant API/browser/upgrade cases and then the broader affected suites.
   Use synthetic catalogs, metadata and media; private library exports are not
   test fixtures.
5. For schema/native/runtime changes, validate a disposable old-home upgrade,
   startup, health, backup/restore and rollback using the exact delivery artifact.
   Restoring an older binary after a schema change may require its matching DB
   backup; an in-place binary swap is not a rollback proof.
6. Record donor SHA, local commit, adaptations, validation and limitations under
   the item. Mark it `Verified` only when its acceptance criteria are satisfied.

Useful existing commands include `dotnet test` on the relevant Core/API projects,
`node --test frontend/test/*.test.mjs`, the configured frontend lint/type/build
checks and artifact HTTP tests. P21/P22 must make the authoritative commands and
platform matrix reproducible in CI; current private development procedures are
not a substitute for that work.

## Source reuse and future updates

Bookshelf's [license](../LICENSE.md) and Sonarr's pinned
[license](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/LICENSE.md)
and [copyright notice](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/COPYRIGHT.md)
provide the starting point for source reuse. Keep the project's GPL terms,
upstream attribution and applicable notices intact. Track the exact donor source
and local changes, and distribute the corresponding source required by the
license with releases. New dependency/native-asset licenses need their own check;
the repository license does not replace them. Keep Bookshelf's branding and
release/update channels distinct from the donor.

To refresh this plan, pin a new Sonarr **v5** tag/commit, update the snapshot and
review changed donor paths against the port record. Re-evaluate affected items
and their tests; do not infer that an upstream merge or newer version preserves
book-specific behavior. An observed donor defect should remain an adaptation
note until its corrected behavior is independently validated.

## Release group implementation — 2026-11-15

RG01/RG02/RG03/RG05 are verified locally in the working tree based on
`4883fcaa724dd15e57c555cd13d39cbcba379a2f`; see the
[implementation record](sonarr-v5-porting-backlog.md#2026-11-15-release-group-implementation-record).
This implements selective parser improvements, manual-import choices through
preview and final command, stored-file visibility/correction, and optional group
token truncation. Explicitly clearing a group is distinct from leaving it
untouched. Ordinary same-path re-identification still preserves file identity
and acquisition provenance. Existing group custom-format IDs, profiles and
expressions require no migration.

Validation:

- **665 focused Core tests passed, 19 existing skips**, covering all parser and
  organizer fixtures plus manual import, aggregation, decisions and staged import
  preservation. All 23 original group-parser cases remain covered, alongside 57
  added cases.
- **25 API/recording-identity cases passed**, including group-only single/bulk
  file updates and manual-preview group, override and score serialization.
- **14 frontend payload/selection checks passed** against actual Babel-transformed
  source with mocked React/Redux/network. They cover untouched versus explicit
  set/clear, selected edition preservation and group-only file updates.
- Webpack compiled successfully with `--no-cache`; focused ESLint and CSS
  stylelint passed. The initial cached build failed with stale webpack dependency
  template errors; the clean-cache build resolved them without source changes.

A broader Core run encountered legacy live-provider tests: the invalid-author
fixture requested `api.bookinfo.pro` and timed out after 100 seconds. The
provider-dependent runs were stopped; these results do not claim a passing full
Core suite. The complete parser/organizer and focused import/API checks above
passed on the final implementation.

No browser-rendering or live-library import was run. RG04's pack-evidence design,
D05's import-score downgrade check and D13's minimum upgrade-score delta remain
planned and are not implied by these release group changes.

### Dev deployment and real-file checks — 2026-11-16

The separate dev service on port 8095 now runs a fresh, self-contained linux-x64
candidate with .NET **10.0.15**, current Release Group changes, the current UI,
Mono wrappers/native helper and bundled MediaInfo. Both locked restores and both
publishes passed. A stopped full-profile snapshot and the previous complete
.NET 6 runtime were retained in the protected local dev deployment directory.

The dev filenames exposed a trailing-ASIN false positive. Group parsing now
uses the existing `AudiobookId.IsAsin` contract to exclude ASIN/ISBN bracket tags
while preserving earlier groups. Ten additional regression cases include the
provided Mushoku Tensei filename ending in `[B0CJWTXLPJ] -H2OKing.m4b`, bare and
lowercase ASINs, ISBNs and non-identifier ten-character group names. **372 parser
tests passed, with 18 existing skips.** This supplements the previous day's
test record; the suites overlap.

Before switching the service, the candidate upgraded a copied dev database with
scheduled jobs deferred and indexers disabled. Browser checks against that copy
set `H2OKing` on the actual book's file record, reloaded, cleared it and reloaded
again. All six browser checks passed without runtime errors. Quality, path and
recording ID stayed unchanged. Manual-import set/clear previews retained the
same recording and edition without committing an import. Screenshots and check
results are retained with the deployment evidence. Media files were read only.

The first switch automatically rolled back because the verification script
incorrectly required an explicit JSON field for a null group. The serializer
omits null fields. After correcting the check, deployment passed, including a
nonblank manual-preview response on the dev service. The running Core/API DLL
hashes match the tested candidate. Migration 048 applied successfully; all 308
authors, 4,592 books and 2,305 files, plus every author/book/edition/file ID, were
preserved. File groups, paths, quality, existing credentials and Forms settings
match the stopped snapshot. The target media file's size, mtime and inode were
unchanged. No dev group values were saved by the smoke checks.

Existing null groups are not automatically backfilled from filenames. Use the
file editor's explicit set/clear action for existing records. Pack attribution
and the D05/D13 score policies remain separate work.

### Browser login redirect repair — 2026-11-16

The dev upgrade exposed an anonymous-browser regression: requesting `/` returned
401 with a login `Location` header, so browsers displayed an error rather than
the login page. The previous copied-profile UI checks used local-auth bypass
and did not cover this path. ASP.NET Core 10 suppresses cookie redirects on API
endpoints, and the frontend resource controllers also carry `[ApiController]`.
The [framework compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/aspnet-core/10/cookie-authentication-api-endpoints)
describes the changed behavior and the endpoint-specific override.

[Static resources](../src/Readarr.Http/Frontend/StaticResourceController.cs) and
[UI initialization](../src/Readarr.Http/Frontend/InitializeJsonController.cs)
now explicitly allow cookie redirects. API and AJAX requests retain 401 behavior;
Forms authentication and saved credentials are unchanged.

The new [HTTP redirect fixture](../src/NzbDrone.Host.Test/AuthenticationRedirectFixture.cs)
covers anonymous root/deep-link/initialization redirects, return URLs, login
access, authenticated pages and API/AJAX 401 responses at both root and a URL
subpath. **21 HTTP cases passed**, including the existing request-binding fixture.
A full Forms sign-in, protected book page and logout were also checked in a real
browser against an isolated copied profile with copy-only test credentials:
**seven browser checks passed**. The fixed self-contained candidate was deployed
to the dev service after a new stopped-profile/runtime backup; dev HTTP checks
confirmed root/deep-link/initialization 302 responses, login 200 and API 401,
with file metadata, credentials and authentication configuration preserved.

### P10 HTTP and refresh cancellation — 2026-11-16

P10 is verified locally in `25f0540bb` and `a13f988a1`; see the
[implementation record](sonarr-v5-porting-backlog.md#p10) and
[repeatable cancellation proof](local-validation.md#check-refresh-cancellation-and-shutdown).
Caller cancellation reaches rate limiting, redirects, response bodies and
downloads. Metadata refreshes and cover work observe command cancellation and
shutdown without treating cancellation as a provider failure. Refreshes fetch
fallback and missing-parent metadata before saving, then finish the current
apply unit before stopping further work.

Final validation passed **4,413 offline .NET tests** across Common, Core, API and
Host, with **94 existing skips**, plus **47 Python tests**. A fresh self-contained
package passed **38 cancellation checks** plus its unchanged-input check, and
the same candidate passed **193 package/app/restore/upgrade/media/Docker checks**.
API cancellation closed a held metadata body, preserved catalog/media and
allowed a successful retry; SIGTERM stopped a separate synthetic home cleanly.
The upgrade used the retained .NET 6 package; the Docker proof used the actual
Dockerfile and a native musl-x64 package. Input packages remained unchanged.

**P11 is the next implementation slice:** replace global IPv6 fallback with
per-connection Happy Eyeballs and add pooled connection expiry. Operating-system
DNS interruption was not simulated by the P10 transport tests; connection-family,
DNS failure and losing-socket cases remain explicit P11 gates. Native ARM64/P01,
broader P09/P21 acceptance and the separate schema modernization remain open.
No GitHub workflow was changed or run, and neither the running dev service nor
production appdata/media was changed by this work.

### P11 connection handling — 2026-11-16

P11 is verified locally in `2512159c9`; see the
[implementation record](sonarr-v5-porting-backlog.md#p11). New connections use
the pinned v5 Happy Eyeballs design with local ownership/cancellation fixes.
IPv6 and IPv4 attempts interleave per connection with a 250 ms stagger and
immediate fallback after failure. No process-wide IPv6 failure flag remains.
Cached clients renew connections after a maximum pooled lifetime of 30 minutes,
while active responses retain their connections until completion.

**56 focused cases passed without skips**, including deterministic race/DNS
tests and real IPv4/IPv6, HTTP/SOCKS5 proxy, HTTPS CONNECT/SNI, TLS, cookie and
pooling checks. A sequential expiry test changes an injected DNS answer after
the first response completes; a separate test keeps an active body open during
renewal. The production lifetime is checked directly and the renewal tests use
zero lifetime instead of waiting 30 minutes. OS DNS interruption is not forced.

The broader offline suites passed **4,459 .NET cases**, with existing platform/fixture skips.
Locked backend/frontend builds and fresh x64/musl-x64 packages passed. The same
candidate passed **231 combined package/app/restore/upgrade/media/cancellation/
Docker checks**, including unchanged input fingerprints. The running dev service,
production appdata/media and GitHub workflows were unchanged.

**P12, the migration engine compatibility slice, is next.** It must preserve
Bookshelf's schema history and verify synthetic SQLite/PostgreSQL upgrades;
Sonarr's TV schema is not a replacement. P01's native ARM64 gate and broader
P09/P21 acceptance remain open.

### P12 migration engine compatibility — 2026-11-16

P12 is verified locally in `bb4cd939e`; see the
[implementation record](sonarr-v5-porting-backlog.md#p12) and
[repeatable datastore proof](local-validation.md#check-the-migration-engine-on-sqlite-and-postgresql).
The migration stack now uses the pinned donor's FluentMigrator 8.0.1 and Npgsql
10.0.3. Historical Bookshelf main/log/cache migrations and their applied IDs remain
unchanged, including migration file 034's actual version 158. SQLite's provider
and busy timeout are deliberately left for P13.

The port preserves raw SQLite schema and sequence details during rebuilds,
uses native rename/drop where supported, keeps PostgreSQL explicit-ID behavior,
and restores/disposes migration context and services safely. Ordinary migration
errors do not trigger destructive log/cache corruption recovery. Stored library
data, text GUID conventions and dates retain their existing contracts.

**87 focused cases passed on SQLite and owned PostgreSQL**, with no skips.
Broader offline validation passed **4,523 .NET cases**, plus **23 existing
migration cases** separately on PostgreSQL. All **63 Python tests** passed.
Fresh packages passed **37 datastore checks plus two unchanged-input checks**,
including genuine old-binary upgrades, partial-DDL failure rollback and backup
recovery on both databases. The same candidate passed **231 existing delivery
checks**, including .NET 6 upgrade, cancellation and the actual Docker image.
The [refreshed dependency inventory](dependencies/2026-11-16-migrations/summary.json)
contains 119 exact components and zero reported NuGet vulnerabilities across 25
audited projects; license notices accompany the packages.

No live application home was an input. The running dev service, production data
and GitHub workflows were unchanged. Native ARM64/P01 and broader P09/P21 gates
remain open. **P13, SQLite provider and busy-timeout compatibility, is next.**


### P13 SQLite provider and lock behavior — 2026-11-17

Implementation `238cc9a0e` and the
[P13 evidence record](sonarr-v5-porting-backlog.md#p13)
update the managed provider to **System.Data.SQLite 2.0.4** and bundle the pinned
v5 donor's **SourceGear.sqlite3 / SQLite 3.53.4** engine. Loading is explicit and
fails without the matching bundled asset. The new glibc asset requires **glibc
2.34+**; musl uses its own binary. Exact package notices and RID checksums accompany
the new [120-component dependency inventory](dependencies/2026-11-17-sqlite/summary.json),
with zero NuGet-reported vulnerable packages across 25 projects.

The new engine's default foreign-key enforcement exposed an incompatibility with
Bookshelf's historical migrations. The connection factory explicitly retains the
legacy disabled-enforcement policy; historical migrations remain untouched. Main,
log and cache connections retain UTC/pooling/journaling behavior. Production
repositories verify GUID TEXT storage and fractional UTC/local dates under two
cultures and two non-UTC timezones.

The v5 **1000 ms native busy timeout** passes paired contention measurements,
while the **30-second default managed command timeout** remains separate. Both
100/1000 ms policies recover from a briefly held writer and report BUSY within
the measured command budget for a sustained lock; these results do not demonstrate
a throughput improvement. **98 paired SQLite probe checks** cover exact multipart
rows, concurrent readers/writers, online snapshots, cache/log writes, media
preservation, interruption rollback and the next writer. Candidate provenance
includes mapped engine bytes, source ID and compile options; a missing-engine
clone fails without falling back to host SQLite.

Backups use an unpooled staging destination, verify standalone journaling and
integrity, close before rename, preserve prior backups on failure and reject
source/symlink aliases. Stalled retries honor the source timeout, including
explicit unlimited zero, without clearing unrelated connection pools.

Local verification passes **4,538 source tests**, **23 PostgreSQL migration
cases**, **73 Python tests**, **131 app/restore/cancellation/datastore checks** and
**141 package/.NET 6 upgrade/media/Docker checks**. The fresh musl-ARM64 package
passes **17 static checks**. Runtime acceptance on native ARM64 remains required,
so **P13 stays In progress** for that gate. This work used no GitHub workflows and
changed no running development service, production appdata or media.


### P15 optional database logging — 2026-11-17

Implementation `9c36a3bc9` completes the
[P15 local acceptance record](sonarr-v5-porting-backlog.md#p15). Database logging
still defaults on. XML `LogDbEnabled=false` or environment
`Readarr__Log__DbEnabled=false` disables it after restart; environment values win
over XML and explicit false is preserved. The effective database registration
controls startup SQL-target registration, log paging/clear/trim and update-history
consumers. Main/library/download history and the metadata cache stay active, as
do file/console logging and existing backups. Existing log databases are retained
for reenabling. Update-install history and its corresponding notifications pause
while their log storage is disabled; this is documented with the setting.

All **4,560 source tests**, **23 PostgreSQL migration cases**, **81 Python tests**,
**233 delivery checks** and **63 optional-log checks** pass. The new proof covers
missing/inaccessible SQLite and PostgreSQL log databases, default/enabled/disabled
modes, both option sources, existing-row preservation, reenabling with fresh SQL
writes, independent logs and backups. The pre-P15 package correctly fails the
missing-feature check. Every application home, database and container is owned
synthetic data, with input fingerprints preserved and owned resources cleaned up.

**P14 is next:** full PostgreSQL connection strings can now validate an optional
log connection consistently. No GitHub workflows, running development service or
production appdata/media were changed; P13 native ARM64 and the broader platform
and release gates remain open.


### P14 full PostgreSQL connection strings — 2026-11-17

Implementation `787a593e2` completes the
[P14 local acceptance record](sonarr-v5-porting-backlog.md#p14). XML/environment
strings now configure main, metadata cache and optional diagnostic logs. Missing
active strings and mixed component/string sources are rejected without a SQLite
fallback. Presence tracking distinguishes omitted settings from explicit empty,
zero and default ports under real .NET configuration binding. Legacy component
selection, credentials and database names retain their existing behavior.

Npgsql retains quoted-password, TLS and other connection options with
`Enlist=false`. Environment strings are absent from generated/default XML and
host API resources. Existing XML and backup contracts are preserved. Config/parser/
binding and PostgreSQL connection/startup/API/logging-target failure boundaries
avoid raw secret-bearing provider payloads. Migration stage/version and safe
SQLSTATE diagnostics remain; bounded cleansing removes whole connection strings.

All **4,671 source tests**, **23 PostgreSQL migration cases**, **90 Python tests**,
**115 connection-string checks**, **63 optional-log checks**, **52 SQLite checks**
and **233 delivery checks** pass. Owned
PostgreSQL 16 SCRAM authentication exercises semicolons/quotes in real passwords;
the genuine pre-P14 binary's component-mode library is reopened through DSNs with
schema, identity, files, history, cache, media and account authentication intact.
The previous package correctly fails the no-SQLite-fallback requirement. The proof
uses private Unix sockets, so TLS transport/certificate integration is not claimed.

The running development service, production appdata/media and GitHub workflows
were unchanged. P13 native ARM64 and broader P01/P09/P21/P22/P25 gates remain open.
The P16 record also tracks an existing single-user assumption in host-settings
access found while independently preserving multiple authenticated accounts.


### P16 account settings and local recovery — 2026-11-18

P16 is **Verified** locally in `0b6c53444`; see the [policy and acceptance record](sonarr-v5-porting-backlog.md#p16)
and [user-facing recovery instructions](../README.md#accounts-and-password-recovery).
Bookshelf retains existing Forms, Basic and None defaults, local-address policy,
API-key requirements and saved account identities. It does not inherit the donor's
forced mode conversion, generated bootstrap credentials or reset-token paths.

Host settings now select credentials by the saved account identifier. An
authenticated Forms/Basic account edits its own credentials; API-key-only access
to multiple saved accounts requires explicit selection before credential or
authentication-mode changes. Unrelated general saves can proceed without a
selection when those fields are unchanged. The frontend shows **Account to edit**
when choices are available. Safe account metadata and masked passwords replace
the previous multiple-account `SingleOrDefault` failure. Existing accounts remain
shared administrators of one application; no roles, registration or separate
per-account library model is introduced.

Stopped-server recovery uses
`Readarr -passwordreset -data=/absolute/existing/home`, the existing database
configuration, hidden interactive username/`id:GUID` and password prompts, and one
attempt within a two-minute total input window. It requires a compatible existing
schema, opens only the main database and does not initialize or migrate a home.
The selected password changes through the salted credential compare-and-swap
path; username, identifiers, authentication mode, API key and library remain
unchanged. The utility creates no reset token or HTTP endpoint, and reports
generic errors without echoing supplied values.

Forms credential stamps invalidate earlier sessions after a credential change or
recovery. Existing Forms cookies from before this check need one fresh login;
API keys remain independent. The implementation also redacts attempted values
from authentication validation responses. Configuration and account writes keep
their separate persistence order; this slice does not promise a cross-store
transaction or a concurrent registration workflow.

The packaged authentication proof passes **111 checks** on owned SQLite and
PostgreSQL 16 homes, including recovery/startup mutual exclusion and unchanged
identities, settings, API keys and library data. A genuine pre-P16 binary fails the
same-content-root old-cookie assertion as expected. Source suites pass **4,730**
cases plus **44** authentication integration and **23** PostgreSQL migration cases;
**101** Python tests, **233** delivery checks, **115** PostgreSQL connection-string
checks and **63** optional-log checks pass. Desktop and phone browser smoke tests
verify selection, masked passwords, cleared confirmation and a targeted rename.
See the [validation record](local-validation.md#p16-verification-2026-11-18).

P02's supported ARM password work-factor
benchmark and P13's native ARM64 runtime acceptance remain open, together with
broader P01/P09/P21/P22/P25 platform and release gates. This work does not deploy
the running development service or use production appdata/media or GitHub
workflows.


### P24 URL-base login redirects — 2026-11-18

P24 is **Verified** locally in `9dda441fd`; see the
[P24 acceptance record](sonarr-v5-porting-backlog.md#p24). The login redirect helper
checks the raw path against an exact base or a slash-delimited child using ordinal
case-insensitive comparison. `/bookshelf` no longer counts as a child of `/books`.
Exact-base queries/fragments, application-relative paths and encoded bytes retain
the intended single prefix; non-local destinations still use the existing local
URL guard. This is a routing correctness fix, not a new external-redirect guard.

All **41 focused source cases** pass. The initial packaged proof covers fourteen
real login destinations plus base-path logout. No saved identity, authentication
mode, API key or library contract changes.


### P04 optional allowed-host filtering — 2026-11-18

P04 is **Verified** locally in `c03ea78e2`; see the
[P04 acceptance record](sonarr-v5-porting-backlog.md#p04). The optional XML/env/API/UI
setting retains permissive access when empty. Configured exact DNS/IP names and
`*.domain` subdomains are normalized; wildcards do not match their apex. Bind
wildcards, ports, schemes, paths, CIDRs and malformed entries are rejected. Request ports do not
change host matching. Localhost, loopbacks and a valid normalized server hostname
remain available for local recovery. Invalid startup settings fail before serving;
invalid saves do not change accounts or configuration. Omitted/null values preserve
XML, explicit empty clears it, and environment values take precedence. Restart is
required after changes.

The middleware filters the effective host after trusted forwarding and before
UI/API/SignalR access. **90 focused source cases** include trusted/untrusted proxy
boundaries, and the initial packaged proof covers hosts, ports, localhost,
configuration/environment compatibility and startup preservation. The combined
P24/P04/P25 `accesspolicy` command passes **62 checks**, including unchanged inputs
and cleanup. A genuine pre-P04 package fails the expected unknown-host assertion.
The packaged proxy check uses loopback; source fixtures supply forged remote peers.
The upgrade follow-up `0015c349f` keeps absent defaults out of existing XML and
adds an exact-byte regression; the genuine older-home upgrade then passes.


### P25 local update isolation and candidate provenance — 2026-11-18

P25 remains **In progress**. Commit `736ffe104` adds an always-deny in-app update
policy and compiled fork identity; see the
[P25 local record](sonarr-v5-porting-backlog.md#p25). Existing update settings and
markers are preserved, but they cannot authorize legacy Readarr downloads or file
replacement. Update listings/checks remain empty/no-op; installation and the
standalone updater fail with an explicit manual-installation reason. Backend
assembly metadata and frontend provenance share the product ID
`bookshelf-audiobooks` and source commit/tree/branch/dirty state. The initial hosted
identity proof passes **8 checks**, including unchanged input.

Local tooling stages only a clean, matching, tested Linux x64/.NET 10 package with
its corresponding GPL source archive and evidence receipts. Its unsigned manifest
binds product, local `develop` channel, version, source, runtime/RID, schema and
checksums, and explicitly denies publication and in-app updates. It is not a
signed release or an operational update feed. Clean source `0015c349f` now passes
**301 delivery checks**, **111 authentication checks** and **41 actual candidate
CLI checks**, including failed-receipt and controlled-tamper rejection. Its
version `10.0.0.36421` candidate includes the exact corresponding GPL source and
remains unsigned and unpublished.

Final source verification passes **4,986 cases** across Core **3,856**, Common
**836**, API **16**, Host **181**, authentication integration **54**, PostgreSQL
migration **23** and updater **20**, retaining **97 existing skips**. Python checks
pass **131 tests**. Desktop/390px browser checks pass **20 UI/API assertions plus
seven preservation/cleanup guards**. See
[local validation](local-validation.md#p24-p04-and-local-p25-verification-2026-11-18)
for the final package/candidate record. GitHub workflow gates, signing, publication,
container tags, a fork-owned feed and the supported platform matrix remain open;
P02/P13 native ARM acceptance is also unchanged. No running development service,
production home/media or GitHub workflow was changed.


### P17 optional SSO deferred — 2026-11-18

The user explicitly requested that P17 be skipped. It is **Deferred**, and this
batch introduces no OIDC provider, dependency, settings or callback flow. Existing
Forms/Basic, API-key access and local password recovery continue under the P16
policy. Revisit SSO only after an explicit product decision.


### D03 and D04 client routing and completion — 2026-11-18

D03 and D04 are **Verified**. `f7ee205d8` always applies author-tag eligibility
before client overrides, blocked-client policy, priority and round robin.
`ef8beac21` and `3aeb8c56b` require distinct intended books and coherent committed
recording parts before completion, source retirement or client cleanup. See the
[completion policy](download-completion.md) and
[backlog acceptance](sonarr-v5-porting-backlog.md#d04).

Automatic and manual imports, restart restoration and both client cleanup paths
share persisted, revalidated file evidence. Tracked imports retain source copies
through database publication. Copy saves a durable retention policy before a
manual selection imports; failed retirement remains retryable after restart.
The verifier refreshes mapped paths, supports configured library-root aliases,
rejects unsafe child links, and waits for client status `Completed`. Client
removal preserves data for explicit Copy and nonmovable sources. Unknown scope
withholds cleanup; files absent from local inventory and unknown publisher totals
remain D07/B03 work.

Verification passes **5,205 source cases** with **97 existing skips**, **29 focused
cleanup follow-up cases** and **141 Python tests**. Clean source `3aeb8c56b`,
version `10.0.0.39353`, passes **433 delivery checks** (including **132 acquisition
and 53 actual Docker checks**), **111 authentication checks** and **43 actual
candidate CLI checks**. The local unsigned/unpublished Linux x64 candidate binds
that exact source, package and evidence; its release gate now requires acquisition
proof. See [local validation](local-validation.md#d03-and-d04-source-verification-2026-11-18).
No running development service, production/Unraid home or media, or GitHub workflow
was changed. P17 remains deferred; broader platform and release-feed gates retain
their existing status.


### D01/B01, D02 and D05 preservation and preference gates — 2026-11-19

D01, B01, D02 and D05 are **Verified**. `127f0882f` fixes edition-scoped
retirement, shared-path ownership, per-recording part/runtime coverage and shared
Calibre-record deletion. It adds a versioned synthetic home recipe and real
authenticated API/browser upgrade, restart and backup-restoration harness.
`c331b0bea` adds stable rejection codes across search/import/queue contracts and
prevents same-quality custom-format downgrades within a selected recording.
Intended-name scoring preserves acquisition provenance and explains filename
preference loss; explicit manual import retains its existing override behavior.

The [preservation contract](identity-preservation.md) defines source and hosted
fixture boundaries. Automatic file imports remain staged; another edition's
files or runtime cannot authorize retirement. Readable API messages and
temporary/permanent classification remain compatible, with additive codes and
unknown-value round trips. The published guard is checked against actual
custom-format conditions, while filesystem/naming/repository inputs are owned.

Clean artifact source `c331b0bea`, version `10.0.0.34076`, passes **5,281 source
cases** with **94 existing skips**, **148 Python tests**, **441 delivery checks**
(including **140 acquisition** and **53 Docker**), **111 authentication**,
**262 identity** and **45 actual candidate CLI checks**. The local unsigned and
unpublished candidate now requires eleven gates, including identity evidence.
See [local validation](local-validation.md#d01-b01-d02-and-d05-verification-2026-11-19).

This completes the B01/D01 fixture and runnable-harness entries in Phase 0; P25
publication/feed/platform gates remain open. D06-D08 classification/policy work
and B03 pack membership remain separate. P17 remains deferred. No running dev
service, production/Unraid home or media, or GitHub workflow was changed.


### D06, D07 and D11 diagnostic and recovery gates — 2026-11-19

D06, D07 and D11 are **Verified**. `e13f2ebd0` adds book-aware rejected-file
classification and validated advanced Media Management extension policy.
`7cbb3b78e` adds a bounded client inventory contract and qBittorrent adapter.
`fd35e9d11` integrates local/client diagnostics and an explicit recoverable
`importBlocked` state, bounded retries, failure detection and receipt-only
cleanup recovery. `faf8684c1` makes the reusable browser proof wait for actual
rendered controls and advanced-setting state.

The [completion/recovery policy](download-completion.md#blocked-imports-and-file-diagnostics)
documents supported containers, harmless sidecars, extension-setting semantics,
API state compatibility and 30–300-second retry behavior. Remote file inventories
remain diagnostic data and never authorize filesystem work or completion.
Existing Copy, ownership and committed-part guarantees remain enforced. Actual
client failures retain their existing handling; extension rejections alone do not
activate D08 failure/blocklist/redownload automation.

Source verification passes **5,447 cases** with **94 existing skips**, plus
**148 Python tests** and **2 queue UI tests**. Clean artifact source `faf8684c1`,
version `10.0.0.36335`, passes **466 delivery checks** (including **165 published
acquisition and 53 Docker checks**), **111 authentication**, **262 identity**,
**40 desktop/390px settings UI** and **45 actual candidate CLI checks**.
The glibc and musl builds carry matching clean provenance. The unsigned local
Linux x64 candidate and GPL source archive bind that exact commit; a later
documentation-only commit does not relabel it. See
[local validation](local-validation.md#d06-d07-and-d11-verification-2026-11-19).

The running development service remains unchanged. No production/Unraid data,
real library media or GitHub workflow was used or changed. P17 stays deferred;
D08, D09/D10/D12 and broader signing/feed/platform work retain their status.


### D10, D09 and D12 tracking, submission and intervention — 2026-11-19

The next acquisition slice scopes opaque download IDs to their client throughout
tracking, history, queue actions, manual imports, receipts and seed policy. A
complete successful poll can reconcile missing jobs. Offline or partial polls
keep last-known queue state visible and prevent automatic processing/cleanup.
Client changes debounce refreshes and invalidate freshness; re-grabs reset only
their own cached pair.

Eligible-client failover occurs only before an add, using read-only readiness
checks. Explicit routes and author tags retain their meaning. An unknown add
outcome is held durably across restarts and later batches, unless the actual
submitted torrent hash is confirmed on the same client. Activity → Queue lets
an operator review the named client and explicitly clear a hold. This enables
future grabs without removing or restarting a client job. Static readiness in
some legacy adapters can result in a review hold instead of automatic failover.

Blocked-import notifications now have a durable once-per-episode attempt marker,
rich client/source/identity diagnostics and optional webhook support through the
existing opt-in. Polling and changed diagnostics do not repeat alerts. A new
grab/source or verified completion after source retirement can start a new
episode. The marker records an attempt, so a crash or provider failure after it
is written can lose that notification. No real transports are used in validation.

D08 automatic rejection-directed failure/blocklist/search remains separate.
P17 remains deferred; publication, signing/feed and unverified platform gates
retain their previous status. Verification evidence follows in local validation.


Verification: clean source `fcf7e589b`, version `10.0.0.38144`, passes **5,599
source cases** with **94 existing skips**, **148 Python tests**, **42 frontend
Node tests**, **552 delivery checks** (including **251 acquisition** and **53
actual musl Docker**), **111 authentication**, **262 identity-preservation**,
**30 queue browser** and **45 actual candidate CLI checks**. Both clean native
packages build with matching source provenance. Inputs and owned-process cleanup
remain intact. The dev service was not restarted and the candidate remains
unsigned/unpublished. See [local validation](local-validation.md#d10-d09-and-d12-verification-2026-11-19).


### D13, D14 and D17 acquisition policy — 2026-11-20

Quality profiles now set the minimum custom-format improvement for upgrades.
A zero threshold preserves previous behavior. Changed thresholds refresh
pending comparisons for the affected profile, and automatic replacement checks
the intended filename and each replaced recording part.

Language custom formats use available recording evidence and support bilingual
and unknown recordings. Selected Edition Language compares against the assigned
edition. The D21 feed parser fix preserves comma-separated and repeated language
values. Grabs, queue scoring and local import retain the appropriate evidence.

An opt-in check estimates the destination bytes and reserve before each grab.
It budgets a staging copy, plus the download when reported local output shares
the library filesystem. Accepted and uncertain adds count against the estimate
for that batch. The import check continues to verify actual space when needed.

**Verified** on `e191326ba` with **5,687 passing source tests**, **569
package delivery checks**, **41 datastore checks**, and **60
desktop and phone settings checks**. See [local validation](local-validation.md#d13-d14-d21-language-parsing-and-d17-verification-2026-11-20).


### D15, D16 and B04 release size and audio preferences — 2026-11-20

Quality profiles can set a minimum, preferred and maximum release size for
individual qualities. Audiobooks use a rate per hour from trusted duration.
Ebooks use the full release size. Older profiles have no active rules. The
current profile is checked before adding a download. Ranking uses distance from
the preferred size after quality, custom format and other existing priorities.

Audio custom formats extend scoring to codec, channel count, bitrate mode,
sample rate and narrator. Audio conditions use one stream and preserve the
origin of the evidence through search, grab history, queue and file scoring.
The codec options distinguish AAC profiles, xHE-AAC and USAC, carriers with
Atmos, Opus and lossless formats. Tests cover missing and contradictory data.

**Verified** on `9bc7dc65a` with **5,740 passing .NET tests**, **589
package checks** including **288 acquisition checks**, **41
datastore checks**, and **34 desktop and phone settings checks**. See
[local validation](local-validation.md#d15-d16-and-b04-verification-2026-11-20).

### D19, B05 and D26 pending decisions, media analysis and completion webhooks — 2026-11-20

Pending releases reflect saved cutoff, size, format, delay and routing changes
while preserving their original delay. New rows persist their work identity
so a metadata refresh cannot silently assign them to a different work.

MediaInfo analysis records its parser revision, bundled tool build and file
state. A bounded hourly task refreshes stale measurements and backs off on
failed reads. The file view can label saved measurements that need a refresh.

Webhook connections can opt into the versioned `ImportComplete` event. The
verified receipt supplies recording and part identities. Saved history supplies
available custom-format score evidence. Bookshelf records the delivery attempt
before sending so restart and cleanup retries preserve its event identity.

**Verified** on `f9ad59d0f` with **612 package delivery checks**, including
**305 acquisition checks**, and **26 desktop and phone browser/API checks**.
See [local validation](local-validation.md#d19-b05-and-d26-verification-2026-11-20).

### B07, U01 and U02 provider status and keyboard accessibility — 2026-11-21

**System → Status → Metadata Providers** shows the latest recorded provider
outcomes and their timestamps. It distinguishes missing metadata, incomplete
catalog coverage, cached responses, and unavailable providers.

Icon actions have localized names and monitoring buttons expose their pressed
state. Dialogs expose their title, keep keyboard focus in the active dialog,
and return focus to its opener. Destructive confirmations place focus on
Cancel when opened.

**Verified** on `e53d0e4c5` with **5,771 passing .NET tests** and **69
packaged provider and accessibility checks**, including **47 desktop and
phone browser checks**. See
[local validation](local-validation.md#b07-u01-and-u02-verification-2026-11-21).

### B08, B02 and U04 library review and accessible forms — 2026-11-21

**Review Recording** shows why a file is unmapped or conflicts with its
assignment. It presents filename, folder, tag, and catalog evidence together.
An unchanged file with one eligible recording can be associated in place
after reviewing the exact edition. The dialog guides other cases to the
appropriate existing workflow.

**Reconcile Author** previews a reviewed name-only profile's move to its
canonical author. Existing recording identifiers, file paths, series links,
and history are preserved. Profiles must use the same folder, and differing
settings require confirmation before using the canonical profile's settings.

Shared form rows connect labels, help and validation to inputs. Root-folder,
provider and local-recording dialogs use the same accessible form behavior.

**Verified** with **5,793 .NET test executions**, including the SQLite and
PostgreSQL review cases, and packaged desktop and phone browser checks.
See [local validation](local-validation.md#b08-b02-and-u04-verification-2026-11-21).

### U03, U05 and U06 menus, action feedback and React 18 — 2026-11-21

Library menus support keyboard selection and keep their popups within the
available viewport. Provider tests, provider saves and clipboard actions
announce progress or results as appropriate.

React 18.3.1 boots with `createRoot` in its own commit, `3f21f7b6a`.
The browser checks cover library navigation, local recording saves, nested
settings dialogs, column dragging and a single refresh after SignalR
reconnect. Development warnings from function-component defaults and the
legacy virtualizer are tracked in U07 and U21.

**Verified** using synthetic application homes, with browser checks on the
glibc package and Docker checks on the musl package. See
[local validation](local-validation.md#u03-u05-and-u06-verification-2026-11-21).

### U07, U08 and U09 typed identities, transport and status cache — 2026-11-21

U07's first strict TypeScript slice distinguishes book, edition, marketplace,
and local recording identities. `yarn typecheck` and the CSS declaration check
run locally on the pinned Node toolchain.

U08 and U09 move **System → Status → Metadata Providers** onto validated Fetch
responses and a Query cache. The panel retains the last valid observations
during refresh and cancels its pending request on navigation. Concurrent
observers share a request.

The three slices are in `23d5321c3`, `b8a495d95`, and `7ed24552a`.
See [local validation](local-validation.md#u07-u08-and-u09-verification-2026-11-21).

### U10 and U11 History pagination and reconnect — 2026-11-21

Activity → History keeps rows visible during a page, sort, or filter change.
Details and mutation controls are disabled on retained rows until the active
request completes. The page restores its position on Back and corrects it when
the result set shrinks.

History refreshes after committed changes and reconciles its cached pages on
SignalR reconnect. The implementation is in `57652fae3` and `a7c852254`.
Queue and Wanted remain separate migration slices. See
[local validation](local-validation.md#u10-and-u11-history-verification-2026-11-21).

### U10, U11 and U17 Wanted and Queue — 2026-11-21

**Wanted → Missing** and **Wanted → Cutoff Unmet** use keyed pagination and
refresh after library changes or reconnect. Bulk Search and Monitor use the
active page's selection. Queue and file information follow the active books.

**Activity → Queue** uses paged queries and captures the exact selection when
a removal or blocklist confirmation opens. Live refresh waits until the
interaction ends. A rejected action refreshes the page and shows an error.

See [local validation](local-validation.md#u10-u11-and-u17-wanted-and-queue-verification-2026-11-21).

### U12, U14 and U15 preferences and settings — 2026-11-21

Queue migrates the saved browser layout, sort, page size, and unknown-author
option. Download Clients holds the global Completed and Failed Download
Handling drafts locally and updates the saved values from the API response.

Settings protects dirty forms during navigation and reload. Closing the
Add/Edit Download Client dialog can discard its edits while keeping the
settings page's draft. See
[local validation](local-validation.md#u12-u14-and-u15-preferences-and-settings-verification-2026-11-21).

### U13, U16 and U18 selection, navigation and filters — 2026-11-22

Wanted bulk selection follows local book IDs on the active page. Bookshelf
uses the React Router data router with one deployment basename and carries
forward dirty-form protection, bookmarks, and collection state restoration.

History can combine a book with several event types and save the filter.
Queue can combine download status with protocol. Both apply filters across
the full results before counting and paging. See
[local validation](local-validation.md#u13-u16-and-u18-selection-navigation-and-filters-verification-2026-11-22).

### U19, U20 and U21 search and library performance — 2026-11-24

Interactive search now uses context-specific results, instant local filtering,
and explicit refresh. Each server-side search result has its own grab identity.
Results use measured windows of table rows or responsive cards, with audio
details and rejection reasons available in the results.

The large-library benchmark confirmed that the current virtualizer keeps
mounted rows bounded and identified a Back-navigation scroll restoration issue.
The library views now save the position received from their scroll containers.
See [local validation](local-validation.md#u19-u20-and-u21-search-and-library-performance-verification-2026-11-24).

### U22, B06 and B03 asset and recording lifecycle — 2026-11-24

Vite replaces Webpack with content-hashed output and a loopback development
server. Packaged URL-base behavior, search workers, fonts, static pages and
source provenance are retained. The local hot-reload and localization checks
are complete; CI and other-platform acceptance remain separate open gates.

Local recordings now support metadata corrections and explicit local author
creation. Both local edits and publisher-pack work associations use revisions
to reject stale changes. Work membership is independent of recording and file
ownership, with confirmed coverage separated from uncertain volume labels.
The hosted verification upgrades a disposable library, uses both editors and
checks restart, rescan, backup restoration and deduplicated related-file totals.
See [local validation](local-validation.md#u22-b06-and-b03-asset-and-recording-lifecycle-verification-2026-11-24).

### RG04, B09 and D22 collection diagnostics — 2026-11-24

Pack release-group evidence now distinguishes independently identified
recordings from an ordinary multipart audiobook. The original acquisition
labels are retained, and an explicit manual choice remains authoritative.
Library → Statistics reports catalog and owned recordings, confirmed works,
tracked files, measured audio duration and media breakdowns without counting
shared file associations twice or treating stale analysis as current.

Saved import lists retain their independent last complete successful membership,
including exact exclusions and unresolved provider identities. Partial or failed
list responses do not erase previously observed members. This is a read-only
membership view, not tag synchronization or library-cleanup policy.
See [local validation](local-validation.md#rg04-b09-and-d22-collection-diagnostics-verification-2026-11-24).


### D23–D25, D08, D20 and D29 list and acquisition policies — 2026-11-24

Lists can apply or synchronize author tags with explicit list ownership.
Non-destructive list cleanup uses a read-only preview and confirmed action on
exact list-created recordings; it cannot run automatically or remove media.
The custom JSON provider supports a documented, bounded audiobook-native
payload and retains last success when complete membership cannot be proven.

Indexer rejection policy is explicit and conservative; terminal handling
requires confirmed source evidence and never removes client data. Quality
sorting uses live saved-profile rank with equal group ranks and a lowest-file
collection sort. qBittorrent total and inactive seeding modes retain current
defaults and reject unsupported settings rather than changing semantics.
See [local validation](local-validation.md#d23-d24-d25-d08-d20-and-d29-policy-verification-2026-11-24).

### Dependency, typing, queue/API, host and notification slices — 2026-11-24

Six more locally accepted roadmap items are **Verified**: P18 (TLS certificates
and keys), P19 (host lifecycle study and graceful external restart), P20
(coexisting API versions), D18 (download-level queue), D27 (health recovery),
and D28 (committed media-server refresh batching). The existing v1 API and
book/edition identities remain intact; v2 is advertised separately.

P09 and U07 advanced within the staged dependency/strictness work. The refreshed
NuGet and Yarn graphs report zero advisory findings at the time of audit;
large remaining JS conversion and broader native acceptance are still open.
P22 now has an enforced [Linux platform matrix](platform-support.md) and
static native package checks; native ARM64 and release acceptance are not
established. P23 adds functional authenticated, opt-in request/SQL aggregates
with bounded monotonic retention and tested large-library presentation, while
independent background-job measurements and broader overhead evaluation remain
open. P17 stays deferred as requested; no GitHub workflow or production upgrade
was run.

See the [detailed backlog](sonarr-v5-porting-backlog.md) and
[local validation](local-validation.md#dependency-typing-queue-api-host-and-notification-verification-2026-11-24).

### Background profiling, typed library boundaries and local release — 2026-11-24

P23 and D30 now have **Verified** local acceptance (83/92 overall). The
performance panel separately measures supported background commands and HTTP
requests, with bounded retention and provider timing, while preserving the
existing API counters. D30 completes the design decision to retain the
ordinary importer and use committed observations; a future optional staged
transform must satisfy the [import extension contract](import-extension-design.md)
before any execution feature is proposed.

P09's maintained dependency-line trials and U07's eight component conversions
advance both staged items. A real controlled virtualized-grid navigation
regression found in the combined package was corrected with an external
scroll callback and verified across table/poster/overview modes. The final
release source `7b941a853` passes the source, frontend, tooling and hosted
checks. P25's actual unsigned local `develop` candidate **10.0.0.42949** was
staged and independently verified, with matching GPL source, checksums,
license notices and exact evidence. Publishing and built-in updating remain
disabled; P25, CI and native ARM64 acceptance are still open.
See [local validation](local-validation.md#background-profiling-typed-library-resources-dependency-compatibility-and-local-release-2026-11-24)
and [the local candidate](release-policy.md#current-local-candidate-2026-11-24).


### Strict shared infrastructure and dependency compatibility — 2026-11-25

U07 and P09 advance together through typed shared utilities, Redux store and
preferences, author/book/edition/file presentations, and common visual/action
controls. The source has 139 fewer unchecked JS modules (991 → 852) and 255
ordinary TS/TSX modules (217 included in the strict program). Confirmed
regressions include the book overview's owning-author edit target, stale
selection/range identities, preservation of valid legacy custom filters, and
proper sort/filter state propagation through the library selectors.
US, UK, local recording and ebook identity boundaries remain explicit.

P09 adopts the validated Redux 5/Thunk 3/React Redux 9/Reselect 5 graph, ESLint
9 flat configuration, Stylelint 18 and compatible plugins, plus the
open-source FluentAssertions 7, Npgsql 10.0.4 and System.IO.Abstractions 23.1.0
managed graph. Both registry audits report zero known findings; this is not
a comprehensive native-code audit. The clean install pins React DnD's
hoist-non-react-statics declarations rather than inheriting transitive types.
The broader React 19, rule/tooling, analyzer, managed/native and strict UI
migration decisions remain explicit and staged. See the [dependency review](dependency-audit.md)
and [local validation](local-validation.md).

The roadmap remains **83 of 92 Verified**, eight In progress and one Deferred.
U07 and P09 are not closed; P01/P02/P13/P22 native ARM64, P21/P25 CI/release
acceptance and deferred P17 remain unchanged. No production deployment or
release publication is part of this slice.

### Shared forms, interaction dependencies and managed compatibility — 2026-11-25

U07 advances 87 more JS modules (852 → 765); ordinary TS/TSX rises 255 → 351,
with 321 source modules in the strict program. The next group covers forms,
modal focus/backdrop behavior, row selection/header/pager/date contracts,
settings drafts, typed collection projections and drag/drop hooks. The
selector join no longer mutates Redux book resources; stale author maps
and valid profile/sort inputs are exercised with frozen resources.

P09 replaces the legacy DnD HOCs, Popper, document-title and lazy loader,
adopts supported detail tabs and FontAwesome's React wrapper, and updates
NLog's managed graph and the OpenAPI generator. Both audits report zero
known findings; log archive naming/retention/redaction, public API
documents, desktop/touch ordering and actual keyboard navigation remain
verified. The isolated React 19 trial now passes both TS projects and
focused hosted behavior, but the final supported graph remains React 18
because older scrollbar/slider/truncation integrations and function
default/JSX migration remain. Managed/native compatibility gates remain
staged. See [local validation](local-validation.md) and the
[dependency review](dependency-audit.md).

The roadmap remains **83 of 92 Verified**, eight In progress and one
Deferred. U07/P09 remain in progress; prior release, native ARM64 and
CI gates and deferred P17 are unchanged. No release publishing or
production deployment is part of this work.
