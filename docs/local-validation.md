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
