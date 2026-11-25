# Local package validation

Run delivery checks locally without GitHub Actions. The checks use complete
Linux packages, fresh synthetic library homes and generated media. They do not
accept an existing application home, backup or audiobook library as input.

Python 3.10+ and the .NET SDK selected by `global.json` are required. Run host
checks on the package's native Linux architecture and libc. Docker checks are
optional and require a local Unix-socket Docker daemon and a native musl package.
All commands below run from the repository root.

## Prepare a package

Build the backend and frontend using the repository's normal prerequisites:

```sh
RestoreLockedMode=true ./build.sh --backend --frontend
./build.sh --packages --runtime linux-x64 --framework net10.0
```

The backend publishes projects serially because they share output directories.
The resulting package includes its runtime, updater, UI, platform libraries,
dependency notices and bundled MediaInfo. A bare `dotnet build` output is not a
complete delivery package. Frontend builds require the repository's Node/Yarn
setup; validation itself does not install frontend dependencies.
Preparing bundled MediaInfo may also require Docker or a matching native
builder, as described in [MediaInfo packaging](mediainfo.md). An existing
complete package can be validated without rebuilding it.

## Validate on this machine

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr
```

The default checks are:

| Check | What it exercises |
| --- | --- |
| `package` | Selected SDK/runtime patch, native architecture, executable files, updater, bundled MediaInfo checksums and dependency notices |
| `app` | HTTP startup, empty library, API-key enforcement, database creation/integrity and graceful shutdown |
| `restore` | Application-generated current backup, malformed/incompatible payload rejection, configuration-only restoration and preservation of synthetic library/settings/media |
| `media` | Published raster adapters, animated GIF, PDF/EPUB metadata, audio tags/narrators, SQLite and bundled MediaInfo execution |

The media check builds a small probe against the provided package. It runs that
probe in a private clone using the package's own self-contained runtime and
native libraries. Calibre coverage exercises the production client with a
synthetic protocol response; it does not test a running Calibre installation.

## Check client routing and download completion

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --checks acquisition
```

This explicit check loads the package's client-selection, completion, history,
tracking and cleanup services in a private package clone. It uses synthetic book
and file identities, owned SQLite-backed repository interfaces, physical source
and library files, and a fake download client. It contacts no indexer or download
client and accepts no existing application home or media library.

The proof covers tag isolation, distinct books, coherent recording parts,
incomplete imports, synchronous receipt-write failures, source-retirement order,
restarts, legacy and stale evidence, ebook formats, and persisted manual Copy
intent. Its importer boundary supplies controlled committed-file results; source
tests separately exercise the staged importer and manual-import integration.
Input package fingerprints must remain unchanged. See the
[completion policy](download-completion.md) for scope and cleanup behavior.

## Check audiobook identity preservation

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/genuine-older-audiobook-package \
  --playwright-module /path/to/installed/playwright \
  --checks identity
```

The explicit proof creates a new home using the older package, then seeds the
versioned [synthetic recording recipe](../scripts/validation/fixtures/audiobook-identities-v1.json).
SQLite seeding is the fixture boundary; login, API responses, browser pages,
restart and application-managed backup restoration use the real server. Desktop
and 390px browser checks use the explicitly supplied Playwright installation.
No existing home, private catalog, external metadata provider or real media is
accepted. A distinct older payload with the audiobook schema is required; this
is separate from the .NET 6 baseline used for runtime upgrades.

The proof checks exact identities, file ownership, coauthor credits, local
recordings, regional editions, narrations, pack and fractional positions, unknown
dates, chapters, saved credentials and explicit false/zero values. Source
regression fixtures separately cover metadata refresh and staged import failure.
Restoration rolls data back through the candidate's backup API; it does not
attempt a binary downgrade across database schemas. Candidate preparation
requires a complete passing identity receipt in addition to the other checks.

## Check provider status and accessible interactions

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --playwright-module /path/to/installed/playwright \
  --checks metadataaccessibility
```

This check uses Chromium and FFmpeg to exercise the packaged application with
a synthetic library and a loopback metadata provider. It checks completed and
cached searches, confirmed missing metadata, and rate limits. The browser
checks accessible names, keyboard activation, and focus in recording and
confirmation dialogs at desktop and phone widths.

## Check recording review and author reconciliation

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --playwright-module /path/to/installed/playwright \
  --checks libraryreview
```

The check generates audiobook files with FFmpeg and uses Chromium to review
them at desktop and phone widths. It exercises an exact recording association,
identity conflicts, stale previews and an atomic author reconciliation.
It checks the resulting recording, history and file identities after restart.

## Check interactive search and library virtualization

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --playwright-module /path/to/installed/playwright \
  --checks interactivesearch libraryvirtualization
```

Interactive search checks the real empty-search endpoint and injects synthetic
release responses and grab acknowledgements into Chromium. The library check
seeds a disposable catalog and uses the real API and views to measure rendered
rows and scroll restoration. Both exercise desktop and phone layouts. Neither
contacts a real indexer or download client, or modifies an existing library.

## Check the frontend asset and recording lifecycle contracts

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/older-audiobook-package \
  --playwright-module /path/to/installed/playwright \
  --checks assetpipeline recordinglifecycle
```

The asset proof loads the packaged login page and a recording bookmark at
both `/` and a nonempty URL base. It verifies hashed entry and lazy assets,
font and image loading, the real search worker, and the OAuth callback HTML
using a local synthetic callback (no external OAuth provider is contacted).
The recording lifecycle check requires a genuine older audiobook package. It
upgrades a synthetic catalog, generates an unmapped audiobook, edits local
metadata and creates a local author through the candidate API. Explicit pack
memberships preserve standalone regional copies and count related files once.
The browser exercises both editors, and the hosted check tests stale revision
rejection, rescan, restart and application-managed backup restoration.

The collection diagnostics proof uses a genuine older package as well:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/older-audiobook-package \
  --playwright-module /path/to/installed/playwright \
  --checks collectiondiagnostics
```

It seeds owned current/stale audio measurements, separate regional recordings,
an ebook, reviewed pack memberships and an unresolved file. Two local HTTP
servers return synthetic Readarr list contents, partial responses and failures.
The real candidate sync service and browser verify independent durable
membership, exclusion display, statistics filters and release-group evidence
preservation. The release-group DTO fixture tests an already stored evidence
record; the source and acquisition tests separately exercise actual resolution.

## Check list policies, quality ranking and provider compatibility

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/older-audiobook-package \
  --playwright-module /path/to/installed/playwright \
  --checks listlifecycle providerpolicies indexerrejectionpolicy qualityranking
```

These hosted checks upgrade disposable libraries from a genuine package before
D23–D29. The list-policy check uses owned loopback list responses to exercise
shared tags, failed/partial refreshes, manual ownership and explicit cleanup;
no deletion is permitted. The custom JSON/qBittorrent check tests real
provider requests against synthetic local servers and the saved provider
settings. The indexer policy check creates only disabled synthetic definitions;
separate repository and service tests exercise terminal rejection and retry
logic. The quality ranking proof uses real History and Blocklist pagination,
profile reorder and library/file ranks. Browser checks use desktop and phone
viewports. The fixture does not submit real downloads or contact real providers.

## Check refresh cancellation and shutdown

Run the cancellation proof against a package containing P10:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --checks cancellation
```

This check creates two synthetic homes and a loopback Audnexus server. It holds a
metadata response open, cancels a running author refresh through the command API,
and checks that the HTTP connection closes, the command becomes `Cancelled`, and
saved catalog rows and media bytes survive. A successful retry verifies that
cancellation did not populate the response cache or trigger a provider cooldown.
The second home receives SIGTERM during a held response and must stop gracefully
without changing its saved library. No external metadata provider is contacted.
This check is explicit so the default checks remain usable with older packages.

## Check authentication policy and local recovery

Use a package containing P16 and a complete, genuine pre-P16 package:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/pre-P16/package \
  --checks authpolicy
```

This explicit check creates private synthetic homes and an owned PostgreSQL 16
container with a Unix socket and no published ports. It checks Forms, Basic and
None compatibility, API-key enforcement, multiple-account settings, account
selection, credential changes and old-session rejection. The baseline creates
the SQLite library and a working Forms cookie; candidate bytes replace it at the
same owned content-root path so cookie rejection cannot result from a different
data-protection discriminator.

Recovery checks send synthetic passwords through standard input, never arguments
or environment variables. They cover stopped-home recovery, invalid selectors,
confirmation failure, live-server rejection and recovery-to-startup lock handoff.
Saved identities, other accounts, library/history/cache/media, authentication mode
and API keys must survive. Responses and logs are checked for credential canaries.
Every owned process, container and socket is cleaned up, and both input packages
are fingerprinted. No existing home or external database is accepted as input.

## Check host filtering, redirects and updater isolation

Run the explicit checks with a complete current package:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --checks accesspolicy releaseidentity
```

`accesspolicy` uses fresh synthetic homes to check local login return URLs,
configured/default host filtering, API/UI/SignalR boundaries, trusted forwarded
hosts, configuration precedence and rejected startup settings. It verifies that
manual update commands and direct standalone-updater execution refuse before
replacement while retaining saved update settings and markers. Source tests use
HTTP mocks to prove no legacy feed/download calls; the package check does not
claim to monitor every outbound connection.

`releaseidentity` reads compiled product/source metadata through the owned
server's status API and compares it with the frontend build receipt. Dirty builds
can pass this consistency check but cannot pass the release-candidate gate.
Native source builds without the canonical build metadata are also ineligible.
See [release policy](release-policy.md) for clean-checkout preparation, required
evidence, exact source archives and the unsigned, unpublished candidate boundary.
Both checks leave input packages unchanged and clean up their owned processes.

## Check the migration engine on SQLite and PostgreSQL

Use a complete pre-P12 package as the baseline:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/pre-P12/package \
  --checks datastore
```

This explicit check requires a cached native `postgres:16-alpine` image and the
same local Unix-socket Docker daemon used by the Docker checks. The PostgreSQL
container runs as the host uid, with no network, no published ports, and a private
Unix socket. It accepts no existing application home or PostgreSQL endpoint.
The candidate must contain FluentMigrator 8 and the baseline must contain the
older engine; the runner rejects the same package or a newer baseline.

The actual old binary creates synthetic SQLite and PostgreSQL libraries, then
the candidate opens those same databases. Checks retain authors, books, editions,
two recording parts, narrators, overrides, cleanup rules, settings, history,
identities, dates, defaults, indexes and constraints. A fresh PostgreSQL home also
replays the complete main/log/cache history. Historical migration 034 retains
its actual version **158**; files are not renumbered to make the test pass.

Recovery checks first create an application-generated SQLite backup and
`pg_dump` backups of all three PostgreSQL databases. An explicit failure injection
replays migration 158 against a deliberately incompatible synthetic schema; this
is labelled as failure injection rather than a legacy upgrade. It must fail,
roll back partial DDL, preserve the saved catalog, and restart after restoring the
backup. Owned containers, sockets and application processes are cleaned up, and
input package fingerprints must remain unchanged. Raw logs and backups stay in
the private evidence directory.

## Measure SQLite locking and bundled native loading

Use the explicit SQLite check for a package containing P13. An optional pre-P13 .NET 10
package measures both providers with the same synthetic workload:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/pre-P13-net10/package \
  --checks package sqlite
```

The probe uses the application's connection factory and backup implementation
inside an owned runtime clone. It measures 100 ms and 1000 ms native busy waits
with separate managed command budgets, concurrent WAL readers, atomic recording
parts, metadata/history writes, main/log/cache databases, online snapshots and
interrupted work followed by another write. Timings describe this workload;
they are not throughput or total request-latency guarantees.

Evidence includes the actual engine version/source ID, compile options and
mapped native path/checksum. The package check compares the engine bytes with
[`sqlite-native.lock.json`](../scripts/sqlite-native.lock.json), including the
RID. The new engine's glibc assets require **glibc 2.34 or newer**; musl uses its
separate asset. The application fails if the bundled engine is missing rather
than selecting host SQLite. ARM64 assets can be inspected here, but runtime
acceptance requires matching native ARM64 hardware.

## Check optional database log storage

Use a package containing P15 and an owned PostgreSQL 16 container:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --checks logstorage
```

This explicit check needs the cached `postgres:16-alpine` image and the local
Unix-socket Docker daemon. It accepts no existing home or database endpoint.
It exercises the default enabled mode, XML `LogDbEnabled=false` and the explicit
`Readarr__Log__DbEnabled=false` environment override in fresh synthetic homes.

SQLite checks cover missing and existing log databases, empty paged log browsing,
log clearing, housekeeping, backups, file/console output and reenabling. PostgreSQL
uses separate owned main/cache databases with an absent log database, then denies
connections to a previously populated log database while disabled. Both engines
must retain library/download history, cached metadata and synthetic media, and
recover the existing log/update history after reenabling. The setting takes effect
at process startup; later configuration reads do not change the registered mode.

## Check PostgreSQL connection strings

Use the explicit P14 check with a complete pre-P14 package as the baseline:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/pre-P14/package \
  --checks postgresconnections
```

The check uses an owned PostgreSQL 16 container with a private Unix socket, no
network and no published ports. It creates a SCRAM-authenticated role with a
password containing semicolons and quotes. The old binary creates component-mode
main/log/cache databases; the candidate switches that same home to full strings
without losing library identities, authentication, file parts, history, metadata
cache or media. No existing home or PostgreSQL endpoint is accepted.

Other cases cover XML/environment precedence, unrelated settings saves, the
optional log database, missing strings, mixed sources, invalid syntax/values and
owned server authentication failures. Canaries must not appear in application
logs or API resources. Saved XML and backups retain their normal configuration
contract and are excluded from output scans. The runner preserves input package
fingerprints and cleans up its application processes, container and socket.
TLS options are checked by source tests; this Unix-socket proof does not claim
TLS transport or certificate-chain integration coverage.

## Check a genuine upgrade or legacy backup

Supply a separate older **binary package**, without any saved application data:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/older/package
```

This adds `upgrade`: the old binary creates a synthetic home and its own backup,
then the candidate starts on that same home. The check preserves authors,
recordings, narrators, two file parts, root folders, history, settings, user
identities, API key and media bytes. It verifies login with both legacy and
salted passwords, allowing the candidate to strengthen password derivation.
The baseline must support salted accounts; password hashes are generated through
its real settings API rather than being pinned to one release's work factor.

To exercise restoration of an application-generated backup from before the
salted-password schema, also supply:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --legacy-runtime /path/to/pre-password-migration/package
```

An unsuitable legacy binary fails the legacy check. Without the corresponding
argument, genuine upgrade and legacy-backup coverage are reported as unrequested;
the runner does not manufacture old backups or silently claim those checks ran.

## Optional native Docker check

Prepare a musl package separately; a glibc package cannot serve as an Alpine image:

```sh
./build.sh --packages --runtime linux-musl-x64 --framework net10.0
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --docker-runtime _artifacts/linux-musl-x64/net10.0/Readarr
```

The additional `docker` check builds the actual repository Dockerfile in a
private context, runs its S6 service, checks HTTP and shutdown, and exercises the
media probe on that exact image. Build steps can download the base image,
packages and pinned MediaInfo sources. Test containers use `--network none` and
publish no host ports. Only containers and anonymous volumes owned by the run
are removed. The locally built image and private evidence remain available.

Use matching ARM64 artifacts on a native ARM64 Linux machine. The runner rejects
foreign apphosts and does not invoke QEMU or add JIT compatibility flags. Default
ARM64 startup acceptance remains open until it passes on native hardware.

## Results and focused runs

Each invocation creates a new private directory under `_temp/validation/` and
prints its path. `summary.json` records every requested check, failures,
unrequested coverage and before/after package fingerprints. Original packages
must remain unchanged. Raw logs, generated credentials, synthetic databases and
media stay in the private evidence directory and are excluded from Git.

Use `--output /existing/private/parent/new-run` for a different **new** directory.
An existing output directory, a package/output overlap, or an application-home
ancestor is rejected. Exit status is zero only when every requested check
completes successfully; failures return nonzero, and interruption returns 130
after cleanup of owned processes and containers.

Run a subset when investigating a failure:

```sh
python3 scripts/validate-local.py --runtime /path/to/package --checks package app
python3 -m unittest discover -s scripts/validation/tests -t scripts -v
```

The Python tests cover path boundaries, secret-safe failure reporting, owned
process cleanup, interruption and failure propagation. These package checks
supplement the application test suites. They do not establish PostgreSQL,
Windows/macOS or native ARM64 support from an x64 run.

## Verification record

On 2026-11-16, fresh Linux x64 and musl-x64 packages passed the combined local
command with a genuine .NET 6 baseline: package **14**, app **15**, current
restore **39**, media **42**, upgrade **27**, Docker **53**, and **3** unchanged
input-package checks: **193 passed / zero failed**. The media check includes
animated-GIF frame/timing preservation. Output paths contained spaces and a
semicolon. The Calibre response was synthetic; the Docker image and bundled
MediaInfo execution were real.

The pre-password-schema legacy backup positive path was not run because that
older binary was unavailable. Supplying an unsuitable newer baseline to that
option was checked and returned nonzero. The legacy path remains explicit in
reports and is not included in the passing count above.

The locked backend build, frontend build and both package preparations passed.
All **41 Python regression tests** passed, covering cleanup, path and reporting
boundaries. The existing six shell test-runner status cases also passed.
GitHub workflows were neither changed nor run. The running development service
and production media/appdata were unchanged. Native ARM64 and broader P21
integration acceptance remain open.

### P10 cancellation verification — 2026-11-16

After `25f0540bb` and `a13f988a1`, the fresh Linux x64 package passed
`--checks cancellation`: **38 checks**, plus **one unchanged-input check**.
The held response closed on API cancellation, the command became `Cancelled`
within five seconds, its catalog/media stayed intact, and a subsequent refresh
completed against the same loopback provider. In a second synthetic home,
SIGTERM during a held body stopped the server gracefully within ten seconds.
Incomplete responses were not cached. All **47 Python regression tests** passed.

The same candidate also repeated the six package/app/restore/upgrade/media/Docker
checks above: **193 passed, zero failed**, using the genuine .NET 6 baseline and
the actual repository Dockerfile. Both proof reports recorded the same candidate
fingerprint and unchanged input packages. Locked backend, frontend and native
x64/musl-x64 package builds passed. The development service was not restarted;
GitHub workflows were neither changed nor run.

### P11 connection verification — 2026-11-16

After `2512159c9`, fresh Linux x64 and musl-x64 packages passed a single run with
all seven checks selected:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/older/net6/package \
  --docker-runtime _artifacts/linux-musl-x64/net10.0/Readarr \
  --checks package app restore upgrade media cancellation docker
```

**231 checks passed, zero failed**: package **14**, app **15**, current restore
**39**, genuine .NET 6 upgrade **27**, media **42**, cancellation **38**, Docker
**53**, and **three unchanged-input checks**. Backend/frontend and both package
builds passed. The run used synthetic libraries and metadata, the actual
Dockerfile, and no published container ports. The running development service,
production appdata/media and GitHub workflows were unchanged. The new connection
race, DNS, proxy/TLS and pool-renewal unit/wire evidence is recorded under
[P11](sonarr-v5-porting-backlog.md#p11); this package run does not simulate all
network topologies or establish native ARM64 acceptance.

### P12 migration verification — 2026-11-16

The candidate from `bb4cd939e` passed the new `datastore` proof against a retained
pre-P12 package: **37 checks**, plus **two unchanged-input checks**, with zero
failures. Actual old/new binaries created/upgraded synthetic SQLite and
PostgreSQL 16.13 libraries, retaining saved catalog data, recording parts, settings,
history, identities, dates and schema. Injected partial-DDL failures rolled back;
both application-generated SQLite and PostgreSQL point-in-time backups recovered
and restarted successfully. Restore comparison normalizes PostgreSQL's internal
dropped-column position gaps while preserving visible column order and every
type/default/nullability/index/constraint assertion.

The same candidate also passed the seven-check command from the P11 record:
**231 checks**, with a genuine .NET 6 baseline and native musl-x64 Docker image.
Both reports recorded the same candidate fingerprint. Locked builds and package
preparation passed, as did **63 Python regression tests**, including owned
PostgreSQL cleanup when diagnostic collection fails. All containers, sockets and
processes owned by the proof were stopped. The development service, production
data and GitHub workflows were unchanged. Broader migration unit evidence and
intentional automatic-rebuild rejection cases are recorded under
[P12](sonarr-v5-porting-backlog.md#p12).


### P13 verification, 2026-11-17

Implementation `238cc9a0e` passed the explicit SQLite comparison against a
preserved genuine pre-P13 .NET 10 package: **98 probe checks plus two unchanged
input-package checks**, zero failures. The candidate used the pinned native
SQLite 3.53.4 bytes; the missing-engine clone failed without using host SQLite.
Both busy-timeout policies passed controlled contention. The supported connection
interrupt rolled back unfinished work and allowed subsequent writes; the probe
joins native work before disposing its resources.

Fresh glibc/musl-x64 delivery packages passed **141 package/.NET 6 upgrade/media/
Docker checks**. Separately, **131 app/restore/cancellation/datastore checks**
passed with the genuine pre-P12 migration-engine baseline and owned PostgreSQL 16.
The musl-ARM64 package passed **17 static package/input checks**; no native ARM64
execution was claimed. All **73 Python regression tests** passed. Source suites
passed Common **768**, Core **3,697**, API **8** and Host **65**, retaining existing
skips, plus **23** PostgreSQL migration cases. Every owned container, private
socket and application process was cleaned up. No live home, production media,
running development service or GitHub workflow was changed. P13 remains open for
native ARM64 runtime acceptance and does not establish all P22 platforms.


### P15 verification, 2026-11-17

Implementation `9c36a3bc9` passed **63 optional-log checks** against a fresh
package, including unchanged inputs and cleanup. SQLite/PostgreSQL default,
disabled and reenabled modes preserve the library, download history, metadata
cache, media and prior log/update rows. Disabled mode succeeds with inaccessible
log storage; file/console output and backups continue. Reenabling produces a new
SQL event. The genuine pre-P15 package fails the disabled-log creation assertion,
as expected. All **81 Python regression tests** passed.

The canonical backend/frontend and both x64 package preparations passed. The
usual package/app/restore/.NET 6 upgrade/media/cancellation/Docker command passed
**233 checks**. Source suites passed Core **3,704**, Common **774**, API **8** and
Host **74**, with existing skips retained, plus **23 PostgreSQL migration cases**.
No GitHub workflow, running development service or production data was changed.


### P14 verification, 2026-11-17

Implementation `787a593e2` passed **115 PostgreSQL connection-string checks**,
including input fingerprints and cleanup. Real SCRAM authentication exercises
quoted/semicolon passwords. A genuine pre-P14 binary creates component-mode
libraries, then full strings reopen those same databases with schema, identities,
authentication, history, cache and media intact. XML/env precedence, unrelated
saves, optional logs, invalid configurations and secret-free server errors pass.
The previous package fails the expected SQLite-fallback check. TLS flags are
source-tested; transport integration is outside the Unix-socket proof.

The existing optional-log proof passes **63 checks**, the SQLite native/locking
regression passes **52 checks**, and the normal delivery command passes **233
checks**. All **90 Python tests** pass.
Source suites pass Core **3,756**, Common **828**, API **13**, Host **74**, and
**23 PostgreSQL migration cases**, retaining existing platform skips. An existing
loopback cancellation teardown returned Linux EINVAL once; the unchanged Common
suite passed on a full rerun. Canonical locked backend/frontend builds and both
x64 packages passed. No live home, production data, running development service
or GitHub workflow was changed.


### P16 verification, 2026-11-18

Implementation `0b6c53444` passes **111 authentication-policy checks** with a
genuine pre-P16 P14 package and private SQLite/PostgreSQL 16 homes. Baseline and
candidate run at the same owned content-root path with preserved data-protection
keys; pre-stamp cookies require fresh login. The genuine previous binary fails
that exact assertion. Forms/Basic UI credentials still cannot replace the API
key. Account settings require unambiguous selection, authenticated users edit
their own credentials, and recovery targets one existing identity. Both real
recovery-to-startup handoffs pass. Rejected live homes, selectors and confirmations do not change credentials; logs and responses do not
contain password canaries. Account identities, other users, configuration, API
keys, main schema, library/history/cache and media survive. All owned processes,
containers and Unix sockets are removed; both input packages remain unchanged.

Canonical locked backend/frontend builds and glibc/musl-x64 packages pass. Source
suites pass Core **3,776**, Common **828**, API **16**, Host **110**, plus **44**
authentication integration cases and **23** PostgreSQL migration cases, with
existing skips retained. Source cases additionally verify missing/old/future
database rejection without creation or migration and prompt timeout rejection.
An existing Linux socket-accept teardown race occurred
in Common; `ad11a435b` joins cancelled accepts before closing their native socket,
and the full suite passes afterward. Python validation passes **101 tests**.
Desktop and 390px phone Chromium smoke checks verify account choices, populated
username/masked password, cleared confirmation on switching, and a saved targeted
rename while preserving other accounts and credential encodings.

Normal package/app/restore/.NET 6 upgrade/media/cancellation/Docker delivery passes
**233 checks**, including unchanged inputs. P14 connection-string and P15 optional
log regressions pass **115** and **63** checks respectively. Private evidence is
under `_temp/p16-auth-20261118/`: `final-authpolicy`,
`final-authpolicy-negative`, `final-suites`, `final-delivery`,
`final-postgresconnections`, `final-logstorage`, and `browser-final-pass`.
The browser evidence includes a phone screenshot with its synthetic API key
masked. No existing home, running development service, production media or GitHub
workflow was changed. P02 ARM work-factor and P13 native ARM64 acceptance remain
open; these x64 results do not establish the complete platform/release matrix.


### P24, P04 and local P25 verification, 2026-11-18

P24 `9dda441fd`, P04 `c03ea78e2`, updater/build identity `736ffe104` and local
candidate tooling `07794abd6` are verified with clean artifact source
`0015c349f`. Both x64 package preparations and canonical locked backend/frontend
builds pass. The backend and frontend record the same Git commit/tree/branch
with `buildDirty=false`; direct source builds with missing metadata or dirty
builds cannot satisfy candidate preparation.

The first older-home upgrade exposed an unintended XML write while reading the
new empty Allowed Hosts default. `0015c349f` changes it to a non-persistent read
and adds exact-byte preservation coverage. A Common-only rerun initially lacked
the platform test assembly after packaging; rerunning Core first restored the
required test layout. All seven ordered suites then pass: Core **3,856**, Common
**836**, API **16**, Host **181**, authentication integration **54**, PostgreSQL
migration **23** and standalone updater **20**: **4,986 passed**, with **97 existing
skips** retained. Python validation passes **131 tests** and changed frontend
files pass ESLint; only the existing Browserslist age notice remains.

The final delivery command passes **301 checks**: package, app, restore, genuine
.NET 6 upgrade, media, cancellation, actual repository Docker/S6, access policy
and compiled/frontend identity, plus unchanged inputs. The separate same-content-
root pre-P16 P14 authentication proof passes **111 checks**, retaining the
account/session/recovery, SQLite/PostgreSQL and saved-data boundaries. The
pre-P04 package fails the intended unknown-host assertion as a negative proof.
Source HTTP mocks prove that disabled updater paths make no legacy feed/download
calls; the package proof does not claim outbound network monitoring.

Chromium desktop and 390px phone smoke checks pass **20 UI/API assertions plus
seven preservation/cleanup guards**. They exercise advanced Allowed Hosts,
actionable invalid-save 400 feedback without config writes, normalized valid
saves and restart notices, disabled-update explanations and hidden install
controls. Credentials and API keys remain unchanged. Screenshots mask the
synthetic API key; every owned browser/server process stops and package bytes
remain unchanged. This is a focused browser check, not a full accessibility audit.

The actual release CLI stages and verifies one native Linux x64/.NET 10 `develop`
candidate at version `10.0.0.36421`, bound to source `0015c349f`, the exact matching
GPL source archive and the two passing reports. **41 checks** reject failed,
incomplete or foreign receipts; wrong product/channel/commit/tree/branch/version/
RID/runtime/schema; altered manifests, payload bytes, permissions, evidence or
source archives; and unexpected bundle files. The controlled mutations are
restored and final verification passes. Source and canonical package remain
unchanged. The candidate stays unsigned, `publishable=false` and
`builtInUpdatesEnabled=false`; no installation, feed activation or publication
is authorized by that receipt.

Private evidence is under `_temp/p24-p04-p25-20261118/`:
`source-verified`, `final-delivery-v2`, `authpolicy-final-v2`, `browser-final-2`,
`release-candidate` and `release-candidate-proof`. The candidate is deliberately
bound to the exact `0015c349f` checkout; later documentation-only commits do not
relabel its source identity. Verification requires that matching clean checkout.
Owned applications, PostgreSQL containers/sockets and Docker test containers are
cleaned up. The running development service and production appdata/media remain
untouched. No GitHub workflow, push, image publication or production update was
performed. P17 is deferred at the user's request. P25 feed/signing/publication
and supported-platform gates, P02/P13 native ARM acceptance and the broader
release matrix remain open.


### D03 and D04 source verification, 2026-11-18

Client-tag isolation is committed in `f7ee205d8`; completion, source-retention and
cleanup changes are committed in `ef8beac21`. Offline Core passes **4,075 cases**
with **55 existing skips**. Common **836**, API **16**, Host **181**, PostgreSQL
migration **23**, authentication integration **54** and updater **20** also pass:
**5,205 source cases**, retaining **97 existing skips** in total. PostgreSQL uses
an owned temporary cluster and socket; external metadata fixtures are excluded
from the offline Core run as in the preceding validation.

The tooling passes **141 Python tests**. The explicit `acquisition` check is now
required by local candidate preparation, including rejection of an otherwise
passing report that omits this proof. Its published importer boundary is
controlled committed-file results; the source suites exercise the actual staged
importer, manual selection, rollback and physical library-alias behavior.

A subsequent nonmovable-source cleanup follow-up passes **29 focused cases**,
including two new regressions for removable client tasks whose files cannot be
moved. Both event paths remove the task with `deleteData=false`.


Final packages were built with normal package permissions from clean source
**`3aeb8c56b`**, version **`10.0.0.39353`**, on
`fix/sonarr-v5-foundation`. Backend and frontend provenance agree on commit, tree,
branch and `buildDirty=false`. glibc and musl packages contain .NET **10.0.15**.
The final delivery command passes **433 checks**, including **132 published
acquisition checks**, **53 actual musl Docker checks**, and unchanged-input guards.
The separate genuine pre-P16 authentication proof passes **111 checks**.

Actual candidate preparation and verification pass **43 checks**, including
rejection of otherwise passing evidence that omits `acquisition`, incorrect
identity, altered bytes/modes, source-archive tampering and unexpected bundle
files. The Linux x64 `develop` candidate remains unsigned, unpublished and
`publishable=false`, with in-app updates disabled. Its GPL source archive is
exactly `3aeb8c56b`; this evidence record does not relabel the artifact after a
documentation-only commit.

Private local evidence is retained under `_temp/d03-d04-20261118/`:

- `source-core-final/tests/` and `source-verified/tests/`: source TRX reports.
- `tests/cleanup-final.trx`: final cleanup follow-up.
- `python-tests-final.log`: Python discovery.
- `build-correct-permissions/results.json`: clean backend, frontend and packages.
- `final-delivery-correct-permissions/summary.json`: final delivery receipts.
- `authpolicy-final/summary.json`: final authentication receipts.
- `release-candidate-proof/summary.json`: actual CLI rejection/acceptance proof.
- `release-candidate/`: complete local candidate, source archive and manifest.

Checks use owned synthetic homes, files, databases, clients and containers.
They preserve the input packages and clean up owned processes/containers. The
running development service remains PID **3980350** in its separate runtime.
No running development/production home, media, Unraid share or GitHub workflow
was changed. P17 remains deferred; native ARM64 and broader release gates remain
separate work.


### D01, B01, D02 and D05 verification, 2026-11-19

Preservation and the reusable recipe/harness are committed in `127f0882f`.
Rejection codes and custom-format upgrade guards are committed in `c331b0bea`.
The full offline source run passes Core **4,145**, Common **836**, API **22**,
Host **181**, PostgreSQL migration **23**, authentication integration **54** and
updater **20**: **5,281 passing cases**, retaining **94 existing skips**. The
usual external metadata fixtures remain excluded from offline Core; PostgreSQL
uses an owned temporary cluster/socket. Python discovery passes **148 tests**.

Normal-permission glibc and musl packages build from clean `c331b0bea`, version
`10.0.0.34076`, with matching frontend/backend provenance and .NET **10.0.15**.
Delivery passes **441 checks**, including **140 published acquisition checks**
and **53 actual musl Docker checks**. A separate genuine pre-P16 proof passes
**111 authentication checks**. The new identity proof uses the complete prework
`3aeb8c56b` package to create a genuine older synthetic home and passes **262
checks**, including desktop/phone login, API identity/ownership, restart and
application-managed baseline-data restoration. Its declared SQLite recipe
boundary and separate source refresh/import coverage are documented in the
[preservation contract](identity-preservation.md).

Actual candidate preparation and verification pass **45 checks**, including
rejection when acquisition or identity evidence is omitted, incorrect identities,
changed bytes/modes, source archive/evidence tampering and unexpected bundle
files. The Linux x64 `develop` candidate binds exact source `c331b0bea`, remains
unsigned and unpublished, and has in-app updates disabled. This later evidence
commit does not relabel the compiled source or its GPL archive.

Private receipts live under `_temp/d01-b01-d02-d05-20261119/`:

- `source-final/tests/`: all seven source suite TRX reports.
- `python-verified.log`: Python discovery.
- `build/results.json`: clean backend, frontend and both packages.
- `final-delivery/summary.json`: complete delivery results.
- `authpolicy-final/summary.json`: authentication and input-preservation results.
- `identity-final/summary.json`: synthetic upgrade/browser/restore results; owned
  desktop/phone screenshots are under `identity-final/identity/browser/`.
- `release-candidate-proof/summary.json`: actual CLI acceptance/rejection proof.
- `release-candidate/`: complete candidate, source archive, manifest and evidence.

Input packages remain unchanged; owned servers and containers are cleaned up.
The separate development runtime remains PID **3980350**. No production/dev home,
real media, Unraid data or GitHub workflow was changed. P17 stays deferred; broader
platform, signing/publication and release-feed work retain their prior status.


### D06, D07 and D11 verification, 2026-11-19

File classification/policy is committed in `e13f2ebd0`, client inventory in
`7cbb3b78e`, blocked recovery in `fd35e9d11`, and the reusable browser proof's
rendering waits in `faf8684c1`. Final source verification passes Core **4,303**,
Common **836**, API **30**, Host **181**, PostgreSQL migration **23**,
authentication integration **54** and updater **20**: **5,447 cases**, with
**94 existing skips**. The Core and Host follow-ups verify the optional-clock
construction fix discovered by the full run; the original failure receipts are
retained separately. Offline metadata exclusions and the owned PostgreSQL
cluster/socket follow the preceding records. Python discovery passes **148
tests**; the queue renderer passes **2 Node tests** and targeted frontend ESLint.

Clean glibc/musl packages were built from **`faf8684c1`**, version
**`10.0.0.36335`**, with .NET **10.0.15** and matching frontend/backend commit,
tree, branch and `buildDirty=false`. Final delivery passes **466 checks**,
including **165 acquisition checks** and **53 actual musl Docker checks**.
Separate proofs pass **111 authentication** and **262 identity checks**, using
genuine appropriate older packages. The owned acquisition fixture uses actual
classification, diagnostics, retry, history and completion services with physical
synthetic files and SQLite-backed repository interfaces; its importer and client
boundaries are controlled, with real importer/adapter coverage in source tests.

The settings browser proof passes **40 checks** through actual Forms login and
Media Management at desktop and 390px phone width: advanced-field visibility,
normalization/save, invalid-save preservation and feedback, explicit clear,
reload, other-setting preservation and no external browser requests. Its reusable
script is [rejected-extensions-browser.cjs](../scripts/validation/rejected-extensions-browser.cjs).
It accepts only the supplied owned loopback fixture and uses stdin for synthetic
credentials/key; browser/server cleanup and package fingerprints are checked.
Four screenshots retain valid and invalid states at both widths.

Actual candidate preparation and verification pass **45 checks** against the
final receipts, including missing acquisition/identity evidence, wrong identity,
altered payload/evidence/source bytes or modes and unexpected bundle files.
The Linux x64 `develop` candidate remains unsigned, unpublished and
`publishable=false`, with built-in updates disabled. Its source archive is
exactly `faf8684c1`; this evidence-only commit does not change artifact identity.

Private receipts live under `_temp/d06-d07-d11-20261119/`:

- `source-core-verified/tests/`: final Core TRX; `tests/host-verified.trx`: Host.
- `source-final/tests/`: Common, API, PostgreSQL, authentication and updater TRX.
- `python-final.log`, `queue-ui-tests.log`, `frontend-lint.log`: tooling/UI checks.
- `build-verified/results.json`: clean backend, frontend and package results.
- `delivery-verified/summary.json`: final package delivery checks.
- `authpolicy-verified/summary.json`, `identity-verified/summary.json`: separate
  authentication and synthetic identity/upgrade/browser/restore gates.
- `policy-ui-verified/summary.json`: final settings proof and artifact fingerprint;
  `policy-ui-verified/screenshots/`: desktop/phone valid/invalid screenshots.
- `release-candidate-proof/summary.json`: final actual candidate CLI proof.
- `release-candidate/`: complete package, GPL source archive, manifest and evidence.

Input package preservation and owned process/container cleanup pass. The separate
development runtime remains PID **3980350**. No running dev/production home,
real media, Unraid data or GitHub workflow was changed. P17 remains deferred;
D08 automation and broader release/feed/platform gates retain their prior status.


## D10, D09 and D12 verification (2026-11-19)

Clean source `fcf7e589b`, version `10.0.0.38144`, implements client-scoped
tracking/reconciliation, safe pre-submission failover, durable uncertain-submission
holds and once-per-blocked-episode notification attempts. Only the pinned Sonarr
v5 baseline `c018578e4b79ee048575c02d7a8aa2b929e4b837` informed this adaptation.
No GitHub workflows, production servers, real client adds or notification
transports were used.

Validation against this source:

- **5,599 source cases passed**, with **94 existing skips**. The Core suite uses
  the documented offline filter and a new owned PostgreSQL cluster; the Common,
  API, Host, PostgreSQL migration, authentication integration and Updater suites
  also pass. Legacy synthetic fixtures now supply scoped history and explicitly
  identify successful client polls without weakening stale-client assertions.
- **148 Python tooling tests**, **42 frontend Node tests** and targeted ESLint
  pass. The frontend suite includes blocked/stale queue rendering and its existing
  library-editor test has the missing UI boundary stub restored.
- **552 delivery checks** pass, including **251 acquisition checks** and
  **53 actual musl Docker checks**. Both clean glibc and musl packages build with
  .NET 10.0.15, and compiled/frontend provenance agrees on the exact clean source.
- **111 authentication checks**, **262 identity-preservation
  checks**, **30 queue review browser checks** and **45 actual
  candidate CLI checks** pass. Input package hashes and owned-process cleanup
  checks remain valid. The candidate is unsigned and unpublished.

The acquisition proof runs the published services using owned SQLite-backed
repository interfaces, physical synthetic source/destination files, controlled
import results and fake clients/transports. It exercises same-ID jobs on different
clients, complete/failed/lazy/partial polls, stale cleanup, explicit routing,
release errors, exact submitted-hash reconciliation, unresolved holds across
restart/concurrent attempts, explicit acknowledgement and post-add bookkeeping
failure. Notification checks cover durable deduplication, changed diagnostics,
separate clients, recovery only after source retirement, provider opt-ins and
recording/edition identity. These are service/integration boundaries, not live
protocol tests for every download adapter.

`submission-holds-browser.cjs` runs only against an owned loopback fixture using
synthetic credentials on stdin. On desktop and at 390px it verifies authentication,
wrong-client refusal, explicit review confirmation, cancel preservation, scoped
acknowledgement, reload persistence, another client's retained hold, overflow and
browser errors. Screenshots contain synthetic data only.

Operational API notes: queue and manual-import resources carry numeric
`downloadClientId`; ambiguous legacy job IDs fail before mutation. External
`DownloadedBooksScan` commands retain their legacy string `downloadClientId`
(job ID) and accept optional numeric `clientId` for ownership. Submission holds
are listed at `GET /api/v1/downloadsubmission`; clearing uses
`POST /api/v1/downloadsubmission/acknowledge` with the exact `attemptId`, positive
`downloadClientId` and `clientReviewed: true`, following client review. Normal
API authentication and request-header protections apply. Clearing a hold only
permits future grabs; it performs no client removal/restart.

Known semantics: failover requires a proven pre-add readiness failure. Adapters
with static readiness checks may conservatively create a review hold when an
outage is discovered during add. Notification markers record at-most-once
attempts, so a provider failure/crash after marking can lose delivery. D08
rejection-directed blocklist/search automation, P17 and broader signing/feed or
platform gates retain their prior status.

Private local evidence is under `_temp/d09-d10-d12-20261119/`:
`source-results.json`, `source-final/`, `source-core-verified/`, `api-verified/`,
`python.log`, `frontend-tests.log`, `eslint.log`, `build-verified/`,
`delivery-verified/summary.json`, `authpolicy-verified/summary.json`,
`identity-verified/summary.json`, `submission-ui/summary.json` (and desktop/phone
screenshots), and `release-candidate-proof/summary.json`. The earlier mixed
bootstrap proof is smoke evidence only; the clean delivery above is authoritative.
The running development service remains on its original process/runtime.

## D13, D14, D21 language parsing and D17 verification (2026-11-20)

Source `e191326ba` produced version `10.0.0.38747`. The glibc and musl packages
carry matching clean provenance. The Sonarr comparison uses only the pinned
v5 source `c018578e4b79ee048575c02d7a8aa2b929e4b837`.

- **5,687 source tests** passed with **94 existing skips**, including
  SQLite and PostgreSQL migration cases. **148 Python tooling tests** and **42
  frontend Node tests** passed, as did targeted ESLint.
- **569 delivery checks** passed, including **268 acquisition checks** and
  **53 musl Docker checks**. The acquisition probe uses synthetic files, owned
  SQLite repository fixtures, and controlled client and filesystem responses.
  The new checks exercise the score threshold at its boundary, language scoring,
  and exact and insufficient space budgets through the pre-add path.
- **41 datastore checks** passed using SQLite and PostgreSQL 16. The
  retained pre-P12 binary created the original schemas and data. The new package
  applied migrations 159 and 160 while preserving the saved catalog and the
  original migration identities, including historical 158. Existing profiles
  gained a zero threshold and existing files gained nullable language evidence.
- **60 settings browser and API checks** passed at desktop and 390px.
  An owned home from the previous Bookshelf build verifies the profile default,
  score save, omission and explicit reset, language condition rendering, and
  space check save, omission and explicit disable.
- **112 authentication checks** and **262 identity preservation
  checks** passed.

D17 uses the library staging requirement when client output cannot be verified.
Reported local output on the same mount adds a second copy to the estimate. The
batch estimate creates no storage reservation. The import check still applies.
A directory left behind by an unavailable mount can resolve to its parent
filesystem unless the operating system reports the mount failure.
D14's Selected Edition Language uses the assigned edition from Bookshelf and
requires usable language evidence. Bookshelf has no authoritative work-level
original-language metadata.

Local evidence is under `_temp/d13-d14-d17-20261120/`. It includes the source
suites, proof summaries, synthetic desktop and phone screenshots, and both package builds. The mixed bootstrap run is a smoke check. The clean
packaged delivery is the artifact used for verification.

## D15, D16 and B04 verification (2026-11-20)

Clean source `9bc7dc65a` produced version `10.0.0.39747`. The glibc and musl
packages carry matching source provenance. The Sonarr comparison remains pinned
to v5 `c018578e4b79ee048575c02d7a8aa2b929e4b837`.

- **5,740 .NET tests** passed with **94 existing skips**, including
  SQLite and PostgreSQL migration cases. **148 Python tests**, **42 frontend
  Node tests**, and targeted ESLint passed.
- **589 delivery checks** passed, including **288 acquisition
  checks** and **53 musl Docker checks**. The packaged acquisition probe covers
  long audiobooks, ebook limits, unknown duration, grouped settings, quality
  precedence, and size tie-breaking. Audio coverage compares structured indexer
  evidence, measured files, scoped history and individual stream characteristics.
- **41 datastore checks**, **112 authentication checks**, and
  **262 identity preservation checks** passed. Upgrading an owned
  SQLite or PostgreSQL home retains previous data. Migration 161 adds a nullable
  audio snapshot field for blocklist rows. Size fields are stored in each
  profile's existing quality data.
- **34 desktop and phone browser/API checks** passed. An owned home
  created by the previous build verifies legacy profiles, per-quality audio and
  ebook units, decimal input, saved values on old API requests, invalid partial
  updates, and intentional clears. The custom format editor displays the saved
  xHE-AAC condition and the codec API lists USAC and Atmos separately.

Use **Settings → Profiles** to edit a quality profile and open **Release Size
Rules**. Blank or zero disables an individual rule. Audio size checks need a
trusted duration. If a candidate has no usable measured target, it retains the
fallback ordering at the size tie-break. The global indexer size cap remains a
separate check. Use **Settings → Custom Formats** to choose Audio Codec, Audio
Channels, Audio Bitrate Mode, Audio Sample Rate, or Narrator. These use structured
metadata and file measurements. A title alone supplies no audio confirmation.

Local evidence is under `_temp/d15-d16-b04-20261120/`, including source and
package results, isolated migration and acquisition receipts, and synthetic
screenshots. The mixed bootstrap run is a smoke check. The clean package is the
artifact used for delivery verification.

## D19, B05 and D26 verification (2026-11-20)

Clean source `f9ad59d0f` produced version `10.0.0.40681` on .NET 10.0.15.
Both Linux x64 packages carry matching source provenance. The Sonarr
comparison remains pinned to v5 `c018578e4b79ee048575c02d7a8aa2b929e4b837`.

- **5,768 .NET tests** passed with **94 existing skips**, including SQLite and
  PostgreSQL migration cases. **148 Python tooling tests**, **43 frontend Node
  tests**, and targeted ESLint passed.
- **612 delivery checks** passed, including **305 acquisition checks**, **45
  native media checks**, and **56 musl Docker checks**. The bundled MediaInfo
  CLI analyzed a synthetic WAV and saved the current analysis revision. A
  timestamp change invalidated that evidence. The acquisition probe exercised
  the hourly batch limit, retry delay, file changes during analysis, and
  retention of useful stream measurements.
- The acquisition probe exercised partial imports, failed receipt writes,
  opt-in, durable attempt recording, exact multipart identities and safe
  filenames. Restart retained the completion and suppressed a duplicate
  attempt. The synthetic probe uses an owned SQLite store and a fake webhook
  transport.
- **26 browser and API checks** passed at desktop and 390px. A loopback receiver
  accepted test events from the packaged UI. The saved setting survived an
  older API update that omitted it, and an explicit false turned it off.
- **41 datastore checks**, **112 authentication checks**, and **262 identity
  preservation checks** passed. Both database backends retained the previous
  schema and data.

D19 regression cases exercise saved cutoff and size changes, client routing,
original delay and work identity, and targeted pending release removal. Legacy
rows carry their original author association and resolve work using their
stored release metadata.

The completion webhook has at-most-once attempt semantics. A crash or provider
failure after the saved attempt can leave delivery incomplete. See the
[webhook contract](download-completion.md#verified-import-completion-webhooks)
and [MediaInfo behavior](mediainfo.md#reading-media-details).

Local evidence is under `_temp/d19-b05-d26-20261120/`, including package
summaries, owned source suites, synthetic desktop and phone screenshots, and
proof of unchanged package inputs.

## B07, U01 and U02 verification (2026-11-21)

Clean source `e53d0e4c5` produced version `10.0.0.19884` on .NET 10.0.15.
The glibc and musl Linux x64 packages carry matching source provenance.

- **5,771 .NET tests** passed with **94 existing skips**, including the SQLite
  and PostgreSQL migration suites. **148 Python tooling tests** and **49
  frontend Node tests** passed. Targeted ESLint and Stylelint passed.
- **69 provider and accessibility checks** passed, plus the unchanged package
  input check. The owned provider exercised complete and cached searches, a
  confirmed missing recording, and a rate limit. The authenticated status API
  reported the outcomes and preserved the last success time. Reading status
  generated no additional provider requests.
- **47 of those checks** exercised the packaged browser interface at desktop
  and 390px in Chromium with iPhone emulation. They covered the provider cards,
  named controls, Enter and Space, local-recording preview, safe confirmation,
  nested Escape, focus containment and return, and backdrop dragging. A source
  regression covers iOS scroll restoration when dialogs close out of order.
- **612 delivery checks** passed, including **305 acquisition checks** and
  **56 musl Docker checks**. **262 identity**, **112 authentication**, **41
  datastore**, and **26 webhook browser/API checks** also passed.

Provider tests cover timeouts, server failures, cancellation, incomplete and
repeated pages, sparse metadata, recovery and conflicting author credits.
Observations represent the latest operations since startup. The status panel
refreshes the local observations every 30 seconds while open.

Local evidence is under `_temp/b07-u01-u02-20261121/`, including the source
results, package summaries, and synthetic desktop and phone screenshots.

## B08, B02 and U04 verification (2026-11-21)

Clean source `4dbb32991` produced version `10.0.0.22035` on .NET 10.0.15.
Both Linux x64 packages carry matching source provenance. The source suites
ran against the backend in `6f7a20fdc`. The next commit adjusts the author
reconciliation dialog's layout on phone screens.

- **5,793 .NET test executions** passed with **94 existing skips**. The SQLite
  and PostgreSQL suites exercised recording association, stale previews,
  author references, ambiguous identities, and transactional rollback.
  **148 Python tooling tests** and **53 frontend Node tests** passed.
- **59 library review checks** passed, including **24 browser checks** at
  desktop and phone widths. The proof exercised conflicting and region-specific
  evidence, stale evidence refresh, exact association, immediate book file
  counts, reviewed author reconciliation, and canonical navigation. Restart
  preserved the associations, series links, file IDs, bytes, history, and saved
  author correction.
- **83 provider and accessibility checks** passed. Browser checks covered label
  focus, native select and password names, path suggestions, and links from
  root-folder validation and local-recording help to their respective fields.
  A relative root-folder path receives a field validation error.
- **612 delivery checks**, **262 identity checks**, **112 authentication
  checks**, **41 datastore checks**, and **26 webhook browser/API checks** passed.

In-place association requires an unchanged file in the author folder and a
single eligible, unoccupied local recording. Reconciliation requires a unique
same-marketplace canonical identity and equivalent author folders. Related
active downloads and unresolved submissions block reconciliation. When
settings differ, the review requires confirmation of the canonical settings.

Local evidence is under `_temp/b08-b02-u04-20261121/`, including source
results, package summaries, and synthetic desktop and phone screenshots.

## U03, U05 and U06 verification (2026-11-21)

Clean source `3f21f7b6a` produced the glibc and musl Linux x64 packages.
Both packages carry the same source provenance. The menus and action
feedback are in `6bec254fa`, and the React 18.3.1 upgrade is in its own
commit.

- **112 provider and accessibility checks** and **62 library review
  checks** passed on both the React 17 baseline and the React 18 package.
  They covered menu selection, Tab order, focus restoration, phone
  overflow actions, nested dialogs, a synthetic local recording save,
  column dragging, and one refresh of authors, commands and the active
  page after SignalR reconnect.
- **46 webhook and clipboard browser/API checks** passed on both React
  versions. The browser exercised a real loopback Test and provider save,
  controlled warning and error responses, and controlled clipboard
  success and failure.
- **612 delivery checks** passed, including **305 acquisition checks** and
  **56 musl Docker checks**. **262 identity**, **112 authentication**, and
  **41 datastore checks** passed.
- **5,793 .NET test executions** passed with **94 existing skips**.
  **148 Python tooling tests** and **61 frontend Node tests** passed.
  The changed frontend source passed scoped ESLint.

Browser checks ran in Chromium at desktop width and with iPhone 13
emulation. The 640×480 CSS-pixel desktop viewport exercises the layout
available at 200% zoom. A separate placement check moved the View trigger
to the bottom edge and confirmed that its popup opened above it.

React 18 development checks reported zero legacy-render warnings.
Function-component `defaultProps` warnings are tracked under U07.
The installed `react-virtualized` WindowScroller still reported one
`findDOMNode` warning per layout, tracked under U21. The locked install
also reported legacy peer ranges in dependencies including the router,
Popper, drag-and-drop and virtualizer.

Local evidence is under `_temp/u03-u05-u06-20261121/`, including builds,
source results, package summaries and screenshots from synthetic homes.

## U07, U08 and U09 verification (2026-11-21)

Clean source `7ed24552a` produced the glibc and musl Linux x64 packages
with matching source provenance. The implementation commits are
`23d5321c3` for type checking, `b8a495d95` for Fetch, and `7ed24552a` for Query.

- Fresh frozen Yarn install, standalone typecheck, full frontend lint, and
  **75 frontend tests** passed on Node **20.11.1**. The frontend tests also
  passed on Node 24.22.3, and **148 Python tooling tests** passed.
- The production build passed CSS declaration verification. The same check
  rejected a deliberately stale `BookRow.css.d.ts` in a disposable worktree.
- The Fetch and Query browser runs each passed **125 provider and
  accessibility checks**. The new cases covered the URL-base-prefixed
  authenticated request, invalid data, HTTP and expired-authentication
  errors, retained observations, cancellation on navigation, and recovery
  after returning to Status.
- Query tests used two observers of the production query options to verify
  request sharing, cancellation when the last observer leaves, data retention
  on background failure, and isolation from a prior API root's late result.
- The clean packages passed **612 delivery checks**, including **305
  acquisition checks** and **56 musl Docker checks**. **62 library review**,
  **262 identity**, **112 authentication**, **41 datastore**, and **46
  webhook and clipboard checks** also passed.

The TypeScript compiler follows the pinned Sonarr v5 version, 5.9.3.
The TypeScript ESLint pair uses 8.55.0 for compatibility with that compiler
and the repository's pinned Node version. Strict checking covers the new
identity and API slice. The existing mixed project continues to cover other
TypeScript files.

Local evidence is under `_temp/u07-u08-u09-20261121/`, including package
summaries, browser reports, and the CSS declaration check.

## U10 and U11 History verification (2026-11-21)

Clean source `a7c852254` produced matching glibc and musl Linux x64 packages.
`57652fae3` contains the History pagination slice, and `a7c852254` adds
SignalR-driven refresh and reconnect recovery.

- Standalone typecheck, frontend lint, and **80 frontend tests** passed on
  Node **20.11.1**. **148 Python tooling tests** passed.
- **47 focused Core tests** and **67 API tests** passed. The new tests cover
  successful writes, failed writes, orphan cleanup, and the payload-free
  History notification.
- **30 History pagination checks** passed. Desktop Chromium and iPhone
  emulation covered retained rows, disabled actions, cancellation, filter
  and sort changes, Back, page clamping, and saved table preferences.
- **20 History sync checks** passed. Chromium observed the real
  payload-free SignalR event, active-page refresh, a missed event, and
  recovery through a single refresh after reconnect.
- **612 delivery checks** passed, including **305 acquisition checks** and
  **56 musl Docker checks**. The package also passed **125 provider and
  accessibility**, **62 library review**, **262 identity**, **112
  authentication**, **41 datastore**, and **46 webhook and clipboard checks**.

The browser checks use disposable History rows and a disabled synthetic
client and indexer. Seeded dates use SQLite's UTC format so they sort alongside
real events. Run both with `validate-local.py --checks historypaging historysync`
and the usual runtime, Playwright, and output arguments.

Local evidence is under `_temp/u10-u11-history-20261121/`, including source
test logs, package receipts, browser reports, and synthetic screenshots.
The final delivery receipt is in `delivery-retest/` and the combined History
report is in `history-fixed.mhr9sR/run/`.

## U10, U11 and U17 Wanted and Queue verification (2026-11-21)

`e5e203f77` migrates Wanted Missing and Cutoff Unmet. `e48890a94` adds
Queue pagination and confirmation safeguards. The glibc and musl Linux x64
packages carry matching clean provenance from `e48890a94`.

- Standalone typecheck, frontend lint, and **89 frontend tests** passed on
  Node **20.11.1**. **148 Python tooling tests**, **50 focused Core tests**,
  and **70 API tests** passed.
- The packages passed **612 delivery checks**, including **305 acquisition**
  and **56 musl Docker checks**. **50 History**, **125 provider and
  accessibility**, **62 library review**, **262 identity**, **112
  authentication**, **41 datastore**, and **46 webhook and clipboard**
  checks also passed.
- **27 Wanted checks** passed. Chromium exercised request cancellation,
  actions gated to current results, Back navigation, real monitoring and
  recording association, page-scoped Cutoff details, and recovery after
  a missed monitoring event.
- **22 Queue checks** passed. Chromium verified the saved unknown-author
  setting, single and bulk confirmation holds, captured IDs and options,
  stale-action feedback without retry, reconnect, and one list fetch after
  the actual manual Refresh command.

Chromium used the glibc package. The Queue browser fixture uses synthetic
rows on the authenticated route and intercepts its removal request. The
disposable backend has an empty Queue and zero configured download clients
or indexers. API tests use in-memory collaborators to check stale targets
and client scoping.

Use `validate-local.py --checks wantedpaging queuepaging` with the runtime,
Playwright, and output arguments. Evidence is under
`_temp/u10-u11-wanted-queue-20261121/`, with the combined browser report in
`wanted-queue-retest/summary.json`.

## U12, U14 and U15 preferences and settings verification (2026-11-21)

`b08ae123a` migrates the Queue table preferences. `c983beb2e` isolates the
global Download Clients draft, and `789f28562` adds navigation and dialog
protection to the current router and Add/Edit Download Client editor.
The glibc and musl Linux x64 packages carry matching clean provenance from
`789f28562`.

- Standalone typecheck, frontend lint, **103 frontend tests** and **149 Python
  tooling tests** passed. Frontend commands used Node **20.11.1**.
- **27 Queue checks** passed, including five preference checks. A legacy
  browser profile retained column order and visibility, page size, sort, and
  an explicit `false` unknown-author option. Changes survived reload, and
  corrupt storage opened Queue with defaults.
- **40 settings checks** passed against the glibc package. Chromium verified
  failed saves, duplicate submission protection, a real `202` save with an
  explicit `false`, clean and dirty reload, and exact Back and Forward
  navigation. Browser-generated history positions also covered cancellation
  after reload. The dialog protected edits on all four close paths and kept
  the parent draft on desktop and phone.
- The packages passed **612 delivery checks**, including **305 acquisition**
  and **56 musl Docker** checks. Identity, authentication, provider
  accessibility, and library review verification also passed.
- **27 Wanted** and **50 History** checks passed in the final paging replay.
  The Wanted fixture now waits independently for the committed file event,
  the resulting list response, and the row to disappear.

The settings fixture used a disabled synthetic client. It intercepted one
failed global settings save and let the valid retry write to the disposable
database. Saved client credentials and an unrelated zero-valued quality
threshold were unchanged.

Use `validate-local.py --checks settingsdrafts queuepaging` with the runtime,
Playwright, and output arguments. Evidence is under
`_temp/u12-u14-u15-20261121/`. See `settings-verified/summary.json` and
`paging-final/summary.json` for the browser results.

## U13, U16 and U18 selection, navigation and filters verification (2026-11-22)

`26b7e8e85` adds Wanted selection by local book ID. `38893c6a8` moves the
application to React Router 7.18.4 and keeps the unsaved-draft guard.
`6a099f7f3` adds History and Queue filters. The glibc and musl Linux x64
packages carry matching clean provenance from `6a099f7f3`.

- Both TypeScript projects, frontend lint, **114 frontend tests**, **75 API
  tests**, and **149 Python tooling tests** passed. The production Webpack
  build verified CSS types.
- **36 navigation checks** passed with disposable homes at `/` and under
  `/bookshelf-validation`. The browser checked login return URLs, direct
  recording bookmarks, fragments and query strings, Back and Forward, the
  recording-to-author link, Calendar, and the application not-found route.
- **40 settings checks** passed with the new router, including dirty Back and
  Forward, reload, same-route query and fragment changes, and dialog close on
  desktop and phone.
- **32 Wanted checks** passed, including Shift ranges, active-page Select All,
  and preserving surviving book IDs through a sort. **39 History paging** and
  **20 live-sync checks** passed, covering combined book and event filters,
  saved filters, recovery from invalid storage, desktop and phone.
- **32 Queue checks** passed. The browser verified filter round-trips,
  filter-specific empty results, and the unknown-author preference. The
  published endpoint accepted supported filters and rejected invalid values.
  The API tests checked counts and page contents across tracked and pending
  jobs. The browser table used synthetic rows.
- **612 delivery checks** passed, including **305 acquisition** and **56 musl
  Docker** checks. Identity, authentication, provider accessibility, and
  library review verification also passed.

Use `validate-local.py --checks routernavigation settingsdrafts queuepaging
wantedpaging historypaging historysync` with the runtime, Playwright, and
output arguments. See `navigation-verified/summary.json` and
`paging-verified/summary.json` under `_temp/u13-u16-u18-20261122/`.

## U19, U20 and U21 search and library performance verification (2026-11-24)

`f4e3e9b9b` scopes server-side release identities. `81f6ee0a3` adds typed
interactive search queries and responsive windowed results. `5b5eb5eec`
refines wrapping and tab handling. `77d05ce49` restores library scroll
positions. `79dfd16d6` preserves freshness when an in-flight search is
invalidated. `2581f5c15` fixes the measured release window at the end of a
long list. Both Linux x64 package variants carry matching clean provenance
from `2581f5c15`.

- Both TypeScript projects, frontend lint, **122 frontend tests**, **85 API
  tests**, and **149 Python tooling tests** passed. The production Webpack
  build verified CSS types.
- The API tests covered identical indexer release GUIDs from separate book
  and author searches, stale tokens, legacy grab compatibility, request-local
  copies, and pack membership. Frontend tests verified scope cancellation,
  local text filtering, invalidation during an in-flight request and exact
  release status updates without extending the five-minute freshness period.
- **31 interactive search checks** passed against the published Linux x64
  package. The real search without configured indexers and synthetic
  280-release responses exercised text and preset filters, the custom filter
  editor, sorting, fresh reopens, explicit refresh after expiry, isolated
  book and author results, and the exact ID used by a forced grab. No real
  indexers or download clients were involved.
- An independent browser probe with 280 releases measured 14 mounted rows
  (413 search-panel descendants) on desktop and 3 (155 descendants) on a
  phone, compared with 280 rows and 7,392 descendants in both old layouts.
  The phone's internal horizontal overflow fell from 408px to 3px. Expanded
  audio details and rejection reasons did not overlap adjacent results. A
  single scroll to the end reached the last release in both layouts; the
  middle and top remained reachable, including after resizing to cards.
  A separate manual-search modal probe exercised wide and phone layouts and
  a simulated 200% zoom layout (720px CSS viewport, device scale factor 2).
  Long titles and rejection text wrapped without
  internal overflow; the correct book and last result remained reachable.
- **42 library virtualization checks** passed. The large-library benchmark
  used 521 authors and 4,161 books, with a peak of 28 mounted rows on desktop
  and 23 on a phone. The existing virtualizer
  already bounded the DOM, and the frame measurements did not establish a
  reliable performance advantage for replacing it. The scroll callbacks were
  fixed instead. All four settled Back-navigation offsets (19,015, 152,095,
  18,560 and 149,964 pixels) restored exactly, where the baseline returned to
  zero. Selection, sorting, column changes, alphabet jumps, resizing, distinct
  regional recordings and retained ebook pages were also checked.
- **612 delivery checks** passed, including **305 acquisition** and **56 musl
  Docker** checks. The exact packages also passed all **262 identity**, **112
  authentication**, **188 provider-accessibility and library-review**, **77
  navigation and settings**, and **124 paging and live-sync** receipt checks,
  including their unchanged-package assertions.

Use `validate-local.py --checks interactivesearch libraryvirtualization`
with the runtime, Playwright, and output arguments. Evidence is under
`_temp/u19-u20-u21-20261124/`, in `search-library-final/summary.json` and the
other `*-final/summary.json` receipts. The controlled library comparison is in
`_temp/u21-library-20261124/{baseline-settled,candidate-settled}/result.json`.
The independent exact-package search and modal measurements are in
`_temp/u19-browser-benchmark.RTMFmw/{final-exact,modal-exact2}/measurements.json`.

## U22, B06 and B03 asset and recording lifecycle verification (2026-11-24)

`61b2bef65` moves the frontend from Webpack to Vite 8.3.4 with Node 24.22.3.
`5719886db` preserves separate Yarn 1 canonical and aliased package entries.
`b0741e7de` adds the local recording editor, local author creation and explicit
pack work memberships. `0ddf620a2` corrects invalid-membership HTTP status
handling, and `f11f21142` preserves claims on unchanged files with older SQLite
timestamp encodings. Both Linux x64 packages have matching clean provenance
from `f11f21142`.

- A fresh frozen Yarn 1.22.19 install, both TypeScript projects, ESLint, **124
  frontend tests**, and **150 Python tooling tests** passed. Vite verified CSS
  declarations in its production build. Existing CSS imports retain their
  module behavior; the full Stylelint run reports one pre-existing property
  ordering violation in `EditQualityProfileModalContent.css`, with no new
  violations or formatter crash. The local install was also updated.
- **5,797 source tests** passed across the offline Core, Common, API, Host,
  Updater, PostgreSQL migration and PostgreSQL recording-repository runs,
  including **4,634 Core tests** and **86 API tests**. There were **96 skips**
  across those suites, including platform-specific cases and the SQLite-only
  timestamp fixtures in the PostgreSQL run. The focused work-membership cases
  passed **10/10** on SQLite and PostgreSQL 16, and the local recording service
  and repository's final focused run passed **22/22**.
- **44 asset checks** passed at the root and nonempty URL base, including
  authenticated deep bookmarks, generated scripts, fonts, images, real search
  worker results and the local OAuth callback HTML. The same baseline checks
  passed on the pre-Vite package. The isolated Vite update proof verified new
  content hashes, removal of obsolete assets, source maps and a non-minified
  profiling build. The development proof against `542b91fa3` passed **11
  checks**, including same-route TSX Fast Refresh, CSS declaration generation
  and live source localization. Legacy class-component styles may reload the
  page rather than preserving in-memory state. The later source changes only
  affect recording validation and repository claims.
- **76 recording lifecycle checks** passed on the published candidate. They
  upgraded a synthetic `2581f5c15` home, preserved all baseline IDs and data,
  edited an older local recording without changing its zero-based chapters,
  and created local-only authors from real generated audio through the API
  and browser. Desktop and phone editors saved exact local and work revisions.
  A pack, independent US/UK recordings and a fractional standalone recording
  shared two explicitly confirmed works, with five distinct files counted
  once. The uncertain membership was not counted until reviewed. Stale and
  duplicate edits were rejected; local metadata and coverage survived refresh,
  rescan, restart and application-managed backup restoration without modifying
  owned media.
- **39 datastore checks** passed on SQLite and PostgreSQL, including the
  genuine older schema upgrade, expected additive columns and new membership
  constraints, injected failure and point-in-time restore. The PostgreSQL
  proof now recreates its own database targets empty before restoring the
  older dump, since the dump cannot remove newer dependent foreign keys.
  All **110 authentication policy checks** passed after accounting for the
  new nullable metadata columns; the original credential and catalog
  preservation assertions remain in place.
- The final package passed **612 delivery checks**, including **305
  acquisition** and **56 musl Docker** checks. Exact-package identity,
  provider-accessibility/library review, router/settings/search/virtualization
  and operational paging receipts passed **262**, **188**, **150** and **124**
  checks respectively, including unchanged-package assertions.

Local evidence is under `_temp/u22-b06-b03-20261124/`, including
`source-tests-replay/`, `asset-final/summary.json`,
`lifecycle-phone-replay/summary.json`, `datastore-final/summary.json`, and
`auth-replay/summary.json`, as well as the other final package receipts.
The Vite HMR and cache receipts are in
`_temp/u22-vite-work/dev-final-hmr/result.json` and
`_temp/u22-vite-work/cache-proof-result.json`.

The local U22/B06/B03 slices are complete. No GitHub workflows, external
provider lookups, real download clients or production library homes were used.
CI and supported-platform acceptance outside these Linux x64 packages remain
open under P21/P22. Future provider associations for local recordings are not
invented by the editor.

## RG04, B09 and D22 collection diagnostics verification (2026-11-24)

The three implementation slices landed in `c6091abac`, `26e397334`,
`6e7062ba6`, `09479e0f0` and `90baea993`. The hosted browser proof identified an
additional list-editor binding error; `2b8f722ee` fixes it and adds two frontend
regression cases. Both Linux x64 packages (glibc and musl) were rebuilt from
clean `2b8f722ee` with matching backend and frontend source provenance. The
only product changes from `90baea993` are that frontend binding fix and its
unit test.

- RG04's release-group resolution passed **137 focused Core tests** (one
  existing skip) and **14 PostgreSQL tests** (one skip). The later field-limited
  quality/group edit passed **47 Core tests** (one existing skip) and **four
  PostgreSQL regression tests**, including atomic rollback and unchanged
  fractional MediaInfo timestamps. Original grab, folder and file labels,
  the recording identities that established independent pack membership,
  and the resolution source/reason remain available for review. An explicit
  manual choice, including clearing the group, is not overwritten. A volume
  range, a multi-work omnibus, legacy grab or ordinary multipart recording
  alone is not proof of an independently releaser-labeled pack.
- B09's nine statistics tests passed on each of SQLite and PostgreSQL 16.
  In the local synthetic benchmark, a query covering 4,165 books and 4,168
  files took 121 ms on SQLite and 96 ms on PostgreSQL; these are individual
  fixture measurements, not a latency guarantee. The statistics page reports
  tracked file sizes rather than physical disk allocation, measured durations
  rather than advertised runtime, and file associations rather than a
  guarantee of complete recordings. Explicit work membership is deduplicated.
- D22's final focused suite passed **40 SQLite tests, six PostgreSQL repository
  tests and eight API exclusion tests**. Migration 165 retains independent
  per-list snapshots and revision claims. Failed, partial, disabled and stale
  attempts cannot overwrite the last complete result, whereas confirmed empty
  results can. Conflicting qualified identities are stored as unresolved and
  cannot auto-add a recording. Exclusions are exact, and neither a membership
  snapshot nor its read-only display grants authority to remove files, change
  monitoring, or synchronize tags. The panel's status refers to the saved list
  definition, not a live provider request.
- The backend suite at `90baea993` passed **5,857 .NET tests** with 94 skips:
  4,679 Core, 836 Common, 94 API, 181 Host, 20 Updater, 23 PostgreSQL migration
  and 24 PostgreSQL recording/group/list/statistics tests. The final
  `2b8f722ee` frontend passed both TypeScript projects, ESLint, production Vite
  verification and **130 frontend tests**. All **150 Python tooling tests**
  passed. GitHub workflows were not used.
- The exact final package passed **72 collection diagnostics checks** (74
  including unchanged-package assertions). It upgraded the genuine older
  `f11f21142` audiobook package's disposable home, keeping all catalog IDs,
  associations and owned bytes. The fixture has eight distinct audiobook
  recordings, two confirmed works, one ebook and eleven tracked files. Only
  four audio files contribute their 340 measured seconds; stale analysis,
  repeated streams and multiple work associations do not inflate totals. Two
  loopback Readarr providers exercise overlapping membership, partial data,
  HTTP failure, confirmed empty success, conflicting identities, exact
  exclusions, disabled and deleted lists. Desktop and phone Chromium exercise
  statistics filters and Refresh, the actual list editor and the File Details
  group explanation. Restart and application-managed backup restoration retain
  the snapshots, statistics, work memberships and evidence. The hosted
  release-group proof uses a seeded persisted evidence record; the separate
  source fixtures exercise the selection and import logic itself.
- On `90baea993`, datastore and authentication proofs passed **39** and
  **110** checks respectively. The operational paging, metadata-accessibility
  and library review, identity, and recording lifecycle receipts passed **124**,
  **188**, **262** and **78** checks respectively, including unchanged-package
  assertions. These backend regressions preceded the frontend-only ID binding
  correction.
- The exact final package's asset, router, settings, interactive-search and
  library-virtualization regression receipt passed **194 checks**, including
  the unchanged-package assertion. The exact final package also passed **612
  delivery checks**, including **305 acquisition**, **56 musl Docker** and
  matching backend/frontend release identity checks.

Local evidence is under `_temp/rg04-b09-d22-20261124/`, particularly
`collection-verified/summary.json`, `collection-verified/collectiondiagnostics/screenshots/`,
`source-tests/`, `datastore-final/summary.json`, `auth-final/summary.json`, and
`ui-verified/summary.json` and `delivery-verified/summary.json`. Focused
release-group, statistics and list receipts
are in `_temp/rg04-20261124/`, `_temp/b09-library-statistics/` and
`_temp/d22-membership/` respectively.

These local RG04/B09/D22 slices are complete. D23/D24 remain separate work.
No production or existing development library, external provider, real
indexer/download client or GitHub workflow was used; other platform and CI
acceptance remain open under P21/P22.

## D23, D24, D25, D08, D20 and D29 policy verification (2026-11-24)

The local implementation includes the qBittorrent mode (`ab21f3d28`), Custom
JSON provider (`df29bf412`), per-indexer rejection policy (`3d3e53793`), list
lifecycle (`6fc295263`, `d872f911e`), and profile-based quality ranking
(`0c4da26bd`). Migration 166 adds provenance for exact list-created recordings
and list-owned author tags; 167 adds a derived rank view; 168 adds the three
indexer policy fields with the review default. Existing IDs, user-owned tags,
quality profiles and media remain unchanged by the migration itself.

- D23/D24: **37 focused SQLite tests, 11 PostgreSQL repository tests and nine
  API tests** passed. All successful list snapshots must support a cleanup
  proof; recent exact membership and known list-created origin are required.
  Older or local/manual records cannot gain cleanup authority retroactively.
  Tag Existing None preserves existing new-author behavior. Add is persistent;
  Sync owns only tags it creates, with shared claims across lists. The cleanup
  tag action is author-level; its unmonitor action targets exact recordings.
  An explicit same-value user edit relinquishes the relevant list claim.
- D25: **49 Core and 49 Common transport tests** passed, including page
  consistency, complete-success gating, response bounds, optional bearer
  header, and response/redirect diagnostic redaction. The public contract and
  sample JSON live in [Custom JSON import lists](custom-json-import-lists.md).
  Explicit work grouping expands to the supplied recordings; it does not
  invent work membership or create unresolvable work records.
- D08: **148 focused SQLite/Core tests, 96 API tests and seven PostgreSQL
  repository tests** passed, including successful terminal claim, atomic
  blocklist/history writes and concurrent duplicate protection. Unknown or
  ambiguous source evidence, ordinary matching failures and unsupported
  remote-only diagnostics remain review cases. Rejecting a file type never
  authorizes automatic removal; opt-in search still respects the existing
  global/interactive rules. The source tests cover the real transition;
  hosted settings verification uses disabled owned indexers only.
- D20: **53 focused SQLite/Core tests, 10 PostgreSQL tests and 94 API tests**
  passed on the initial ranking slice. The rank view reads the persisted
  profile and is not a second table of copied rank rows. History and Blocklist
  order the complete filtered result before pagination, with equal group
  ranks and stable ID ties. For the Books sort, the minimum rank of tracked
  files in the monitored edition represents the least preferred tracked part,
  not the guaranteed quality or completeness of a recording. The old global
  quality weight remains available to existing clients and acquisition logic.
- D29: **135 focused qBittorrent tests** passed. The selected total/inactive
  parameter is checked in magnet, file-upload and explicit share-limit forms.
  Null, global, unlimited and zero semantics remain intact. Old clients reject
  incompatible inactive mode before an add. Existing completion safeguards
  also recognize a short global inactive-time remove/delete action.
- The initial combined `88366f339` packages had matching clean glibc/musl
  source provenance. Both TS projects, ESLint, Vite production verification,
  **132 frontend tests and 156 Python tooling tests** passed. The full
  offline source suite passed **6,012 tests** with 94 skips: 4,797 Core,
  843 Common, 96 API, 181 Host, 20 Updater, 23 PostgreSQL migration and 52
  focused PostgreSQL regression tests. The fixture-only AcquisitionProbe
  binding correction `066ffdb40` uses the published rejection service with
  synthetic unknown-indexer context and rejects any policy write. Its replay
  passed **306 acquisition checks**, retaining all previous assertions and
  adding a review-only preservation check.
- The initial combined package also passed **41 datastore**, **110 auth**,
  and **72 collection diagnostics** checks; broader UI/navigation, operational
  paging, metadata-accessibility/library review, recording identity and
  recording lifecycle receipts passed **194**, **124**, **188**, **262** and
  **78** checks respectively, including unchanged-package assertions.
  The focused list, provider and indexer hosted checks passed **45**, **54**
  and **39** cases respectively, each exercising real API behavior and
  desktop and phone UI against disposable homes. Initial ranking verification
  passed 29 cases; subsequent review reproduced a separate cached rank issue
  after individual and bulk author profile reassignment, requiring the final
  correction below.
- `f09759bef` fixes the independently reproduced author-profile reassignment
  case and updates file editor ranks from the successful response even if
  SignalR is disconnected. It invalidates per-author and all-library cached
  statistics after individual and bulk author edits. The rank event refreshes
  authors only when the stored profile assignment actually changes, so
  ordinary tag-only edits do not trigger a full rank refresh. **21 focused
  backend and eight frontend tests** passed, including seven new C# cases.
  The hosted fixture also warms the individual and global caches and switches
  profiles via both real author endpoints before validating the resulting
  library/file and paginated history/blocklist ranks.
- The final clean glibc and musl Linux x64 candidates were rebuilt from
  `f09759bef`. All four dedicated hosted receipts pass on that exact source:
  **45 list lifecycle**, **54 custom-provider/qBittorrent**, **39 indexer
  policy**, and **34 quality-ranking checks** (172 feature checks; 180 with
  the unchanged-package assertions). The list receipt verifies actual sync,
  overlapping membership, both manual ownership boundaries, healthy/failed
  snapshots, stale confirmation rejection, tag and unmonitor actions, restart,
  preserved ownership and unchanged media bytes. The quality replay includes
  the additional individual/bulk reassignment proof; no restart is needed to
  get fresh ranks. The provider and indexer proof uses local mock responses
  or disabled indexer definitions, never actual downloads.
- Final exact-source verification passed **6,019 .NET tests** with 94 skips
  (4,804 Core, 843 Common, 96 API, 181 Host, 20 Updater, 23 PostgreSQL
  migrations and 52 focused PostgreSQL tests). The corrected frontend passed
  both TS projects, ESLint, Vite production verification and **133 frontend
  tests**. The final tooling suite passed **156 tests**. The existing Python
  resource warnings originate in the earlier identity/restore test helpers;
  they are not new provider fixture warnings or failing tests.
- Final exact-package replay passed **613 delivery checks**, including 306
  acquisition checks and 56 musl Docker checks, and the datastore/authentication
  checks again passed 41/110. Replayed collection, library/navigation,
  operational paging, metadata/library review, identity and lifecycle checks
  passed 74/194/124/188/262/78 respectively (including unchanged-package
  assertions). Across all 13 final hosted receipts there were **1,868 passing
  checks**, including the unchanged-input assertions, with no failed checks.

Final evidence is in `_temp/d23-d29-20261124/`: `source-tests-verified/`,
`quality-verified/summary.json`, `list-verified/summary.json`,
`provider-verified/summary.json`, `indexer-verified/summary.json`,
`delivery-verified/summary.json`, and the other `*-verified/summary.json`
receipts. The corresponding feature screenshot directories contain the owned
Chrome desktop and phone views. Focused implementation receipts are in
`_temp/d23-d24-list-policy/`, `_temp/d25-custom-20261124/`,
`_temp/d08-20261124/`, and the D20/D29 and assignment logs under
`_temp/d23-d29-20261124/`.

All six local slices are verified. No production or existing development
library was modified. No GitHub workflows, external real list providers,
real indexers, or real download clients were used. Cross-platform release
and CI acceptance remain separate P21/P22 work.

## Dependency, typing, queue API, host and notification verification (2026-11-24)

This batch covers P09, U07, P18–P20, P22–P23, D18 and D27–D28. The exact
integrated product source was `0f75e288e`; all three declared Linux RIDs were
published from a clean, locked checkout. The final delivery candidate adds
only two equivalent stylesheet lint corrections and the test-only loopback
teardown repair (`aa5be616a`). Later commits change validation/documentation
only. See the [roadmap](sonarr-v5-modernization-roadmap.md),
[platform matrix](platform-support.md),
[host configuration and diagnostics](host-tls-and-lifecycle.md),
[v2 API contract](../src/Readarr.Api.V2/README.md) and
[dependency inventory](dependencies/2026-11-24-maintenance/summary.json).

- Managed and frontend dependency inventories match the exact candidate's
  lockfiles and manifests (28 SHA-256 comparisons). The 25-project NuGet audit
  reports zero vulnerable packages. Yarn audit reports changed from 183 to
  zero; the final offline frozen workspace install succeeded and a further
  audit of the installed graph returned zero findings across 836 dependencies.
  This is registry-advisory evidence, not native-code or complete security
  assurance. No license key is required.
- Exact integrated source tests: **6,100 passing .NET tests, 94 existing
  skips** (4,850 Core, 855 Common, 101 API, 193 Host, 21 Updater, 23 PostgreSQL
  migration and 57 focused PostgreSQL tests). The original Common run exposed
  an existing TCP listener disposal race after both IPv4 and IPv6 test requests
  had succeeded. Test-only commit `199cc07e0` cancels and joins pending accepts
  before stopping the listener; the whole affected fixture, 15 repetitions
  of the failing case, and full Common suite then passed. The original failure
  receipt is retained; no application behavior was changed by that repair.
- Both TypeScript projects, **140 frontend tests**, ESLint, production Vite
  verification (including CSS declarations) and targeted stylesheet lint pass.
  The final tooling suite passes **170 tests**. The strict TypeScript migration
  remains incremental; the source still contains 999 JS files and 98
  non-declaration TS/TSX files.
- The new exact-package feature checks pass: **34 TLS/lifecycle**, **21
  download-level queue**, **24 versioned OpenAPI**, **36 notification
  lifecycle**, and **27 profiling**. Fixtures prove actual encrypted PFX and
  PEM chain handshakes, repeated same-home process restarts and one-time
  SIGTERM cancellation, identity-specific download coverage/actions on two
  isolated client configurations, normalized v1/v2 OpenAPI equality, health
  recovery opt-in migration, and existing media-server settings. Profiling
  verifies API-key authorization, large-catalog (4,161 books) request and SQL
  timings, non-cacheable aggregate-only responses, desktop and phone views,
  restart gating and sample clearing. Focused source tests also exercise real
  PostgreSQL provider timings, monotonic retention and concurrent scopes, and
  Plex/Subsonic adapter batching, target resolution and retry behavior with
  synthetic dependencies. The packaged notification check exercises an owned
  loopback Subsonic test endpoint.
- The primary exact-artifact sweep passed **2,346 checks across 22 successful
  receipts**, including unchanged input package assertions, and **616 delivery
  checks** (including 306 acquisition and 56 actual x64 musl Docker checks).
  Additional SQLite, PostgreSQL migration, log storage, authentication, list
  lifecycle, library identity, browser/UI and older-package upgrade checks
  passed. Static package validation passed on each of glibc x64, musl x64,
  and musl ARM64. ARM64 is static inspection only, not native execution.
- The pre-P14 PostgreSQL connection-string fixture initially compared the
  entire pre-migration data/schema directly with the current schema. Tooling
  commit `4e66c7566` requires the genuine historical migration set through
  158, preserves every previously tracked value, verifies only the expected
  additions and their defaults/types, and verifies exact final migration
  history. The corrected real SCRAM/owned-socket upgrade and connection-string
  replay passed **117 checks**, plus both unchanged-package assertions. This
  corrects the assertion for later additive migrations without relaxing data
  preservation. PostgreSQL TLS transport is not exercised by that fixture.

Exact source evidence and the initial/fixed Common receipts are in
`_temp/final-source-regression-20261124/`. Package, audit, frontend, tooling,
feature-specific browser screenshots and hosted receipts are in
`_temp/p09-u07-p23-p22-20261124/`. The primary runtime is preserved in
`build-source-verified/_artifacts/<rid>/net10.0/Readarr`, and the final
stylesheet-corrected runtime is in
`build-source-final-style/_artifacts/<rid>/net10.0/Readarr` under that evidence
root. The original failed PostgreSQL comparison lives in
`postgresconnections-verified/summary.json`; its successful strict replay is
`postgresconnections-replay-verified/summary.json`.

The final clean `aa5be616a` candidate was rebuilt for all three RIDs after the
stylesheet corrections. A separate **843-check replay over ten receipts**
passed native x64 startup/delivery and Docker, static inspection for the three
RIDs, all five new feature checks, UI/assets, desktop/phone queue and history,
metadata/library review, and the genuine import-list upgrade. Its glibc x64
package fingerprint is
`ea5eee39363324b17e07fa6db1a5e128dea4e67be6b1e4b813346d126222ff8e`.
The corresponding `style-final-*/summary.json` receipts and the candidate's
own UI source-provenance file confirm the exact clean publication. The backend
implementation is unchanged from the primary full sweep; the differences are
only the two CSS files and Common test-fixture repair.

The backlog is now **81 of 92 Verified**, nine In progress, one Planned, and
one Deferred. P09 and U07 retain their broader dependency/type migration gates;
P22 retains native ARM64 and release acceptance; P23 retains background-job
profiling and broader overhead evaluation. No production or existing
application home, saved user configuration or audiobook media was modified.
No real notification/download/list services or GitHub workflow were used.

## Background profiling, typed library resources, dependency compatibility and local release (2026-11-24)

This follow-up covers P23, U07, P09, D30 and P25. The final release source is
`7b941a85335e1b5830a483168144ebb44cd7beaf`, tree
`9fc4dd6397ff692ab557952860ae717efd6cfd9b`. All three declared Linux RIDs were
built from a clean checkout with matching backend/UI provenance. The actual
local release candidate is native glibc x64 only; musl x64 image verification
and musl ARM64 static inspection do not mark native ARM64 or signed release
acceptance complete.

- **P23** (`bc019a705`, additive OpenAPI `ec4613658`): the exact final package
  passed 41 profiling checks. Author/book refresh, folder scan, manual and
  downloaded import, download monitor/processing and MediaInfo command scopes
  share the 256-sample, ten-minute monotonic store with HTTP scopes. Existing
  HTTP-only API counters are preserved, with separate typed job groups. Actual
  scheduled commands, native AAC analysis and an owned manual Copy import
  prove timing and source preservation; a failed import is not recorded as
  successful. SQLite and PostgreSQL source regressions cover provider timings,
  nested scopes, cancellation and bounded retention. The disabled command
  path never opens a profiling scope. Seven paired local-author worker timings
  after warm-up gave a final median 3.986 ms disabled (3.683–4.556 ms range)
  and 4.935 ms enabled (3.881–6.318 ms range). Earlier isolated and integrated
  pairs are retained; these small synthetic workloads are not a universal
  overhead threshold. The owned profiling fixture uses a rejecting loopback
  HTTP proxy, never forwarding startup requests; this is not an OS-level
  network sandbox. See [diagnostics](host-tls-and-lifecycle.md#opt-in-performance-diagnostics-p23).
- **U07** (`2367adf9b`): eight component conversions, branded resource IDs,
  typed work/Wanted/history/download queue paths, and four real library
  collection decoders preserve legacy fields, null/zero/false and unmapped
  associations while rejecting malformed identity data. `ce93fb9df` adds a
  typed external virtualized-scroll helper and unit/browser regressions. The
  source still has 991 JS and 108 non-declaration TS/TSX files; strictness is
  incremental, not a full store conversion.
- **P09** (`987b8a70e`, `efff3d3aa`): maintained major-line/same-line updates
  include SignalR 10, Fuse 7, Ical.Net 5, NUnit 4, Node 24 types and filesize 11.
  The new calendar fixture proves inclusive/exclusive all-day publication
  dates, stable recording UIDs and tag filtering before/after upgrade.
  Registry audits report zero findings across 25 NuGet projects and 1,344
  Yarn dependencies; all 29 recorded lock/manifest hashes match the exact
  release source. Frozen offline workspace install and the repeat Yarn audit
  succeeded. Ical.Net and NodaTime license/notice files match the pinned source
  bytes in every package. Wider incompatible major-line trials and native
  acceptance remain open; see [the dated inventory](dependencies/2026-11-24-major-lines/summary.json)
  and [the review](dependency-audit.md). The attempted stylelint maintenance
  version was reverted: full stylesheet lint has three pre-existing unrelated
  property-order findings, not a newly passing full-CSS-lint gate.
- **D30** (`02397742e`): completed the pinned-v5 [import extension design](import-extension-design.md).
  The current importer and existing post-commit notifications are retained;
  no script-controlled transfer, setting or new execution capability is
  enabled. Any future transformation requires bounded staging-output mapping,
  independent media/identity checks, rollback, worker isolation and reliable
  receipt/Copy/seed preservation.
- **P25**: prepared and independently verified the actual unsigned local
  `develop` candidate **10.0.0.42949** for `bookshelf-audiobooks`, RID
  `linux-x64`. The manifest binds 803 packaged files (hashes and modes),
  the matching GPL source archive and 12 complete hosted evidence reports
  (2,325 checks for the exact glibc package), including all eleven required
  release gates. The package fingerprint is
  `3c6d8592d33c2c59707745bab5bb76e4bda33b30ac3d580644fbc8f02784a28c`,
  source archive SHA-256 is
  `884e29ea4157c35db011e85583e505f8046acd927f526cb55c678ac227a34361`, and
  manifest SHA-256 is
  `da0b0fcfcc80bb841506e75d31c37814e6cb6a57a6d65043c227002b0943e078`.
  Both actual `prepare-release.py prepare` and a separate `verify` invocation
  passed with the expected product, channel, commit, RID and version. The
  source archive contains the application GPL license and new dependency
  notices. `publishable=false` and `builtInUpdatesEnabled=false`; the artifact
  is a local unsigned candidate, not an authenticated update or published
  release. See [release policy](release-policy.md#current-local-candidate-2026-11-24).

### Integrated results and corrected regressions

The exact final source checkout passes **6,110 .NET tests with zero failures**
and 94 existing skips (4,857 Core, 855 Common, 102 API, 193 Host, 21 Updater,
23 PostgreSQL migrations and 59 focused PostgreSQL), using SDK 10.0.404 and
NUnit 4.6.1. Both TypeScript projects, **152 frontend tests**, ESLint and Vite
CSS-declaration/build verification passed. The final Python validation suite
passed **173 tests**.

The final exact packages passed **2,365 hosted checks across 14 successful
receipts**, including unchanged-package assertions: delivery 616, auth 112,
identity/policies 582, UI 214, paging 129, library review/accessibility 188,
new features 87, TLS/notification 72, datastore 43, PostgreSQL connections 119,
SQLite 100, log storage 63 and static musl x64/ARM64 20 each. The delivery
receipt includes 306 acquisition checks and 56 actual x64 musl Docker checks.
The expanded library virtualization check passed all 62 scenarios, including
author/book table, poster and overview desktop jumps and phone scroll/back
restoration. All final reports are complete and have zero failed checks.

The first integrated `bc019a705` artifact and test results are retained. The
bare Common event-handler test lacked the new host clock and actual Server
options; `2d437e303` repairs only the test composition to mirror production.
The real library jump regression in the updated `react-virtualized` graph was
reproduced and isolated to controlled scrolling (identical U07 UI passed on the
previous dependency graph). `ce93fb9df` corrects the application using public
Grid offset and WindowScroller scroll callbacks in all supported views,
retaining the updated dependency. The initial historical SQLite probe also
observed an old baseline command succeeding after its lock released; the
current provider had passed, the isolated baseline replay passed, and the
final exact SQLite replay was green. No regression checks were removed or
relaxed to make the release pass.

The ignored evidence root is `_temp/p23-u07-p09-d30-p25-20261124/`. The
exact release package checkout is `build-source-release-final/`, evidence is
in `release-*/summary.json`, the unsigned staged bundle is `release-candidate/`
and its verification summary is `release-verification.json`. The bundle
contains `release-manifest.json`, `package/`, `source.tar.gz` and the hashed
receipts. The final complete source receipt is in
`_temp/final-release-source-regression-20261124/source-tests-release-final/results.json`.
Earlier attempts and superseded fixture receipts remain alongside the final
evidence. Later repository commits recording these results are documentation
only; re-verify the bundle against the exact clean release-source checkout.

The backlog now contains **83 of 92 Verified**, eight In progress and one
Deferred. P23 and the D30 design study are verified. U07 and P09 remain
staged, and P25's signed-release/publication gates remain open despite the
actual local candidate. Native ARM64, CI, and OIDC follow their existing
external/deferred gates. No production or existing application home, saved
user configuration or audiobook files were changed, no real download/list or
notification endpoint was used, and no GitHub workflow or publishing ran.
