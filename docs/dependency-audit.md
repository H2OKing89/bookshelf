# Dependency security maintenance

**Current runtime (2026-11-15):** application and test targets now use .NET 10,
SDK **10.0.404** and packaged runtime **10.0.15**, implemented in `d572aaa2b`
with API-binding correction `2b0edf8ef`. The
[current runtime-alignment record](#net-10-runtime-alignment-2026-11-15) and
[new inventory](dependencies/2026-11-15-net10/summary.json) supersede the earlier
runtime/version statements for current builds. Earlier sections retain the
original P09 .NET 6 security-slice evidence; the two dated inventories remain
separate. P01 and P09 are still **In progress** for the bounded gates below.

**P12 migration dependencies (2026-11-16):** `bb4cd939e` replaces the Servarr
FluentMigrator 3.3.2.9 umbrella with the three targeted FluentMigrator **8.0.1**
runner components and updates Npgsql to **10.0.3**, matching the pinned v5 donor.
This P12 slice left the SQLite provider update for P13. The
[new inventory](dependencies/2026-11-16-migrations/summary.json) records **119
package IDs / 119 exact components across 25 lockfiles**. Targeted runner
references remove unused database-provider dependencies. The normalized
[NuGet audit](dependencies/2026-11-16-migrations/nuget-audit.json) combines the
solution's 24 successful project results with the Windows GUI audited separately
using its explicit `net10.0-windows` framework: **25 projects, zero reported
vulnerabilities**. This does not expand the native-code audit or close P09/P21.

The [Npgsql 10 release notes](https://www.npgsql.org/doc/release-notes/10.0.html)
identify changed native `date`/`time` mappings. P12 verifies UTC instants, offset
timestamps, nullable dates and native GUID storage on SQLite and PostgreSQL;
existing application identifiers retain their text schema/converter. Connection
strings are omitted from migration-start logs. Pinned FluentMigrator/Npgsql
license texts and source checksums accompany the delivery packages under
`LICENSES/managed/`. Older dated dependency inventories remain historical records.

## P13 SQLite provider and engine, 2026-11-17

Current builds replace `System.Data.SQLite.Core.Servarr 1.0.115.5-18` with
[`System.Data.SQLite 2.0.4`](https://www.nuget.org/packages/System.Data.SQLite/2.0.4)
and the pinned v5 donor's separate
[`SourceGear.sqlite3 3.53.4`](https://www.nuget.org/packages/SourceGear.sqlite3/3.53.4)
native engine. The obsolete Servarr SQLite feed and mapping are removed. All
25 lockfiles were refreshed and restored in locked mode. The new
[inventory](dependencies/2026-11-17-sqlite/summary.json) contains **120 package
IDs / 120 exact components**, with **zero NuGet-reported vulnerable packages
across 25 audited projects**. As in P12, the Windows GUI audit uses its explicit
framework and is combined with the 24 successful solution results.

The managed provider no longer silently selects host SQLite. It loads the
explicit bundled `e_sqlite3` asset or fails. The
[native source/checksum lock](../scripts/sqlite-native.lock.json) identifies the
actual package bytes for glibc/musl x64 and ARM64; package checks verify the RID,
architecture and checksum. **The glibc assets require glibc 2.34 or newer.** Static
ARM64 asset inspection is recorded separately from native execution acceptance,
which remains open; this does not establish all P22 platforms.

Both packages identify their distributed provider/engine contents as public
domain. Exact package notices, SourceGear's license text and the pinned official
SQLite copyright page accompany delivery packages. The
[notice manifest](../LICENSES/managed/sources.json) distinguishes the provider
package's unavailable source revision from the independently pinned copyright
artifact. A clean NuGet report is not a comprehensive native-code audit.

Runtime compatibility includes an explicit preservation of Bookshelf's legacy
`ForeignKeys=false` policy: the new engine otherwise enables enforcement by
build default, breaking historical migrations. No historical migration is
changed. UTC dates, fractional ticks and GUID TEXT storage are verified through
production repositories under two non-English cultures and two non-UTC timezones.
WAL/pooling, the cache database and macOS's TRUNCATE/FullFSync settings remain in
the connection factory. Native busy timeout advances to 1000 ms after paired
measurements; the separate managed command timeout remains 30 seconds by default.

## Earlier 2026-11-15 security-maintenance snapshot

This record covers the **2026-11-15** security-maintenance slice of
[P09](sonarr-v5-porting-backlog.md#p09). It keeps the current target compatible
while the larger .NET 10 runtime transition remains in P01. Its SDK prerequisite
now advances to 10.0.403 because the old SDK 6 setup does not implement the
NuGet audit settings used to validate this change. The reviewed donor remains
Sonarr v5.0.0.2941 at `c018578e4b79ee048575c02d7a8aa2b929e4b837`.

The baseline lock inventory had **268 package IDs / 277 exact components** across
**24 lockfiles**, with **nine affected package IDs and 16 distinct advisory URLs**
in the official NuGet vulnerability feed updated 2026-11-14. This includes
build/test and runtime-identifier variants; it is not a claim that every listed
assembly is loaded by the application. The final inventory has **235
package IDs / 242 exact components across 25 lockfiles** and
**zero matching NuGet advisories**. The Windows GUI now has its own lockfile and is included.

| Change | Resulting version or decision |
| --- | --- |
| MailKit / MimeKit 4.8.0 | 4.17.0, a security-fixed version compatible with the current target |
| CodePages 6.0.0 | 8.0.0, satisfying the updated mail dependency graph |
| Cryptography.Pkcs 8.0.0 | Central transitive pin 8.0.2 |
| JWT and JsonWebTokens 6.8.0 | Central transitive pins 6.35.0 |
| ImpromptuInterface 7.0.1 | 8.1.0; removes the old System.Net.Http dependency graph |
| System.Runtime.Loader package | Removed; AssemblyLoadContext comes from the target framework |
| System.Private.Uri / RegularExpressions 4.3.0 | Central pins 4.3.2 / 4.3.1 for remaining transitive graphs |
| ImageSharp / PdfSharpCore / SixLabors.Fonts | Removed together, including their transitive entries |
| Raster resizing | Magick.NET-Q8-AnyCPU / Magick.NET.Core 15.1.0 |
| PDF author/title metadata | PdfPig 0.1.16 |

MailKit 4.17.0 includes the [STARTTLS fix](https://github.com/jstedfast/MailKit/security/advisories/GHSA-9j88-vvj5-vhgr)
and resolves a corrected MimeKit. MailKit 4.18.2 and later move this graph to
newer framework-support requirements; that update belongs with P01 rather than
silencing unsupported-target warnings. The
[Pkcs fix](https://github.com/advisories/GHSA-846f-f764-76mx) and
[JWT fix](https://github.com/advisories/GHSA-59j7-ghrg-fj52) are pinned explicitly
because upgrading the mail package alone did not correct every transitive issue.

NuGet's [feature-availability table](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages#feature-availability)
places PackageReference restore auditing in SDK 8 and later; SDK 6 cannot
enforce those settings. [SDK selection and target runtime are independent](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).

`global.json` sets SDK **10.0.403**, `rollForward: latestPatch` and
`allowPrerelease: false`; `mise.toml` uses the same baseline. CI derives
`SDK_PATH` from the SDK actually selected by `dotnet --version`, replacing the
stale 6.0.427 path. SDK selection now makes the audit policy reproducible for
contributors and CI, rather than relying on a newer SDK installed only on one
developer machine. Application targets and self-contained runtimes remain
**net6.0**; using SDK 10 does not make those runtimes supported. The optional
legacy extra-platform SDK-modification path still requires separate P01/P22
work unless it is explicitly rejected or replaced in this change.

Removing the old dependency edges also removes their 4.3.0 AppContext and
RuntimeInformation compatibility components. Their older 4.1.0/4.0.0 variants
remain solely through `Microsoft.DotNet.PlatformAbstractions` in test tooling;
they are absent from the application package graph. This is inventory cleanup,
not a downgrade of the application's framework assemblies. The current test
adapter/discovery stack passed the regression suites; its broader refresh stays
with the remaining P09/P21 work.

Central transitive pinning is deliberate. NuGet auditing remains enabled for
**all** dependencies at **low** severity and above, with warnings still treated
as errors. There are no advisory suppressions or committed audit exceptions.
The final clean locked restore and audit run use those repository settings.

## Image and PDF behavior

No Six Labors key is needed after removing both direct and transitive Six Labors
libraries. The raster adapter uses bundled Magick.NET native binaries; it does
not call a host ImageMagick command. The adapter keeps matching JPEG, PNG, GIF,
TIFF, BMP and WebP output, aspect ratio, JPEG quality 92, transparency and the
verified sequence behavior. Its policy admits only supported raster codecs,
disables external delegates/filters and indirect reads, and caps input bytes,
dimensions, decoded pixels and frames. A fresh private configuration directory
is initialized before native decoding; it cannot silently reuse a weaker old
policy. Native resource time limits are cooperative, not a hard process timeout
or a sandbox. The encoded input limit is 32 MiB, with 16,384 per dimension, 16 Mi pixels
per frame, 64 frames and 64 Mi total pixels. Preflight uses rounded-up output
budgets and rechecks actual resized frames. Native cache memory is capped at
256 MiB with disk/map caches disabled; the 30-second native time limit is
cooperative. Source and destination aliases are checked before cleanup, and
completed output is published from an owned temporary file.

PdfPig reads author/title information through an owned file stream and disposes
both stream and document. Empty metadata and malformed/password-required PDF
fallback keep the existing quality-provenance behavior. EPUB handling and media
ownership remain unchanged; no library database migration is added.

[Magick.NET 15.1.0](https://github.com/dlemstra/Magick.NET/releases/tag/15.1.0)
bundles ImageMagick **7.1.2-35**. That version is the patched release for the
reviewed maintainer [distributed-cache](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-w67p-4ppm-gmrg),
[MPC policy](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-5jwm-r7f4-4m53)
and [TIFF decoder](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-gr4q-6v79-qj3g)
advisories. Zero NuGet matches is not a comprehensive audit of every bundled
native codec or operating-system component. Future native updates still need
maintainer-advisory review and the same decoder regression checks.

The complete [license and notice directory](../LICENSES/managed/README.md)
accompanies output and publish artifacts. Magick.NET's original notice includes
ImageMagick and bundled-codec attributions; PdfPig's full license includes its
external component terms. The [source/checksum manifest](../LICENSES/managed/sources.json)
records exact upstream text and package revisions. Notice files are preserved
verbatim, including upstream line endings.

## Validation and remaining gates

- Clean locked restore: passed in a fresh manifest-only workspace and for each declared Linux RID.
- Full transitive NuGet audit: 25 projects, zero reported vulnerabilities, with no suppression or warning-as-error exception.
- Aggregate CycloneDX 1.6 inventory: [dated inventory](dependencies/2026-11-15/inventory.cdx.json) and [lock hashes](dependencies/2026-11-15/lock-sha256.json), including build/test/RID components and package SHA512s; not final artifact reachability.
- Full Common tests: **691 passed**, with 35 existing skips.
- Full Core tests: **3,439 passed**, with 55 existing skips.
- API regression tests: **267 passed**, with no skips.
- Focused cover/PDF/media tests: **64 passed** (included in the relevant broader suites).
- Host delivery-artifact native/image/PDF probe: **22 passed**.
- musl-x64 container native/startup checks: **40 passed**.
- P08 packaged authentication regression: **50 passed**.
- P07 packaged backup/restore regression: **51 passed**, including current and genuine pre-P02 backups.
- Genuine ARM64 cross-publish/runtime probe: **40 passed** under explicit QEMU, including actual HTTP startup, shutdown, codecs, PDF and SQLite.

All three Linux self-contained publications passed with matching notice hashes.
ARM64 MediaInfo 26.10 was genuinely cross-compiled from the pinned sources,
verified as static AArch64, and exercised through QEMU for version and synthetic
PCM WAV JSON. Cross-toolchain provenance accompanies the cached bundle. Full
ARM child-process/media-import behavior under QEMU is not claimed: the host has
no ARM binfmt handler. The runtime OS check was adapted from the pinned v5
[OsInfo](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Common/EnvironmentInfo/OsInfo.cs)
so startup uses .NET OS APIs rather than spawning `uname`. Mac x64
native-image assets are missing from the chosen package, an actual P22 support
gate rather than a successful platform check. A successful Linux x64 result
does not establish the entire former platform matrix.

No live development/production home, server, key, backup or media was changed.
This slice advances the SDK prerequisite but does not make the unsupported
.NET 6 runtime supported. P01's runtime
migration, P12/P13 database/native changes, P21/P22 complete artifact/platform
gates and P25 release identity remain separate work. Keep P09 **In progress**
until its broader framework-aligned refresh and acceptance requirements are met.

## .NET 10 runtime alignment, 2026-11-15

Implementation `d572aaa2b` moves all **25** executable, library, test and Windows
GUI targets to .NET 10 and aligns active build, test, publish and package paths.
`global.json` and mise select SDK **10.0.404** with latest-patch roll-forward and
prereleases disabled; self-contained executable artifacts bundle runtime
**10.0.15**. Microsoft's [official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
records the 2026-11-10 update as a security release, superseding the earlier
10.0.403 SDK/10.0.14 runtime baseline. The comparison donor remains Sonarr
v5.0.0.2941 at `c018578e4b79ee048575c02d7a8aa2b929e4b837`.

| Change | Current version or behavior |
| --- | --- |
| Applicable Microsoft Extensions, SignalR and System packages | 10.0.15, aligned with the new framework |
| Framework-provided assemblies | Redundant package references removed; framework assemblies supply them |
| SourceLink | SDK-provided support replaces the separate package |
| MailKit / MimeKit | 4.18.2 |
| NLog / NLog.Extensions.Logging | 5.5.1 / 5.5.0, retaining database-log cleansing across configuration changes |
| Microsoft.NET.Test.Sdk / NUnit3TestAdapter | 18.12.0 / 6.3.1 |
| NunitXml.TestLogger / coverlet.collector | 8.1.0 / 10.1.1 |
| NUnit | 3.14.0 retained; no NUnit 4 migration |
| Raster / PDF | Magick.NET 15.1.0 / PdfPig 0.1.16 retained; Six Labors packages remain absent |
| MediaInfo helper build / application image | Alpine 3.24.4 / LinuxServer Alpine 3.24 |

The old Alpine 3.21 base reached normal support end on 2026-11-01; Alpine's
[release table](https://www.alpinelinux.org/releases/) identifies 3.24 as supported
through 2028-06-01. Microsoft's [.NET 10 supported-OS table](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
includes Alpine 3.24 on x64/ARM64. The helper recipe change invalidates both
cached helper architectures. Fresh bundles retain MediaInfo 26.10, source locks,
checksums and notices; glibc-x64 and musl-x64 share the static x64 helper.

Compatibility changes replace ISystemClock with TimeProvider, obsolete PFX
constructors with X509CertificateLoader while preserving password/storage flags,
and obsolete proxy networks with System.Net.IPNetwork without broadening trust.
C# 14 array Contains expression lowering remains supported in both SQL dialects,
Transmission ETAs are bounded before TimeSpan construction, and stream checks
handle partial reads. No Bookshelf database migration is added.

Packaged backup-command HTTP checks exposed a separate framework compatibility
change: ASP.NET Core 7 and later infer registered complex action parameters from
DI. Bookshelf's DryIoc registrations include API resource classes. Correction
`2b0edf8ef` sets `DisableImplicitFromServicesParameters` so JSON bodies retain
their existing contract, following Microsoft's
[documented compatibility action](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/7/api-controller-action-parameters-di?view=aspnetcore-10.0).
Five hosted HTTP regression cases cover body binding and explicit service
parameters. This is a request-binding fix, not a database or credential change.

The [2026-11-15-net10 summary](dependencies/2026-11-15-net10/summary.json) was
generated from clean `2b0edf8ef` and records **145 package IDs / 145 exact
components / 25 lockfiles / zero affected package IDs or matching advisories**.
The [CycloneDX 1.6 inventory](dependencies/2026-11-15-net10/inventory.cdx.json),
[lock hashes](dependencies/2026-11-15-net10/lock-sha256.json),
[zero-match report](dependencies/2026-11-15-net10/matched-vulnerabilities.json)
and [advisory-feed provenance](dependencies/2026-11-15-net10/advisory-feed-provenance.json)
retain the captured feed identity and source/lock evidence. The earlier
`2026-11-15` snapshot is unchanged. Both snapshots include build/test/RID
dependencies; neither asserts final artifact reachability or audits every native
codec and OS package.

Validation of the final implementation and matching delivery candidates:

- All 25 projects passed a clean locked restore and strict transitive NuGet
  audit, with warnings treated as errors, auditing at low severity and above,
  and no advisory suppression or audit-warning exception.
- Both solution configurations built without warnings. The Windows GUI was
  cross-compiled from Linux; Windows execution was not tested.
- Canonical locked `./build.sh --backend` and packaging passed for `linux-x64`,
  `linux-musl-x64` and `linux-musl-arm64`, including self-contained runtime
  10.0.15, actual updaters, expected native architectures, bundled MediaInfo and
  matching dependency notices. Libraries/tests remain framework-dependent.
- Offline suites: **4,551 passed / 103 not-executed TRX records**: Core 3,460,
  Common 691, API/authentication 273, Host 49, Mono 52, Libraries 1, Update 17
  and API-unit 8, including five hosted body-binding cases in the Host suite.
  Not-executed records comprise Core 55, Common 38, Host 4 and Mono 6; runner
  console summaries report 99 skips. No cause is assigned to that reporting
  difference.
  Eight coverage cases produced real XML reports; six fake-runner shell cases
  preserved ordinary/coverage exit statuses.
- Current and genuine legacy packaged backup restoration: **51/51**. A genuine
  P09 .NET 6 baseline then upgraded to .NET 10 in the **same disposable home**:
  **24/24**, preserving authors/books/recordings, two recording parts and
  narrators, user identities and credential encodings, config/API key, roots,
  history and media hashes. Packaged lockout: **50/50**.
- Delivered native probes: glibc-x64 **35**, musl-x64 **55**. Exact-image Docker
  validation: **54/54**, using the actual repository Dockerfile and S6 service,
  with healthy HTTP on 10.0.15, raster/PDF/EPUB/audio/Calibre adapters, native
  SQLite integrity, MediaInfo child execution, notices and graceful exit 0.
- ARM64 native probing: **33 passed** under explicit QEMU. Default-environment
  HTTP startup failed twice on unchanged candidates: target signal 11, then a
  managed AccessViolation during Dapper DynamicClass parameter setup for
  InsertMissingQualityDefinitions followed by QEMU target signal 6. Both failed
  results are retained. A private compatibility experiment with
  `DOTNET_TieredCompilation=0` and `DOTNET_EnableWriteXorExecute=0` passed
  **53/53**, including the 33 native checks, HTTP on runtime 10.0.15, preserved
  API key and graceful shutdown. Candidate hashes remained unchanged and owned
  containers were removed. These flags are not shipped; default ARM64 HTTP
  startup is not a passing gate.

Microsoft's [.NET 10 support notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#notes)
exclude QEMU for running .NET applications. The emulated results do not identify
the failures' cause or establish default behavior on native ARM64 hardware.
The next acceptance check requires default startup on a **native ARM64 runner**.

P01's implementation and x64/musl-x64 acceptance are complete. It remains
**In progress** solely for default ARM64 startup acceptance on that runner. P09
remains **In progress** for its broader managed/database/native compatibility
surface. Migration-engine and SQLite provider/engine changes remain P12/P13;
complete integration/platform coverage and release/source identity remain
P21/P22/P25. Windows/macOS execution and the missing Intel macOS image asset
are still P22 gates. Legacy Azure jobs are manual-only/default-off; local
canonical-build evidence does not assert remote GitHub/Azure CI execution.
All validation used private copies and synthetic data; no live development or
production home, service, credentials or media was changed.

## Reproducing the dependency gates

Use the SDK selected by `global.json`; older SDKs do not implement this audit
policy. From the repository root:

```sh
dotnet restore src/Readarr.sln --locked-mode
dotnet restore src/NzbDrone/Readarr.csproj --locked-mode -p:EnableWindowsTargeting=true
EnableWindowsTargeting=true dotnet list src/Readarr.sln package --vulnerable --include-transitive --format json --no-restore
bash scripts/test-test-runner.sh
```

The Windows-targeting property permits evaluating the GUI dependency graph from
Linux; it does not test a Windows executable. The inventory aggregates locked
NuGet dependencies across build/test/RID variants, includes exact package
SHA512s, and records the source base, modified working tree and lock-manifest
digest. It is a dated dependency snapshot, not a release SBOM or artifact
reachability claim. The native runtime, OS packages and every embedded codec
are outside the NuGet inventory scope.

CI installs the .NET 10 SDK selected by `global.json`, checks both Core/ASP.NET
10 runtimes, and enforces locked restores before
and during build. The test wrapper propagates nonzero runner statuses; six
isolated fake-runner cases cover ordinary and coverage invocations. Legacy
`--enable-extra-platforms-in-sdk` fails explicitly rather than modifying an
installed SDK. Actual remote CI was not run for this local change.

## Compatible maintenance refresh, 2026-11-24

Commit `54fda4e89` refreshes the locked .NET 10 and Yarn Classic graphs without
requiring new licenses, changing native provider packages or jumping major
framework generations. The dated [inventory and audit records](dependencies/2026-11-24-maintenance/summary.json)
include exact managed package hashes, the checked lockfiles, all 25 audited
projects (including the Windows GUI with explicit Windows targeting), and the
frontend before/after audit counts. The initial and refreshed NuGet graphs both
reported **zero known vulnerable packages**; the frontend audit moved from
**183 reports (11 low, 62 moderate, 110 high)** to **zero**. Audit results describe
those registries' reported advisories at the time of the check, not a complete
code or native-library review.

| Managed package | Previous | Updated |
| --- | --- | --- |
| Dapper | 2.0.151 | 2.1.100 |
| Diacritical.Net | 1.0.4 | 1.0.5 |
| Newtonsoft.Json | 13.0.3 | 13.0.4 |
| Polly | 8.5.2 | 8.9.1 |
| IPAddressRange | 6.1.0 | 6.3.0 |

Frontend updates include Lodash 4.18.1, qs 6.17.1, Moment 2.31.0, PostCSS
8.5.33, the current compatible Babel 7 patch lines, and TypeScript ESLint
8.74.0. Affected transitive resolutions were refreshed within their declared
ranges. The unused postcss-url and rimraf dependencies were removed; the clean
script uses built-in Node filesystem operations. Static color transforms retain
the prior computed colors; dynamic colors now use native CSS `color-mix`.
The obsolete color-function plugin is no longer required. No audit exceptions
or forced incompatible major resolutions were added.

Locked restore, the frozen Yarn install, offline Core regression tests,
frontend tests, both TypeScript projects, lint and the production-style Vite
verification passed in the isolated dependency checkout. Integrated validation
is recorded separately in [local validation](local-validation.md). P09 remains
**In progress** for the broader compatibility and native-platform gates;
P08's no-commercial-license implementation remains intact.

## Supported-line and major-version review, 2026-11-24

The [dated registry review and lock inventory](dependencies/2026-11-24-major-lines/summary.json)
records the exact dependency commit `987b8a70e`, the isolated test checkout,
the public registry results for all 90 npm direct IDs and 70 public NuGet
central pins, and the excluded custom-feed Mono.Posix pin. It distinguishes
the registry's highest stable/dist-tag version from the selected compatible
line; neither a newer version number nor the donor's choice is by itself a
compatibility decision. The [individual version rows](dependencies/2026-11-24-major-lines/registry-lines.json)
retain the before/selected/latest versions, resolved managed packages and
available npm license, peer and engine metadata.

### Adopted lines

| Dependency | Before | Selected | Reason |
| --- | --- | --- | --- |
| Ical.Net | 4.3.1 | 5.2.4 | The [maintainer identifies v4 as unsupported](https://github.com/ical-org/ical.net#icalnet-versions). The v5 date-only API is adapted to retain the original recording UID, release date, exclusive next-day end, current title/overview and tag filtering. NodaTime resolves to 3.2.2. |
| NUnit | 3.14.0 | 4.6.1 | Same maintained intermediate major as pinned Sonarr v5; [classic assertion compatibility was restored in 4.5](https://docs.nunit.org/articles/nunit/writing-tests/assertions/assertions.html). Only the removed formatted failure-message overload and a legacy collection-assert namespace needed test-only edits. |
| AutoFixture | 4.17.0 | 4.18.1 | Latest stable compatible test-data generator. |
| Moq | 4.17.2 | 4.21.0 | Stable test-library release; exact restored BSD-3-Clause package metadata lists Castle.Core and ordinary framework dependencies, not a new commercial license package. |
| System.Buffers / System.Memory / System.ValueTuple | 4.6.0 / 4.6.2 / 4.6.1 | 4.6.1 / 4.6.3 / 4.6.2 | Same-line managed compatibility/pin maintenance; .NET 10 and all native-asset versions unchanged. |
| @microsoft/signalr | 6.0.25 | 10.0.11 | Latest registry 10.x browser client, matching the server's supported .NET generation; [Microsoft ties client support to the runtime lifecycle](https://learn.microsoft.com/en-us/aspnet/core/signalr/client-features?view=aspnetcore-10.0). |
| @types/node | 20.16.11 | 24.19.2 | Type declarations now match the pinned Node 24 toolchain; do not jump to Node 26 types. |
| fuse.js | 6.6.2 | 7.5.0 | Same major as the pinned Sonarr v5 global search; exercised the actual Bookshelf search worker, including typo ranking, nested tags, indices and limit. |
| filesize | 10.1.6 | 11.0.35 | Normal binary size/bit formatting is unchanged. The newer library throws on infinite values, so the wrapper explicitly returns blank for all non-finite input. |
| react-virtualized | 9.21.1 | 9.22.6 | Stay on the existing API major while using a version whose peer contract includes React 18. |
| react-focus-lock | 2.9.4 | 2.13.7 | Same-line modal focus and React 18 compatibility maintenance. |
| eslint-plugin-react-hooks | 4.6.2 | 5.2.0 | ESLint 8-compatible intermediate major used by the donor; retain the existing lint rules and configuration. |

Compatible same-line frontend maintenance also updates TanStack Query, React
Router, React 18 and Lodash declarations, React lazy loading, Vite 8,
its React and CSS modules plugins, Autoprefixer, core-js, CSS-module declaration
tooling and the compatible ESLint 8 / Prettier 2 plugin versions. No forced
incompatible resolutions were added. The original unused Core project
RestSharp reference is removed: the production lock graph no longer resolves
it, while the legacy integration test client continues using its existing
106.15.0 API.

The [restored NuGet metadata](dependencies/2026-11-24-major-lines/reviewed-nuget-metadata.json)
retains the reviewed licenses and declared dependencies. New distributed
[Ical.Net and NodaTime notices](../LICENSES/managed/README.md) come from the
exact Git commits identified by their package metadata, with package and
notice hashes in [sources.json](../LICENSES/managed/sources.json). No existing
native engine, raster/PDF asset, .NET runtime, SDK or corresponding notice pin
was changed.

### Reviewed and deliberately retained

Versions below are the registry observations on this date, not automatic
upgrade requirements. The machine-readable review records individual IDs
even where the compatibility decision is shared by a coupled family.

| Retained managed line | Registry candidate | Decision |
| --- | --- | --- |
| DryIoc.dll 5.4.3; DryIoc DI adapter 6.2.0 | 6.0.0; 8.0.0 | Coupled container/registration and lifetime migration; preserve the newly verified host singleton and restart behavior. The donor retains the same versions. |
| FluentAssertions 5.10.3 | 7.2.2 open-source line; 8.11.0 latest | Actual 7.2.2 build trial failed existing time-precision, collection equivalence and member-projection assertion APIs across several fixtures. Do not rewrite or weaken assertions in a dependency refresh. [The vendor states v7 remains open source and v8+ introduces paid commercial-use terms](https://fluentassertions.com/introduction#licensing). A separate test migration can target v7. |
| FluentValidation 9.5.4 | 12.2.0 | Custom validator and cascade/API migrations span several major versions ([upgrade guides](https://docs.fluentvalidation.net/en/latest/upgrading-to-12.html)); the donor also retains 9.5.4. |
| MailKit 4.18.2 | 5.0.0 | Separate SMTP protocol, auth and TLS behavior review with owned server fixtures before upgrading an actual notification integration. |
| Microsoft ASP.NET/Extensions/System 10.0.15 lines | 11.0.0 | Move these together with a deliberate .NET runtime/SDK migration, not into the pinned .NET 10 delivery ABI. |
| Microsoft.Data.SqlClient 2.1.7; IdentityModel JSON Web Tokens/JWT 6.35.0 | 7.1.2; 8.23.0 | These central declarations do not resolve in the restored locks; they are not vulnerable runtime packages. Avoid introducing a new SQL/auth dependency solely to update dormant pins. |
| MonoTorrent 2.0.7 | 3.0.2 | The donor changed magnet/torrent reads to `InfoHashes.V1OrV2`. A direct substitution can admit a different hash identity, especially v2-only and hybrid magnets. Require scoped client/receipt/blocklist compatibility checks before changing this acquisition contract. |
| NLog 5.5.1 / NLog.Extensions.Logging 5.5.0 | 6.2.2 / 6.3.0 | [NLog 6 migration](https://github.com/NLog/NLog/wiki/NLog-6.0---List-of-major-changes) affects logging configuration and related targets. Preserve redaction, database logging, shutdown and profiler behavior; the donor stays on NLog 5. |
| Npgsql 10.0.3 | 10.0.4 same-line; 11.0.0 latest | Existing pinned provider, exact notice and PostgreSQL migration acceptance remain intact; validate a provider update against the full database gate and replace its notice together. |
| NUnit 4.6.1 | 5.0.0 | Adopt the bounded compatible intermediate release; NUnit 5 has a further [breaking-change list](https://docs.nunit.org/articles/nunit/release-notes/framework.html). |
| RestSharp 106.15.0 (test client) | 114.1.0 | [Modern RestSharp migration](https://restsharp.dev/migration) replaces the old request/client contract used throughout legacy integration tests. Remove unused production reference now; defer rewriting the remaining test harness. |
| Selenium.Support 3.141.0 / ChromeDriver 134.0.6998.16500 | 4.54.0 / 158.0.8127.8800 | Vendor API/driver pairing is a legacy automation migration. Current owned browser gates use Playwright; do not claim the older Selenium integration suite is updated by installing a driver alone. |
| StyleCop.Analyzers 1.1.118 | 1.3.180 | Isolated actual compile trial produces 47 errors (including cascading build failures) on established source formatting. Defer the analyzer/rules migration; no global suppression or unrelated reformatting. |
| Swashbuckle annotations/generator 6.6.2 | 6.9.0 same-line; 10.3.1 donor; 11.0.1 latest | Separate OpenAPI object/source-contract and static-document regeneration comparison across the major lines. Keep the just-verified v1/v2 schemas stable in this slice. |
| System.IO.Abstractions / TestingHelpers 17.0.24 | 17.2.26 same-line; 23.1.0 latest | Must migrate the matching production/test filesystem wrappers and prove import, rename, deletion and safe backup semantics; do not update one half independently. |
| Mono.Posix.NETStandard Servarr pin | Custom feed | Keep the existing native-image ABI and architecture validation. |

| Retained frontend line | Registry candidate | Decision |
| --- | --- | --- |
| Babel 7 core/parser/plugin/presets | Babel 8 | Coupled Babel configuration, Vite compatibility transform plugins and test harness; donor remains on Babel 7. |
| Font Awesome 6 icon family / React wrapper 0.2 | Icon family 7.3.1 / wrapper 3.5.0 | Review icon mappings, CSS, wrapper batching/props and all rendered controls together instead of a blind major jump; donor uses a different icon library. |
| SignalR JS 10.0.11 | 11.0.0 | Chosen supported .NET 10 line; a .NET 11 client is unnecessary for this runtime. |
| React and React DOM 18 / React 18 declarations | React 19.3.x | Existing drag/drop, Popper and document-title peer contracts are not all compatible. Donor stays on React 18. |
| ESLint 8 / config-prettier 8 / plugin-prettier 4 / Prettier 2 | ESLint 10 / config 10 / plugin 5 / Prettier 3 | Coordinated flat-config/toolchain migration; existing config and legacy plugins must be validated before any major jump. |
| React Hooks lint plugin 5.2.0 | 7.1.1 | Intermediate major keeps configured hooks rules and ESLint 8; modern compiler-oriented rule migration is separate. |
| eslint-plugin-simple-import-sort 12 | 14.0.0 | Lint-rule migration, not a production dependency or current audit finding. |
| jQuery 3.7.1 | 4.1.0 (3.8.0 same-line) | Preserve legacy AJAX/DOM APIs while reducing their usage; donor also retains 3.7.1. |
| postcss-mixins 9 / postcss-nested 6 | 12.1.2 / 9.0.1 | CSS compilation changes need stylesheet/warning and generated declaration comparison; donor retains both versions. |
| React DnD 14 / HTML5 and touch backends / multi-backend 6 | DnD/backends 16 / multi-backend 9 | Coupled drag/drop and touch migration (donor uses dnd-kit). Existing peer warnings are known; do not force mixed versions. |
| react-google-recaptcha 2 | 3.1.0 | Legacy external integration; validate its provider/UI contract with owned fixtures first. |
| react-popper 1 | 2.3.0 | Popper major/render-prop migration across menus, selects and tooltips is separate; donor uses Floating UI. |
| react-redux 7 / Redux 4 / redux-actions 2 / redux-thunk 2 / reselect 4 | 9.3.1 / 5.0.1 / 3.0.3 / 3.1.0 / 5.3.0 | Coupled persistent store, middleware, selector and import/ESM migration. The donor replaces this store with Zustand; preserve Bookshelf's existing state semantics. |
| react-slider 1 / react-tabs 4 | 2.0.6 / 6.1.1 | Component and keyboard behavior changes need explicit quality-setting and detail-tab proof; donor retains the original majors. |
| react-virtualized 9.22.6 | 10.0.1 | Selected React 18-compatible 9.x patch rather than changing grid/virtualization APIs. |
| Stylelint 15.10.3 / stylelint-order 6 | 18.1.0 / 8.1.3 | Even the 15.11.0 same-line trial added four shorthand-rule failures beyond the three existing CSS order findings; retained the original line without changing unrelated styles. |
| TypeScript 5.9.3 | 7.0.2 | Keep parser, CSS declaration generator and strict TS project compatibility; donor also uses TypeScript 5.9. |

### Local evidence

Locked restores of the solution and explicit Windows-targetable GUI graph
pass, and frozen Yarn Classic installation passes. NuGet's 25-project audit
reports **zero** vulnerable packages; Yarn reports **zero** advisory findings
over 1,344 dependencies. See the [audit summary](dependencies/2026-11-24-major-lines/summary.json),
[managed lock inventory](dependencies/2026-11-24-major-lines/inventory.cdx.json),
[lock hashes](dependencies/2026-11-24-major-lines/lock-sha256.json),
[NuGet audit](dependencies/2026-11-24-major-lines/nuget-audit.json) and
[Yarn audit summary](dependencies/2026-11-24-major-lines/yarn-audit-summary.json).
The direct-registry review records versions rather than claiming every new
major is supported or licensed for this project.

The isolated dependency checkout compiled the full solution and passed the
offline source suites: Core 4,832, Common 855, API 102, Host 193 and Updater 21
(6,003 passed total; existing skips, including PostgreSQL cases that require
the separately owned socket). The calendar fixture passed on both the older
and updated iCalendar library. The retained frontend graph passed 142 tests,
both TypeScript configurations, ESLint and Vite verification. The actual
owned browser fixture against the previous backend with the rebuilt frontend
passed 125 checks across desktop and phone, including real SignalR negotiation,
a forced reconnect and exactly one resynchronization of authors, commands
and active page; modal focus, keyboard interaction, virtual table reordering
and provider isolation also passed. That is frontend compatibility evidence,
not exact final integrated-package provenance.

No live application home, actual provider, user credentials or external
notification service were used. NuGet/Yarn vulnerability reports are registry
advisory snapshots, not complete native/library security audits. P09's
remaining major-line migrations and native ARM64/release acceptance are not
closed by these changes.

### Integrated virtualization compatibility check

The first combined exact `bc019a705` package reproduced an alphabet jump
regression with the updated `react-virtualized` 9.22.6: the Z control was
rendered and clicked, but the controlled grid remained at its initial scroll
position. The identical typed library UI on the prior dependency graph
successfully jumped and restored scroll. Commit `ce93fb9df` adapts the table
and author/book poster/overview grids to the dependency's public cell-offset
and external WindowScroller callback, rather than manipulating controlled
Grid scroll state. A separate maintained-dependency overlay passed 62 expanded
browser checks, with desktop top/end jumps and desktop/phone view restoration,
and two focused unit tests, both TS projects, ESLint and Vite verification.
The final lock graph is unchanged, so the dated inventory above remains exact.
The initial failed receipt is retained; exact corrected package replay is
recorded in local validation.

## Managed dependency modernization, 2026-11-25

The [dated managed inventory](dependencies/2026-11-25-managed/summary.json)
records the locked graph, official NuGet registry metadata for all 70 public
central pins and the isolated source-test receipt. The custom Servarr
Mono.Posix pin remains excluded from the public registry comparison. This
slice does not change the pinned .NET 10 runtime or SDK, native provider,
MediaInfo, SQLite, Magick.NET or PdfPig delivery versions.

| Dependency | Before | Selected | Decision |
| --- | --- | --- | --- |
| FluentAssertions | 5.10.3 | 7.2.2 | Adopt the last open-source major; the [vendor's licensing guidance](https://fluentassertions.com/introduction#licensing) distinguishes v7 from the commercial-use terms introduced in v8. |
| Npgsql | 10.0.3 | 10.0.4 | Current same-major stable patch; preserve the .NET 10 provider and verify PostgreSQL migrations and repository behavior. |
| System.IO.Abstractions and TestingHelpers | 17.0.24 | 23.1.0 | Updated together to the latest stable registry line. Migrate deprecated factory methods to `New` and return `Stream` from the internal disk provider, matching the new library wrapper. |

FluentAssertions now uses explicit 20 ms precision where its former default
applied, retains the explicitly requested audio precision, and compares typed
collections and predicates rather than legacy object comparisons. The first
full intermediate regression exposed six ineffective `Should().Equals(...)`
expressions as exceptions; the corrected assertions use `Be(...)` and enforce
the original expected file counts. Its [initial receipt](dependencies/2026-11-25-managed/initial-intermediate-test-result.json)
is retained; no assertions were suppressed. Three new cases exercise
real binary reads, write/truncation and missing-file behavior through the
filesystem wrapper. The existing backup, import and receipt tests cover the
`Stream` change; the acquisition delivery probe also migrates its directory
factory and references the three type-forwarded interface/wrapper assemblies
from the candidate itself, rather than adding probe-only packages. The Media
probe still returns FileStream instances, which satisfy the Stream contract.

The expanded IO package graph includes the split TestableIO 23.1.0 abstraction
and wrapper assemblies and Testably.Abstractions.FileSystem.Interface 11.1.0.
All are MIT-licensed according to restored package metadata. The new distributed
[notices and source manifest](../LICENSES/managed/README.md) preserve upstream
bytes, exact package checksums and the identified repository revisions; Npgsql
10.0.4's PostgreSQL license is similarly pinned. The independently restored
solution and Windows-targetable GUI graphs initially had 12 central declarations
that did not resolve in any lockfile. Removing those declarations alone changed
no lockfile, so dormant SQL client, JWT, buffer, memory and framework pins do
not introduce unrelated package dependencies. Actual resolved versions after
subsequent upgrades are recorded in the registry review.

### Remaining managed lines

| Retained dependency | Registry candidate | Decision |
| --- | --- | --- |
| DryIoc 5.4.3 / adapter 6.2.0 | 6.0.0 / 8.0.0 | Coupled container/lifetime review; the adapter 8 package declares .NET 11-generation DI abstractions. Keep the tested .NET 10 host registration behavior. |
| FluentValidation 9.5.4 | 12.2.0 | Custom validators, cascade behavior and several removed APIs require a separate domain validation migration. The donor also retains 9.5.4. |
| MailKit 4.18.2 | 5.0.0 | Package metadata changes MimeKit to MimeKit.Core 5; require owned SMTP/auth/TLS behavior proof. |
| Microsoft ASP.NET, Extensions and System 10.0.15 | 11.0.0 | Move runtime-generation dependencies together, not piecemeal into the .NET 10 package. |
| MonoTorrent 2.0.7 | 3.0.2 | Donor changes torrent/magnet identity to `InfoHashes.V1OrV2`; v2-only and hybrid receipt, client and blocklist semantics require their own acquisition migration. |
| NLog 5.5.1 / extensions 5.5.0 | 6.2.2 / 6.3.0 | Keep logging configuration, redaction, shutdown and profiler semantics; the donor stays on v5. |
| NUnit 4.6.1 | 5.0.0 | Separate next-major assertion/runner behavior migration; retain the already-proven NUnit 4 major. |
| Npgsql 10.0.4 | 11.0.0 | Provider major and coupled runtime/repository changes need distinct migration validation. |
| RestSharp 106.15.0 | 114.1.0 | Only legacy test clients retain the obsolete request/client interface; production graph does not resolve it. Rewrite the test harness separately. |
| Selenium.Support 3.141.0 / ChromeDriver 134 | 4.54.0 / 158 | Legacy automation migration rather than a drop-in driver update; the maintained owned browser gates use Playwright. |
| StyleCop.Analyzers 1.1.118 | 1.3.180 | A real isolated compile of the newer stable version produced 47 total errors including build cascades. The focused analyzer formatter trial touched 189 C# files: 119 whitespace-only, but 70 token changes. A second complete compile still showed 22 distinct direct diagnostics including inherited parameter names. Such names can affect named argument contracts; defer deliberate migration rather than applying broad mechanical renames or suppressions. |
| Swashbuckle 6.6.2 | 6.9.0 same-line / 11.0.1 latest | OpenAPI source/static schema and operation contract comparison is separate from this filesystem/provider/test update. |
| Mono.Posix.NETStandard 5.20.1.34-servarr22 | Custom feed | No native POSIX ABI replacement without architecture-specific acceptance. |

The [registry review](dependencies/2026-11-25-managed/registry-lines.json)
retains each before/selected/latest version and [reviewed package metadata](dependencies/2026-11-25-managed/reviewed-nuget-metadata.json).
Neither the registry's highest stable version nor Sonarr v5's choices alone
establish compatibility. FluentAssertions 8 remains unadopted because of the
commercial terms; no license key or paid library was introduced.

### Managed validation

Both the solution and explicit Windows-targeted GUI graph passed locked restore.
The clean isolated candidate passed all eight suites: 6,146 total cases
(Core 4,857, Common 855, API 102, Host 193, Updater 21, native Linux disk 36,
owned PostgreSQL migrations 23 and focused PostgreSQL repository cases 59).
There were no failing tests; 100 cases were not executed (platform/explicit
skips). The owned PostgreSQL instance stopped and the source stayed unchanged.
The [audit](dependencies/2026-11-25-managed/nuget-audit.json) reports zero known
vulnerable packages across all 25 projects. The [CycloneDX inventory](dependencies/2026-11-25-managed/inventory.cdx.json)
uses exact lock hashes; [SHA-256 file hashes](dependencies/2026-11-25-managed/lock-sha256.json)
identify the input locks and central/SDK declarations. The [source-test receipt](dependencies/2026-11-25-managed/source-tests.json)
records each executed offline suite, platform skips, private PostgreSQL
migration and focused repository cases and native Linux disk tests. No live
home, external provider, credentials or actual download client was used.
This audit is a registry advisory snapshot, not a complete security or native
code audit; integrated native x64 package replay and native ARM64 execution are
separate gates. P09 remains in progress for outstanding coupled migrations and
native release acceptance.
