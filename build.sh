#! /usr/bin/env bash
set -e

outputFolder='_output'
testPackageFolder='_tests'

#Artifact variables
artifactsFolder="_artifacts";

ProgressStart()
{
    echo "Start '$1'"
}

ProgressEnd()
{
    echo "Finish '$1'"
}

UpdateVersionNumber()
{
    if [ "$READARRVERSION" != "" ]; then
        echo "Updating Version Info"
        ESCAPED_BRANCH_NAME=${BUILD_SOURCEBRANCHNAME//\//\\/}
        sed -i'' -e "s/<AssemblyVersion>[0-9.*]\+<\/AssemblyVersion>/<AssemblyVersion>$READARRVERSION<\/AssemblyVersion>/g" src/Directory.Build.props
        sed -i'' -e "s/<AssemblyConfiguration>[\$()A-Za-z-]\+<\/AssemblyConfiguration>/<AssemblyConfiguration>${ESCAPED_BRANCH_NAME}<\/AssemblyConfiguration>/g" src/Directory.Build.props
    fi
}

LintUI()
{
    ProgressStart 'ESLint'
    yarn lint
    ProgressEnd 'ESLint'

    ProgressStart 'Stylelint'
    if [ "$os" = "windows" ]; then
        yarn stylelint-windows
    else
        yarn stylelint-linux
    fi
    ProgressEnd 'Stylelint'
}

BuildMediaInfo()
{
    local runtime
    local runtimes="${RID:-linux-x64;linux-musl-x64;linux-musl-arm64}"
    local split_runtimes
    IFS=';' read -ra split_runtimes <<< "$runtimes"
    for runtime in "${split_runtimes[@]}"; do
        ./scripts/build-mediainfo.sh --runtime "$runtime"
    done
}

Build()
{
    ProgressStart 'Build'

    rm -rf $outputFolder
    rm -rf $testPackageFolder

    BuildMediaInfo

    local bookshelf_build_commit bookshelf_build_tree bookshelf_build_branch bookshelf_build_dirty
    bookshelf_build_commit=$(git rev-parse HEAD)
    bookshelf_build_tree=$(git rev-parse 'HEAD^{tree}')
    bookshelf_build_branch=$(git branch --show-current)
    bookshelf_build_dirty=true
    if [[ -z "$(git status --porcelain --untracked-files=normal)" ]]; then
        bookshelf_build_dirty=false
    fi
    if [[ ! "$bookshelf_build_branch" =~ ^[a-zA-Z0-9._/-]+$ ]]; then
        bookshelf_build_branch=unknown
        bookshelf_build_dirty=true
    fi
    local -a bookshelf_source_properties=(
        "-p:BookshelfSourceCommit=$bookshelf_build_commit"
        "-p:BookshelfSourceTree=$bookshelf_build_tree"
        "-p:BookshelfSourceBranch=$bookshelf_build_branch"
        "-p:BookshelfBuildDirty=$bookshelf_build_dirty"
    )

    slnFile=src/Readarr.sln

    if [ $os = "windows" ]; then
        platform=Windows
    else
        platform=Posix
    fi

    if [[ -z "$RID" || -z "$FRAMEWORK" ]];
    then
        # Projects share output directories, so publish the solution serially.
        dotnet msbuild -restore $slnFile -maxcpucount:1 -p:BuildInParallel=false -p:Configuration=Release -p:Platform=$platform "${bookshelf_source_properties[@]}" -t:PublishAllRids
    else
        dotnet msbuild -restore $slnFile -maxcpucount:1 -p:BuildInParallel=false -p:Configuration=Release -p:Platform=$platform "${bookshelf_source_properties[@]}" -p:RuntimeIdentifiers=$RID -t:PublishAllRids
    fi

    if [[ "$bookshelf_build_dirty" = false ]] &&
       { [[ "$(git rev-parse HEAD)" != "$bookshelf_build_commit" ]] ||
         [[ "$(git rev-parse 'HEAD^{tree}')" != "$bookshelf_build_tree" ]] ||
         [[ "$(git branch --show-current)" != "$bookshelf_build_branch" ]] ||
         [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; }; then
        echo "Source changed during publication; rebuild from a stable checkout." >&2
        return 1
    fi

    ProgressEnd 'Build'
}

YarnInstall()
{
    ProgressStart 'yarn install'
    yarn install --frozen-lockfile --network-timeout 120000
    ProgressEnd 'yarn install'
}

RunVite()
{
    ProgressStart 'Building frontend with Vite'
    yarn run typecheck
    yarn run build:verify
    ProgressEnd 'Building frontend with Vite'
}

PackageFiles()
{
    local folder="$1"
    local framework="$2"
    local runtime="$3"

    rm -rf $folder
    mkdir -p $folder
    cp -r $outputFolder/$framework/$runtime/publish/* $folder
    cp -r $outputFolder/Readarr.Update/$framework/$runtime/publish $folder/Readarr.Update
    cp -r $outputFolder/UI $folder

    ./scripts/build-mediainfo.sh --runtime "$runtime" --output "$folder/tools/mediainfo"

    echo "Adding LICENSE"
    cp LICENSE.md $folder
}

PackageLinux()
{
    local framework="$1"
    local runtime="$2"

    ProgressStart "Creating $runtime Package for $framework"

    local folder=$artifactsFolder/$runtime/$framework/Readarr

    PackageFiles "$folder" "$framework" "$runtime"

    echo "Removing Service helpers"
    rm -f $folder/ServiceUninstall.*
    rm -f $folder/ServiceInstall.*

    echo "Removing Readarr.Windows"
    rm $folder/Readarr.Windows.*

    echo "Adding Readarr.Mono to UpdatePackage"
    cp $folder/Readarr.Mono.* $folder/Readarr.Update
    if [ "$framework" = "net10.0" ]; then
        cp $folder/Mono.Posix.NETStandard.* $folder/Readarr.Update
        cp $folder/libMonoPosixHelper.* $folder/Readarr.Update
    fi

    ProgressEnd "Creating $runtime Package for $framework"
}

Package()
{
    local framework="$1"
    local runtime="$2"
    case "$runtime" in
        linux-x64|linux-musl-x64|linux-musl-arm64)
            PackageLinux "$framework" "$runtime"
            ;;
        *)
            echo "Unsupported Bookshelf package runtime: $runtime. See docs/platform-support.md." >&2
            return 1
            ;;
    esac
}

PackageTests()
{
    local framework="$1"
    local runtime="$2"

    cp test.sh "$testPackageFolder/$framework/$runtime/publish"

    rm -f $testPackageFolder/$framework/$runtime/*.log.config

    ProgressEnd 'Creating Test Package'
}

# Keep frontend-only tooling available independently of Linux packaging.
host_os=$(uname -s)
case "$host_os" in
    CYGWIN*|MINGW32*|MINGW64*|MSYS*)
        # on windows, use dotnet
        os="windows"
        ;;
    *)
        # otherwise use mono
        os="posix"
        ;;
esac

if [ $# -eq 0 ]; then
    echo "No arguments provided, building everything"
    BACKEND=YES
    FRONTEND=YES
    PACKAGES=YES
    INSTALLER=NO
    LINT=YES
    ENABLE_EXTRA_PLATFORMS=NO
    ENABLE_EXTRA_PLATFORMS_IN_SDK=NO
fi

while [[ $# -gt 0 ]]
do
key="$1"

case $key in
    --backend)
        BACKEND=YES
        shift # past argument
        ;;
    --enable-bsd|--enable-extra-platforms)
        ENABLE_EXTRA_PLATFORMS=YES
        shift # past argument
        ;;
    --enable-extra-platforms-in-sdk)
        ENABLE_EXTRA_PLATFORMS_IN_SDK=YES
        shift # past argument
        ;;
    -r|--runtime)
        if [[ $# -lt 2 || -z "$2" || "$2" == -* ]]; then
            echo "Missing runtime identifier. See docs/platform-support.md." >&2
            exit 1
        fi
        RID="$2"
        shift 2
        ;;
    -f|--framework)
        if [[ $# -lt 2 || -z "$2" || "$2" == -* ]]; then
            echo "Missing package framework. Use net10.0." >&2
            exit 1
        fi
        FRAMEWORK="$2"
        shift 2
        ;;
    --frontend)
        FRONTEND=YES
        shift # past argument
        ;;
    --packages)
        PACKAGES=YES
        shift # past argument
        ;;
    --installer)
        INSTALLER=YES
        shift # past argument
        ;;
    --lint)
        LINT=YES
        shift # past argument
        ;;
    --all)
        BACKEND=YES
        FRONTEND=YES
        PACKAGES=YES
        LINT=YES
        shift # past argument
        ;;
    *)
        echo "Unknown build argument: $1. See docs/platform-support.md." >&2
        exit 1
        ;;
esac
done

# Fail before publishing, modifying source files, or deleting previous outputs.
# A compilable RID is not necessarily a complete Bookshelf delivery package.
if [[ "$ENABLE_EXTRA_PLATFORMS_IN_SDK" = "YES" || "$ENABLE_EXTRA_PLATFORMS" = "YES" || "$INSTALLER" = "YES" ]]; then
    echo "Extra-platform builds and desktop installers are unsupported. See docs/platform-support.md." >&2
    exit 1
fi
if [[ -n "$RID" ]]; then
    case "$RID" in
        linux-x64|linux-musl-x64|linux-musl-arm64) ;;
        *) echo "Unsupported Bookshelf package runtime: $RID. See docs/platform-support.md." >&2; exit 1 ;;
    esac
fi
if [[ -n "$FRAMEWORK" && "$FRAMEWORK" != "net10.0" ]]; then
    echo "Unsupported Bookshelf package framework: $FRAMEWORK. Use net10.0." >&2
    exit 1
fi
if [[ ( -n "$RID" && -z "$FRAMEWORK" ) || ( -z "$RID" && -n "$FRAMEWORK" ) ]]; then
    echo "Specify both --runtime and --framework for a targeted package." >&2
    exit 1
fi

if [[ "$host_os" != "Linux" && ( "$BACKEND" = "YES" || "$PACKAGES" = "YES" ) ]]; then
    echo "Bookshelf delivery builds require a Linux host. See docs/platform-support.md." >&2
    exit 1
fi

if [ "$BACKEND" = "YES" ];
then
    UpdateVersionNumber
    Build
    if [[ -z "$RID" || -z "$FRAMEWORK" ]];
    then
        PackageTests "net10.0" "linux-musl-x64"
    else
        PackageTests "$FRAMEWORK" "$RID"
    fi
fi

if [[ "$LINT" = "YES" || "$FRONTEND" = "YES" ]];
then
    YarnInstall
fi

if [ "$LINT" = "YES" ];
then
    LintUI
fi

if [ "$FRONTEND" = "YES" ];
then
    RunVite
fi

if [ "$PACKAGES" = "YES" ];
then
    UpdateVersionNumber

    if [[ -z "$RID" || -z "$FRAMEWORK" ]];
    then
        Package "net10.0" "linux-musl-x64"
    else
        Package "$FRAMEWORK" "$RID"
    fi
fi
