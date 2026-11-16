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

- [ ] Capture the current synthetic upgrade, identity and failure fixtures: B01/D01.
- [ ] Record runnable build/test commands and a browser harness that does not
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
recorded below. The next code item is **P08**, bounded login-attempt lockout.
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
P09 remains the deployment blocker; the next code item is **P08**.

### Dependency advisory deployment blocker

P09 remains **Planned**. The existing [package inventory](../src/Directory.Packages.props)
contains MailKit 4.8.0, affected by the [STARTTLS advisory fixed in 4.16.0](https://github.com/jstedfast/MailKit/security/advisories/GHSA-9j88-vvj5-vhgr),
and ImageSharp 3.1.11. Six Labors documents security fixes in ImageSharp 3.2.0 and
a [valid license-key requirement for that update](https://sixlabors.com/posts/security-support-and-license-enforcement/).
These versions identify the relevant fixes, not completion of the wider P09 audit.
No packages have been upgraded and no audit suppression has been committed.
The current dependency advisories prevent deployment of this batch.

Isolated validation uses a command-line-only exception to treating NU1902/NU1903
as errors, with package auditing enabled and advisory warnings visible. This
permits test execution; it does not resolve the advisories or authorize deployment.
The linux-x64 artifact was tested as recorded above. Supported ARM work-factor
evidence and the remaining artifact gates must be recorded before those
acceptance gates are closed.

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
