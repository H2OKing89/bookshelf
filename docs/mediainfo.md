# Bundled MediaInfo

Bookshelf ships its own MediaInfo CLI at `tools/mediainfo/mediainfo` beside the application. It does not search `PATH` or use a host MediaInfo installation. MAM's reported release information is separate from details read from local audio files.

The current bundle uses [MediaInfo 26.10](https://github.com/MediaArea/MediaInfo/releases/tag/v26.10), including the MediaInfoLib and ZenLib sources from MediaArea's [all-in-one CLI archive](https://mediaarea.net/en/MediaInfo/Download/Source), and [zlib 1.4](https://zlib.net/). Archive URLs and SHA-256 checksums are locked in `scripts/mediainfo.lock`. The build verifies them before extracting anything and never uses upstream's automatic Git dependency downloads.

## Reading media details

New scans and imports automatically probe local audio files. For files already in the library, use **Refresh media info** on the book page. The refresh reads the media and updates only the database's embedded `mediaInfo` document; it does not reimport, rename, or retag files. If probing fails because a file or executable is missing, a file is corrupt, or the process times out, scans retain TagLib's available details and refreshes preserve the existing stored details.

The same action is available through `POST /api/v1/command`:

```json
{"name":"RefreshMediaInfo","bookId":123}
```

Supply either `bookId` or `authorId` to limit the refresh. Omit both to refresh all known audio files with `{"name":"RefreshMediaInfo"}`.

Media details include the codec, profile, additional features, bitrate mode, and individual audio streams, with their source shown as **Read from file · MediaInfo**, **Read from file · TagLib**, or **Reported by MAM**. Explicit xHE-AAC labels and Dolby Atmos features are preserved; USAC alone is shown as USAC rather than assumed to be xHE-AAC. MAM's release details describe the advertised download, while local probes describe the actual file.

## Local development

On a glibc Linux host such as Ubuntu, install Docker Engine with Docker Buildx, then build the helper:

```sh
docker buildx version
./scripts/build-mediainfo.sh --runtime linux-x64
```

The application runtime `linux-x64` uses the same fully static `linux-musl-x64` helper as Alpine x64. This avoids linking the helper to the build host's glibc version; the .NET application keeps its own runtime identifier. Building on Alpine directly requires Bash, a C/C++ toolchain, CMake, curl, and xz (`apk add bash build-base cmake curl xz`).

Then use the usual `dotnet build` or `dotnet test` command. MSBuild copies the prepared helper bundle into application and test output directories. Bundles are cached under `_cache/mediainfo/bundles/linux-musl-x64/` and `_cache/mediainfo/bundles/linux-musl-arm64/`; binaries and downloaded sources are not committed. Cache reuse verifies all bundle checksums and the build recipe identity. Set `MEDIAINFO_BUILD_JOBS` to control compiler parallelism (default: 4).

`./build.sh --backend --runtime linux-x64 --framework net6.0` prepares the matching helper bundle before compiling and publishing Bookshelf. Without a runtime argument, the build prepares all three supported application runtimes: `linux-x64`, `linux-musl-x64`, and `linux-musl-arm64`, sharing the x64 helper between the first two. Cross-building the helpers requires Docker Buildx and a matching native builder or QEMU support for ARM64. The script rejects unsupported runtime identifiers.

An existing cached bundle can also be copied to an isolated development runtime:

```sh
./scripts/build-mediainfo.sh --runtime linux-x64 --output /path/to/runtime/tools/mediainfo
```

This command only places the executable and notices; it does not restart the server or modify media files.

## Docker and release packages

The Docker build compiles a matching musl binary inside an Alpine build stage for each target architecture, then copies only the bundle into the final image. `docker/mediainfo.Dockerfile` exposes the same bundle through its `bundle` target for cross-builds. All Linux packages use fully static helpers, including MediaInfoLib, ZenLib, zlib, the C++ runtime, and musl. The x64 helper works with both glibc and musl application runtimes. No host MediaInfo packages are required.

`build.sh --packages` includes the matching bundle in each package, alongside its version, source lock, checksums, and license notices. CI builds the bundles before publishing .NET outputs and uses the same source recipe for container images.

To update MediaInfo, choose an official stable release, update the archive URL and checksum together, rebuild each supported helper architecture, and verify `--Version`, JSON extraction from representative audiobook formats, and static linking (`readelf -d` should show no dynamic section). Smoke-test the x64 helper on a glibc Linux image as well as Alpine. Keep all bundled license notices. MediaInfo permits redistribution under its [BSD 2-Clause license](https://mediaarea.net/en/MediaInfo/License); ZenLib and zlib have their own included notices.
