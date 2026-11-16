# Dependency security maintenance

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

CI installs SDK 10 and the legacy .NET 6 test runtime into one tool root,
checks both Core/ASP.NET runtime versions, and enforces locked restores before
and during build. The test wrapper propagates nonzero runner statuses; six
isolated fake-runner cases cover ordinary and coverage invocations. Legacy
`--enable-extra-platforms-in-sdk` fails explicitly rather than modifying an
installed SDK. Actual remote CI was not run for this local change.
