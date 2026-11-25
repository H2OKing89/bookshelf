# Bookshelf update and local release policy

P25 isolates this fork from incompatible Readarr/Sonarr updates and adds a local
release-candidate receipt. It remains **In progress** for a fork-owned update
feed, authenticated releases, publication and the remaining platform/CI gates.
These commands do not publish a release or enable the updater.

## Product identity and compatibility

The release product ID is **`bookshelf-audiobooks`**. This is distinct from the
existing `Readarr` application name, executable names, database filenames,
configuration paths, API v1 routes and environment-variable prefixes. Those
existing contracts stay compatible; product isolation does not rename saved
identities, media, `/config`, credentials or API keys.

[BuildInfo](../src/NzbDrone.Common/EnvironmentInfo/BuildInfo.cs) exposes the product
ID and compiled source commit, tree, branch and dirty-state metadata through the
system-status API. The canonical [backend build](../build.sh) embeds the source
identity in assembly metadata. The frontend's
[SourceProvenancePlugin](../frontend/build/SourceProvenancePlugin.js) writes the
matching `UI/source-provenance.json`. Missing or changed source identity cannot
qualify as a clean release candidate.

## Update behavior

No compatible fork-owned update feed or built-in update channel is active.
[BookshelfUpdatePolicy](../src/NzbDrone.Common/EnvironmentInfo/BookshelfUpdatePolicy.cs)
denies in-app updating, with no XML, environment or API switch that enables it.
Existing branch, automatic-update and update-mechanism settings are preserved.
The startup normalizer does not rewrite those settings while updates are blocked.

- Latest-update checks return no candidate. Recent-update and update API requests
  return empty lists without contacting the legacy feed or fetching changelogs.
- Scheduled checks finish without queuing an installation. Manual application
  update commands fail with an explanation before provider calls, downloads,
  scripts, backup/replacement or process launch.
- Startup does not consume an existing `update_required` marker to trigger a
  legacy update.
- The standalone updater exits unsuccessfully before parsing supplied arguments,
  initializing file logging or looking up processes. Its application and install
  engine entry points also enforce the policy before accessing an installation.
- Settings and the Updates page explain the manual package route. Containers
  remain managed by replacing their container image.

The existing installer mechanics remain behind the policy for focused tests;
test mocks that explicitly permit them are not a runtime activation path. A
future updater needs a separately reviewed feed, release authentication and
compatibility contract before this policy can change.

## Prepare an unpublished candidate

The first local candidate format supports **native `linux-x64`, self-contained
.NET 10, channel `develop` only**. It is unsigned, has `publishable=false` and
`builtInUpdatesEnabled=false`, and does not approve a stable channel or legacy
softcover/hardcover variant. Existing musl/ARM package validation is separate;
those RIDs are not eligible for this candidate format yet.

Run from the repository root with a clean checkout, including no untracked source
files. Build the backend and frontend from that same unchanged commit using the
normal [package preparation](local-validation.md#prepare-a-package). Do not change
source or create a different commit between compilation, testing and staging.
Every output path below must be new and must not contain an application home.

The candidate requires passing, complete synthetic-data evidence for all eleven
checks: `package`, `app`, `restore`, `upgrade`, `media`, `cancellation`,
`authpolicy`, `accesspolicy`, `releaseidentity`, `acquisition` and `identity`. For example:

```sh
python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/genuine-older-package \
  --checks package app restore upgrade media cancellation accesspolicy releaseidentity acquisition \
  --output _temp/release-evidence

python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/genuine-pre-P16-P14-package \
  --checks authpolicy \
  --output _temp/release-auth-evidence

python3 scripts/validate-local.py \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --baseline-runtime /path/to/genuine-older-audiobook-package \
  --playwright-module /path/to/installed/playwright \
  --checks identity \
  --output _temp/release-audiobook-evidence

python3 scripts/prepare-release.py prepare \
  --runtime _artifacts/linux-x64/net10.0/Readarr \
  --evidence _temp/release-evidence/summary.json \
  --evidence _temp/release-auth-evidence/summary.json \
  --evidence _temp/release-audiobook-evidence/summary.json \
  --channel develop \
  --output _temp/release-candidate
```

Use a genuine older package appropriate for the upgrade check; a .NET 6 baseline
also exercises the runtime upgrade. The authentication-policy proof separately
needs a genuine pre-P16 P14 package with the existing salted-credential schema.
The identity proof needs an older package with the audiobook schema and an
explicit installed Playwright module; its versioned recipe creates its own home.
All three reports above share the same candidate bytes. Evidence may be split
across reports by repeating `--evidence`, but every report must refer to that exact
same candidate package fingerprint. Skipped, incomplete, failed or inconsistent
checks are rejected. The `releaseidentity` check reads the identity from an owned
running application and verifies that its frontend receipt agrees.

[scripts/prepare-release.py](../scripts/prepare-release.py) calls
[release_candidate.py](../scripts/release_candidate.py). Preparation verifies
that the compiled commit/tree/branch match the clean checkout and then creates:

- An unchanged copy of the tested package, with its per-file SHA-256 hashes and
  permission modes recorded in `release-manifest.json`.
- `source.tar.gz` from that exact Git commit, including `LICENSE.md`, its commit
  marker and archive hash for the corresponding GPL-3.0 source.
- Hashed copies of the passing evidence reports.
- Runtime/RID/framework/version identity and a fingerprint of the source
  migration files defining the database schema history.

Staging does not rewrite the input package. The output is private, and a failed
staging operation removes its owned incomplete candidate directory.

## Verify the candidate

Verification requires the same clean source checkout and exact commit:

```sh
python3 scripts/prepare-release.py verify _temp/release-candidate \
  --expected-product bookshelf-audiobooks \
  --expected-channel develop \
  --expected-commit "$(git rev-parse HEAD)" \
  --expected-rid linux-x64
```

Add `--expected-version` with the intended four-part application version when
checking an externally supplied expectation. Verification also compares the
manifest version with the compiled version attested in the passing evidence.
Wrong product, channel, source commit/tree/branch, RID, version, frontend identity,
schema, source archive, evidence, payload hashes or permission modes are rejected.
Unexpected candidate files are rejected as well.

This unsigned receipt establishes local integrity and source correspondence. It
does not authenticate a publisher, authorize installation or replace a signed
update manifest. No command here contacts an update feed, publishes an image,
starts a GitHub workflow or changes a running application home.

## Legacy metadata and publication audit

`METADATA_URL` remains a real configuration input. The
[ConfigService.MetadataSource](../src/NzbDrone.Core/Configuration/ConfigService.cs)
getter uses the saved setting first, then that environment variable, then
`https://api.bookinfo.pro`. The
[MetadataRequestBuilder](../src/NzbDrone.Core/MetadataSource/MetadataRequestBuilder.cs)
uses it for the existing BookInfo/Goodreads-search providers, which remain
referenced by legacy book/ebook and import-list paths. Removing the variable or
provider based only on its legacy name would change those features.

Audio's [Audnexus provider](../src/NzbDrone.Core/MetadataSource/Audnexus/AudnexusProxy.cs)
uses the separate `AudnexusUrl` configuration. Presence of a legacy metadata
environment variable alone does not show that every audiobook lookup uses it.
P25 does not remove or redirect either metadata configuration.

The existing GitHub workflows still contain legacy softcover/hardcover publishing
variants. This local change does not disable those jobs or establish publication
gates for them. Their retention/removal or migration, a fork-owned feed and trust
policy, signed releases, GitHub release gating and P22 per-RID native acceptance remain
separate work. GitHub billing is not needed for the local checks and staging
commands above.

## Current local candidate (2026-11-24)

The unpublished `develop` candidate **10.0.0.42949** for `linux-x64` was
built and staged from exact clean commit
`7b941a85335e1b5830a483168144ebb44cd7beaf` and verified with the normal
local candidate CLI. It includes 803 package files, corresponding GPL source
and pinned dependency licenses/notices, and 12 hashed passing validation
reports (2,325 exact glibc checks covering all eleven required gates).
The [full local validation](local-validation.md#background-profiling-typed-library-resources-dependency-compatibility-and-local-release-2026-11-24)
records the exact 6,110 passing source tests, current browser and database
checks, and original/corrected regression evidence.

The private candidate directory is
`_temp/p23-u07-p09-d30-p25-20261124/release-candidate/`, with its matching
clean source checkout at `build-source-release-final/` in the same evidence
root. Re-verification must use that checkout, not a later documentation-only
HEAD. Its package fingerprint is
`3c6d8592d33c2c59707745bab5bb76e4bda33b30ac3d580644fbc8f02784a28c`.
The `release-verification.json` receipt and the candidate's own
`release-manifest.json` record the archive/manifest hashes and exact source
identity. No signature, publishing, deployment or updater activation was
performed. P25 remains **In progress** for the separately gated signed release
and publication work.
