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
