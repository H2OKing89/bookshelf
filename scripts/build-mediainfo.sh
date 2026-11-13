#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_dir=$(cd -- "$script_dir/.." && pwd)
# shellcheck source-path=SCRIPTDIR
# shellcheck source=mediainfo.lock
source "$script_dir/mediainfo.lock"

case "$(uname -m)" in
    x86_64|amd64) host_arch=x64 ;;
    aarch64|arm64) host_arch=arm64 ;;
    *) echo "Unsupported MediaInfo architecture: $(uname -m)" >&2; exit 1 ;;
esac
if [[ "$(uname -s)" != Linux ]]; then
    echo "Bundled MediaInfo currently supports Linux builds only." >&2
    exit 1
fi
if compgen -G '/lib/ld-musl-*.so.1' >/dev/null; then
    host_rid="linux-musl-$host_arch"
else
    host_rid="linux-$host_arch"
fi

runtime=$host_rid
output_dir=
jobs=${MEDIAINFO_BUILD_JOBS:-4}
while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime) runtime=${2:?Missing runtime}; shift 2 ;;
        --output) output_dir=${2:?Missing output directory}; shift 2 ;;
        *) echo "Usage: $0 [--runtime RID] [--output DIRECTORY]" >&2; exit 1 ;;
    esac
done
case "$runtime" in
    # The CLI runs in its own process: a static musl helper also serves glibc
    # applications without inheriting the build host's minimum glibc version.
    linux-x64) runtime=linux-musl-x64 ;;
    linux-musl-x64|linux-musl-arm64) ;;
    *) echo "Unsupported MediaInfo runtime: $runtime" >&2; exit 1 ;;
esac
if [[ ! "$jobs" =~ ^[1-9][0-9]*$ ]]; then
    echo "MEDIAINFO_BUILD_JOBS must be a positive integer." >&2
    exit 1
fi

cache_dir=${MEDIAINFO_CACHE_DIR:-"$repo_dir/_cache/mediainfo"}
mkdir -p "$cache_dir"
cache_dir=$(cd -- "$cache_dir" && pwd)
bundle_dir="$cache_dir/bundles/$runtime"
fingerprint=$(cat "$script_dir/build-mediainfo.sh" "$script_dir/mediainfo.lock" "$script_dir/mediainfo-licenses/"* "$repo_dir/docker/mediainfo.Dockerfile" | sha256sum | cut -d ' ' -f 1)

copy_bundle() {
    if [[ -n "$output_dir" ]]; then
        mkdir -p "$output_dir"
        cp -a "$bundle_dir/." "$output_dir/"
    fi
    echo "Bundled MediaInfo $MEDIAINFO_VERSION ready: ${output_dir:-$bundle_dir}"
}

if [[ -x "$bundle_dir/mediainfo" && -f "$bundle_dir/build-id" && -f "$bundle_dir/SHA256SUMS" ]] &&
    [[ "$(cat "$bundle_dir/build-id")" == "$fingerprint" ]] &&
    (cd "$bundle_dir" && sha256sum -c SHA256SUMS >/dev/null 2>&1); then
    copy_bundle
    exit 0
fi

if [[ "$runtime" != "$host_rid" ]]; then
    case "$runtime" in
        linux-musl-x64) docker_platform=linux/amd64 ;;
        linux-musl-arm64) docker_platform=linux/arm64 ;;
        *) echo "Build $runtime on a matching glibc host." >&2; exit 1 ;;
    esac
    if ! command -v docker >/dev/null; then
        echo "Building $runtime from $host_rid requires Docker Buildx (and ARM64 emulation or a native builder)." >&2
        exit 1
    fi
    mkdir -p "$cache_dir/bundles"
    container_output=$(mktemp -d "$cache_dir/container.XXXXXX")
    trap 'rm -rf -- "$container_output"' EXIT
    docker buildx build --platform "$docker_platform" \
        --file "$repo_dir/docker/mediainfo.Dockerfile" --target bundle \
        --build-arg "MEDIAINFO_BUILD_JOBS=$jobs" \
        --output "type=local,dest=$container_output" "$repo_dir"
    (cd "$container_output" && sha256sum -c SHA256SUMS >/dev/null)
    [[ "$(cat "$container_output/build-id")" == "$fingerprint" ]]
    rm -rf -- "$bundle_dir"
    mv -- "$container_output" "$bundle_dir"
    trap - EXIT
    copy_bundle
    exit 0
fi

for prerequisite in curl tar xz cmake make c++ sha256sum; do
    if ! command -v "$prerequisite" >/dev/null; then
        echo "MediaInfo build requires $prerequisite; see docs/mediainfo.md." >&2
        exit 1
    fi
done

download() {
    local filename=$1 url=$2 checksum=$3
    mkdir -p "$cache_dir/downloads"
    if [[ ! -f "$cache_dir/downloads/$filename" ]]; then
        curl --fail --location --retry 3 --output "$cache_dir/downloads/$filename.part" "$url"
        mv -- "$cache_dir/downloads/$filename.part" "$cache_dir/downloads/$filename"
    fi
    if ! printf '%s  %s\n' "$checksum" "$cache_dir/downloads/$filename" | sha256sum -c >/dev/null; then
        echo "Checksum mismatch for $filename; remove it from $cache_dir/downloads and retry." >&2
        exit 1
    fi
}
download "$MEDIAINFO_ARCHIVE" "$MEDIAINFO_URL" "$MEDIAINFO_SHA256"
download "$ZLIB_ARCHIVE" "$ZLIB_URL" "$ZLIB_SHA256"

work_dir="$cache_dir/build/$runtime/$MEDIAINFO_SHA256-$ZLIB_SHA256"
source_dir="$work_dir/source/MediaInfo_CLI_GNU_FromSource"
if [[ ! -d "$source_dir" ]]; then
    mkdir -p "$work_dir/source"
    tar -xf "$cache_dir/downloads/$MEDIAINFO_ARCHIVE" -C "$work_dir/source"
    mkdir -p "$source_dir/zlib"
    tar -xf "$cache_dir/downloads/$ZLIB_ARCHIVE" -C "$source_dir/zlib" --strip-components=1
fi

# All libraries come from the checked archives. Disable optional network readers
# and system dependency discovery so local packages cannot change the bundle.
link_flags='-static-libgcc -static-libstdc++'
if [[ "$runtime" == linux-musl-* ]]; then
    link_flags=-static
fi
cmake -S "$source_dir/MediaInfo/Project/CMake/CLI" -B "$work_dir/build" \
    -DCMAKE_BUILD_TYPE=Release -DMEDIAINFO_CLI_STATIC=ON -DBUILD_SHARED_LIBS=OFF \
    -DBUILD_ZENLIB=ON -DBUILD_ZLIB=ON -DZLIB_BUILD_SHARED=OFF -DZLIB_BUILD_TESTING=OFF \
    -DCMAKE_DISABLE_FIND_PACKAGE_CURL=ON -DCMAKE_DISABLE_FIND_PACKAGE_TinyXML=ON \
    -DCMAKE_CXX_FLAGS='-DMEDIAINFO_LIBCURL_NO -DMEDIAINFO_GRAPH_NO -DMEDIAINFO_GRAPHVIZ_NO' \
    "-DCMAKE_EXE_LINKER_FLAGS=$link_flags"
cmake --build "$work_dir/build" --target mediainfo_cli --parallel "$jobs"
"$work_dir/build/mediainfo" --Version | grep -F "v$MEDIAINFO_VERSION"

mkdir -p "$cache_dir/bundles"
staging_dir=$(mktemp -d "$cache_dir/bundles/.${runtime}.XXXXXX")
trap 'rm -rf -- "$staging_dir"' EXIT
install -m 0755 "$work_dir/build/mediainfo" "$staging_dir/mediainfo"
mkdir -p "$staging_dir/licenses"
cp "$source_dir/MediaInfo/LICENSE" "$staging_dir/licenses/MediaInfo.txt"
cp "$source_dir/MediaInfoLib/LICENSE" "$staging_dir/licenses/MediaInfoLib.txt"
cp "$source_dir/ZenLib/License.txt" "$staging_dir/licenses/ZenLib.txt"
cp "$source_dir/zlib/LICENSE" "$staging_dir/licenses/zlib.txt"
cp "$script_dir/mediainfo-licenses/"* "$staging_dir/licenses/"
# Third-party source notices remain available beside the shipped binary.
cp "$source_dir/MediaInfoLib/License.html" "$staging_dir/licenses/MediaInfoLib-third-party.html"
# Some embedded libraries keep their notices in source headers instead of a
# standalone license. Preserve those exact notices with the binary as well.
for notice in aes-gladman/aes.h sha1-gladman/sha1.h sha2-gladman/sha2.h hmac-gladman/hmac.h fmt/format.h tinyxml2/tinyxml2.h tfsxml/tfsxml.h md5/md5.c base64/base64.h; do
    sed -n '1,/\*\//p' "$source_dir/MediaInfoLib/Source/ThirdParty/$notice" > "$staging_dir/licenses/${notice//\//-}.txt"
done
cp "$script_dir/mediainfo.lock" "$staging_dir/sources.lock"
printf '%s\n' "$fingerprint" > "$staging_dir/build-id"
printf 'MediaInfo CLI %s\nzlib %s\nRuntime %s\n' "$MEDIAINFO_VERSION" "$ZLIB_VERSION" "$runtime" > "$staging_dir/VERSION"
(cd "$staging_dir" && sha256sum mediainfo licenses/* sources.lock build-id VERSION > SHA256SUMS)
rm -rf -- "$bundle_dir"
mv -- "$staging_dir" "$bundle_dir"
trap - EXIT
copy_bundle
