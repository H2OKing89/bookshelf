# Bookshelf platform support

Bookshelf's **delivery targets** are the Linux runtime identifiers below. A
runtime identifier (RID) specifies the application architecture and C library;
a successful managed-code compilation or an available upstream native asset
does not constitute a supported delivery package.

## Delivery matrix

All three target self-contained .NET 10 (`net10.0`). Use the current SDK pin in
[global.json](../global.json) and the corresponding runtime patch.

| Package RID | Intended runtime environment | Container target | Evidence status |
| --- | --- | --- | --- |
| `linux-x64` | Linux x86-64, **glibc 2.34 or newer** | Not the Alpine container package | Previous native x64 package/startup/upgrade/media checks passed. Revalidate each changed artifact. |
| `linux-musl-x64` | Linux x86-64, musl (including the repository's Alpine 3.24 container base) | `linux/amd64` | Previous native x64 musl package and actual-image checks passed. Revalidate each changed artifact. |
| `linux-musl-arm64` | Linux AArch64, musl | `linux/arm64` | Cross-publication, ARM64 native asset inspection and earlier explicit emulation probes are recorded. **Default startup and full delivery acceptance on native ARM64 hardware remain open.** |

The glibc floor is imposed by the pinned native SQLite engine; see the
[SQLite native asset lock](../scripts/sqlite-native.lock.json). It is a minimum,
not a claim that every distro at that version has all the required system
dependencies. Self-contained .NET includes the runtime, not a Linux kernel,
C library or every distribution library. The host must provide the normal system
dependencies of the bundled .NET runtime. Musl and glibc apphosts are not
interchangeable even on the same CPU architecture. Do not install a glibc package
into the Alpine container.

These are build/delivery targets, **not** a list of signed, newly published or
fully accepted releases. Historical evidence is recorded in
[local validation](local-validation.md#p13-verification-2026-11-17) and the
[dependency audit](dependency-audit.md); package hashes and tests must match the
actual candidate being released. The first unsigned local release-candidate
format currently accepts only native `linux-x64` on the `develop` channel.
See [release policy](release-policy.md#prepare-an-unpublished-candidate).

## Package contents

A complete package includes the self-contained application and updater
runtime, compiled UI, managed provider assemblies, matching native
`libe_sqlite3.so` (pinned SourceGear SQLite asset), Mono POSIX helper, native
Magick raster codecs, and bundled MediaInfo CLI. The separate MediaInfo helper
is fully static, including musl and its codec dependencies: the two x64
application RIDs use the same `linux-musl-x64` helper, while ARM64 uses
`linux-musl-arm64`. It does not require a host-installed MediaInfo CLI.

The package contains project/dependency license notices and a MediaInfo source
lock, version, build identifier, checksums and license notices beside its
binary. See [MediaInfo packaging](mediainfo.md) and the
[managed dependency notices](../LICENSES/managed/README.md).
A bare `dotnet build` output does not qualify as a delivery package.

## Build and inspect

Run delivery builds from Linux. Cross-building MediaInfo requires a native
matching builder or Docker Buildx with matching architecture/emulation support
as described in [MediaInfo packaging](mediainfo.md). For all three delivery
targets from one stable checkout:

```sh
RestoreLockedMode=true ./build.sh --backend --frontend
for rid in linux-x64 linux-musl-x64 linux-musl-arm64; do
  ./build.sh --packages --runtime "$rid" --framework net10.0
done
```

For only one RID, pass both `--runtime RID` and `--framework net10.0` to the
backend and packaging commands. Backend builds without a selector publish all
three RIDs; packaging without a selector preserves the existing
`linux-musl-x64` default. Each package is at
`_artifacts/<rid>/net10.0/Readarr`.

The local validator can inspect a foreign-architecture package **without
executing it**:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-musl-arm64/net10.0/Readarr \
  --checks package
```

The static package check verifies the supported RID, self-contained runtime
patch, ELF machine and app/updater loader family, static helper, executable
permissions, pinned SQLite native bytes and managed provider, bundled native
raster codec presence, MediaInfo checksums/source lock/helper RID, dependency
license notices and unchanged input files. It cannot demonstrate that the
foreign apphost or its native libraries run correctly. Run the runtime and media
checks on the corresponding **native architecture and libc** instead.

The repository [Dockerfile](../docker/Dockerfile) consumes the matching musl
publication and UI for `linux/amd64` or `linux/arm64` and builds the static
MediaInfo helper for that architecture. The [optional native Docker
check](local-validation.md#optional-native-docker-check) runs the actual image
on a local matching-architecture Docker daemon. A successful cross-image build,
QEMU execution or a run with temporary JIT compatibility flags does not establish
unmodified native ARM64 startup.

## Unsupported delivery paths

The build script rejects other RIDs (including `linux-arm64` on glibc,
`linux-x86`, and 32-bit ARM), Windows, macOS and FreeBSD packages, and
the historic extra-platform and desktop-installer switches. It also rejects
non-.NET-10 frameworks and malformed target selectors before removing output
directories or invoking build tools. The Dockerfile rejects non-Linux and
unsupported container targets.

Cross-platform source projects and some upstream runtime/native options may
remain in the tree for development or future work. In particular, the SQLite
package includes a glibc ARM64 native asset, but there is no Bookshelf delivery
RID for it. None of these imply a working Windows installer, macOS application,
FreeBSD package or native glibc ARM64 release. Adding one would require a
separate native dependency, packaging and execution review, not SDK patching.

## Release acceptance

For every proposed RID and exact package/image to be distributed:

- Build backend and UI from the same unchanged source identity using the
  pinned SDK, dependencies and helper recipe; verify architecture, libc,
  required native assets, executable modes, checksums and license notices.
- Run the [local delivery checks](local-validation.md) on its native
  architecture and libc in fresh synthetic homes, including HTTP startup and
  graceful stop, database creation/backup/restore, a genuine older-package
  upgrade, media metadata and representative audiobook codecs/parts,
  acquisition/import/rename and preservation of library identities, settings
  and credentials.
- For container targets, validate the actual matching-architecture image and
  S6 startup/shutdown, storage and media behavior.
- Confirm source/artifact provenance and integrity. Keep complete validation
  receipts for each exact artifact, and never infer native runtime acceptance
  from static cross-architecture inspection.
- Follow the [release policy](release-policy.md). In-app update feeds and
  replacement remain intentionally blocked; a candidate is not a signed
  distributable release. ARM64 native acceptance, supported-RID candidate
  staging beyond glibc x64, signing/publishing and broader release gates
  remain incomplete.

Local validation does not require GitHub Actions. These checks qualify the
specific artifacts tested; they do not change existing application homes or
authorize a release.
