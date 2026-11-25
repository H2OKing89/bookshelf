#!/bin/bash
# Generate both API references from an explicitly supplied, complete Linux build.
# Uses an isolated loopback home; never deletes existing builds or uses a live home.
set -euo pipefail

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
  echo "Usage: bash docs.sh /path/to/built/Readarr [new-evidence-directory]"
  exit 1
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
runtime="$(realpath "$1")"
mkdir -p "$root/_temp"
if [ "$#" -eq 2 ]; then
  evidence="$(realpath -m "$2")"
else
  work="$(mktemp -d "$root/_temp/openapi-generation.XXXXXX")"
  evidence="$work/evidence"
fi

cd "$root"
python3 -m scripts.validation.api_documents "$runtime" "$evidence" --generate
mkdir -p src/Readarr.Api.V2
install -m 0644 "$evidence/v1-openapi.json" src/Readarr.Api.V1/openapi.json
install -m 0644 "$evidence/v2-openapi.json" src/Readarr.Api.V2/openapi.json

echo "Generated both Bookshelf API references. Validation evidence: $evidence"
