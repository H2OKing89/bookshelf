#!/usr/bin/env bash
set -euo pipefail

test_runner="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)/test.sh"
case_root=$(mktemp -d "${TMPDIR:-/tmp}/bookshelf-test-runner.XXXXXX")
trap 'rm -rf -- "$case_root"' EXIT
mkdir -p "$case_root/bin"

cat > "$case_root/bin/dotnet" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$@" > "$RUNNER_STUB_ARGUMENTS"
exit "$RUNNER_STUB_EXIT"
STUB
chmod +x "$case_root/bin/dotnet"

check_status()
{
    local mode="$1"
    local expected="$2"
    local directory="$case_root/$mode-$expected"
    local actual
    mkdir -p "$directory/assemblies"

    if (
        cd "$directory"
        PATH="$case_root/bin:$PATH" \
        ProgramData="$directory/program-data" \
        TEST_DIR="$directory/assemblies" \
        RUNNER_STUB_ARGUMENTS="$directory/arguments" \
        RUNNER_STUB_EXIT="$expected" \
            bash "$test_runner" Windows Unit "$mode" > output.log 2>&1
    ); then
        actual=0
    else
        actual=$?
    fi

    if [ "$actual" -ne "$expected" ]; then
        cat "$directory/output.log" >&2
        echo "$mode: expected runner status $expected, received $actual" >&2
        exit 1
    fi

    if [ ! -s "$directory/arguments" ] || ! grep -Fxq 'test' "$directory/arguments"; then
        echo "$mode: the stub test runner was not called" >&2
        exit 1
    fi

    if [ ! -d "$directory/program-data/Readarr" ]; then
        echo "$mode: the Windows test setup did not use its private ProgramData" >&2
        exit 1
    fi
}

check_status Test 0
check_status Test 7
check_status Test 127
check_status Test 137
check_status Coverage 0
check_status Coverage 13
echo 'Passed 6 test runner status cases'
