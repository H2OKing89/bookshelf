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
