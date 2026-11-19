# Sonarr v5 porting backlog for Bookshelf

This is the detailed work list for the [modernization roadmap](sonarr-v5-modernization-roadmap.md).
The only upstream comparison target is **Sonarr v5.0.0.2941**, commit
`c018578e4b79ee048575c02d7a8aa2b929e4b837`; Bookshelf was reviewed at
`38f48b1716fb51adb648c087c33ebe1fa620438e` on **2026-11-13, America/Chicago**.

These are source-reviewed candidates and preservation gates, not completed ports.
An audit item is a risk to reproduce and decide, rather than a claim that a failing
runtime test has already demonstrated it. No application build, dependency audit,
upgrade experiment or runtime change was performed for this comparison.

That statement describes the **2026-11-13 comparison snapshot**. Dated
implementation records below track later work without rewriting its source
evidence or the comparison baseline.

## How to use the list

Execution status is tracked in the index only. Start with `Planned`, then use
`In progress`, `Blocked` with a concrete reason, `Verified` with commit/test evidence,
or `Deferred` with a recorded decision. Snapshot descriptions remain the comparison
evidence; append dated implementation notes when a task is finished. `Keep` means
an existing Bookshelf capability must survive a port, not that it needs replacing.

- **P0:** data/authentication/release boundary or a required preservation gate.
- **P1:** core reliability, supported platform, or frequently used behavior.
- **P2:** useful capability, usability or performance work after the foundation.
- **P3:** optional extension requiring a demonstrated product need.
- **Effort:** S = bounded change; M = several components; L = cross-layer work;
  XL = substantial migration. These are relative sizes, not delivery estimates.
- **Approach:** port a reusable behavior; adapt book-specific rules; study a design;
  or keep/defer an implementation. None means automatic cherry-pick approval.

`S5`/`S` in evidence paragraphs means the pinned Sonarr source; `B` means Bookshelf.
Frontend shorthand paths are relative to `frontend/src/`. The primary source and
counterpart links on every entry are full paths. Some counterpart links identify
an integration point because no equivalent class exists locally. Dependency IDs
are prerequisites; textual requirements explain additional design/test gates.
The two keep/test gates D01 and B01 apply to every subsequent feature port.

## Index

| ID | Priority | Execution | Effort | Candidate |
| --- | --- | --- | --- | --- |
| [P01](#p01) | P1 | In progress | XL | Move the runtime and SDK together to .NET 10 |
| [P02](#p02) | P0 | In progress | M | Replace unsalted password hashes with versioned salted derivation |
| [P03](#p03) | P0 | Verified | M | Restrict forwarded-header trust and local-address auth bypass |
| [P04](#p04) | P1 | Verified | M | Add optional allowed-host filtering |
| [P05](#p05) | P1 | Verified | S | Read the current API key and compare it in fixed time |
| [P06](#p06) | P1 | Verified | M | Bound log-cleansing regex work and fail closed on timeout |
| [P07](#p07) | P1 | Verified | M | Validate every restore payload before replacing config/database files |
| [P08](#p08) | P1 | Verified | M | Add bounded login-attempt lockout |
| [P09](#p09) | P1 | In progress | L | Refresh managed/native packages as a staged compatibility change |
| [P10](#p10) | P1 | Verified | L | Carry cancellation through the HTTP client stack |
| [P11](#p11) | P1 | Verified | M | Use per-connection Happy Eyeballs and expire pooled connections |
| [P12](#p12) | P1 | Verified | XL | Modernize the migration engine while preserving Bookshelf schema history |
| [P13](#p13) | P2 | In progress | M | Upgrade SQLite provider and measure busy-timeout behavior |
| [P14](#p14) | P2 | Verified | M | Support full PostgreSQL connection strings with corrected validation |
| [P15](#p15) | P2 | Verified | M | Make SQL log storage optional |
| [P16](#p16) | P2 | Verified | L | Review v5 authentication defaults, bootstrap and recovery as product policy |
| [P17](#p17) | P2 | Deferred | L | Add OIDC only if SSO is wanted |
| [P18](#p18) | P2 | Planned | M | Load PEM certificates, separate private keys and certificate chains |
| [P19](#p19) | P2 | Planned | L | Study in-process host restart and service lifecycle |
| [P20](#p20) | P2 | Planned | M | Keep API versions coexistable and generate matching OpenAPI documents |
| [P21](#p21) | P1 | In progress | L | Run the delivery artifact through unit, DB and HTTP integration gates |
| [P22](#p22) | P2 | Planned | L | Define a truthful support matrix and package each native dependency |
| [P23](#p23) | P3 | Planned | M | Add opt-in request/SQL profiling only after core correctness |
| [P24](#p24) | P2 | Verified | S | Correct URL-base boundary handling on login redirects |
| [P25](#p25) | P0 | In progress | L | Isolate fork release channels and require matching source/artifact identity |
| [P26](#p26) | P0 | Verified | M | Propagate repository write failures and transact general bulk updates |
| [D01](#d01) | P0 | Planned | S | Preserve Bookshelf's staged replacement and ownership boundary |
| [D02](#d02) | P1 | Planned | L | Add machine-readable download and import rejection codes |
| [D03](#d03) | P0 | Verified | S | Fix tag isolation when choosing download clients |
| [D04](#d04) | P0 | Verified | M | Audit completion against distinct books and complete recording parts |
| [D05](#d05) | P1 | Planned | M | Prevent custom-format downgrades during import and rename |
| [D06](#d06) | P1 | Planned | M | Classify rejected extensions, including downloads with no importable files |
| [D07](#d07) | P1 | Planned | L | Ask download clients for file inventory when the filesystem has no eligible files |
| [D08](#d08) | P2 | Planned | M | Make automatic failure/blocklist/redownload actions explicit per indexer |
| [D09](#d09) | P1 | Planned | M | Fail over among eligible download clients |
| [D10](#d10) | P1 | Planned | M | Remove disappeared tracked items and refresh client configuration promptly |
| [D11](#d11) | P1 | Planned | M | Separate blocked import state from terminal failure |
| [D12](#d12) | P1 | Planned | M | Notify once when manual intervention is required |
| [D13](#d13) | P1 | Planned | M | Add minimum custom-format score improvement for upgrades |
| [D14](#d14) | P1 | Planned | M | Add language custom formats using audiobook evidence |
| [D15](#d15) | P1 | Planned | L | Implement meaningful audiobook size policy, including per-profile limits |
| [D16](#d16) | P2 | Planned | M | Rank near preferred audiobook size instead of always favoring the largest |
| [D17](#d17) | P1 | Planned | M | Check library free space before grabbing |
| [D18](#d18) | P2 | Planned | L | Represent one queued download with multiple books |
| [D19](#d19) | P2 | Planned | M | Refresh pending decisions when policy or metadata changes |
| [D20](#d20) | P2 | Planned | M | Sort library/history quality according to each profile |
| [D21](#d21) | P1 | Planned | S | Backport narrow RSS parser fixes, retain existing feed machinery |
| [D22](#d22) | P1 | Planned | L | Persist successful import-list membership snapshots |
| [D23](#d23) | P2 | Planned | M | Add additive and synchronized tags for existing list entries |
| [D24](#d24) | P2 | Planned | M | Offer non-destructive cleanup when books leave all lists |
| [D25](#d25) | P2 | Planned | M | Add a documented generic JSON import-list endpoint |
| [D26](#d26) | P2 | Planned | M | Enrich webhooks with committed completion and score provenance |
| [D27](#d27) | P2 | Planned | M | Send health-restored notifications |
| [D28](#d28) | P3 | Planned | M | Coalesce media-server refreshes for multipart imports |
| [D29](#d29) | P2 | Planned | M | Support explicit qBittorrent total versus inactive seed time |
| [D30](#d30) | P3 | Planned | L | Defer scripted import replacement; study an explicit extension boundary |
| [U01](#u01) | P1 | Planned | S | Correct accessible names on icon actions |
| [U02](#u02) | P1 | Planned | M | Named, semantic dialogs |
| [U03](#u03) | P1 | Planned | M | Keyboard-operated, viewport-aware menus |
| [U04](#u04) | P1 | Planned | M | Connect form labels, help and validation to inputs |
| [U05](#u05) | P2 | Planned | S | Announce asynchronous action results |
| [U06](#u06) | P1 | Planned | M | Upgrade React independently to 18/createRoot |
| [U07](#u07) | P1 | Planned | L | Strict TypeScript and explicit frontend type checking |
| [U08](#u08) | P1 | Planned | M | Typed fetch transport with cancellation and structured errors |
| [U09](#u09) | P1 | Planned | M | Introduce Query for one server-owned state slice |
| [U10](#u10) | P1 | Planned | M | Keyed pagination that retains previous rows |
| [U11](#u11) | P1 | Planned | L | Deliberate SignalR-to-cache synchronization |
| [U12](#u12) | P1 | Planned | M | Preserve preferences while splitting state into small stores |
| [U13](#u13) | P2 | Planned | M | Local id-based selection with row subscriptions |
| [U14](#u14) | P1 | Planned | M | Typed isolated pending settings state |
| [U15](#u15) | P1 | Planned | M | Unified route/close/unload protection for dirty forms |
| [U16](#u16) | P2 | Planned | L | Data-router navigation with a single basename |
| [U17](#u17) | P1 | Planned | M | Keep Queue stable while a user acts, and coalesce refreshes |
| [U18](#u18) | P2 | Planned | M | Rich Queue/History filter builders |
| [U19](#u19) | P2 | Planned | L | Responsive release cards with variable-height virtualization |
| [U20](#u20) | P2 | Planned | M | Text filtering and intentional caching of interactive searches |
| [U21](#u21) | P2 | Planned | M | Simplify existing library virtualization after measuring it |
| [U22](#u22) | P2 | Planned | L | Vite/HMR and a separately verified asset pipeline |
| [B01](#b01) | P0 | Planned | M | Make audiobook identity and upgrade invariants the porting contract |
| [B02](#b02) | P1 | Planned | L | Provide a supported author reconciliation and correction workflow |
| [B03](#b03) | P2 | Planned | XL | Represent publisher-pack membership without collapsing recording identities |
| [B04](#b04) | P1 | Planned | L | Add audio-specific custom-format rules from trustworthy media evidence |
| [B05](#b05) | P2 | Planned | M | Version probe metadata and invalidate stale analysis deliberately |
| [B06](#b06) | P2 | Planned | L | Complete the local-recording metadata lifecycle |
| [B07](#b07) | P1 | Planned | M | Expose metadata-provider health and partial-result provenance |
| [B08](#b08) | P1 | Planned | M | Explain and resolve unmapped or conflicting recording evidence |
| [B09](#b09) | P2 | Planned | M | Add book-native library diagnostics and collection statistics |

### Release group follow-up index

The [2026-11-15 focused comparison](sonarr-v5-release-group-comparison.md)
uses Bookshelf `4883fcaa724dd15e57c555cd13d39cbcba379a2f` and the same pinned
Sonarr v5 donor. These five additional slices supplement the original 87-item
snapshot. D05 and D13 already cover import-score and upgrade-delta enforcement.
Execution status for the new slices is tracked here.

| ID | Priority | Execution | Effort | Candidate |
| --- | --- | --- | --- | --- |
| [RG01](#rg01) | P1 | Verified | M | Adapt release group parsing with audiobook compatibility fixtures |
| [RG02](#rg02) | P1 | Verified | M | Carry manual-import group choices through preview and commit |
| [RG03](#rg03) | P2 | Verified | M | Expose and deliberately correct stored file release groups |
| [RG04](#rg04) | P2 | Planned | M | Design recording-versus-pack group evidence precedence |
| [RG05](#rg05) | P2 | Verified | S | Add group-token length formatting and define missing-value behavior |

## Platform, authentication, storage and delivery

### P01

**Move the runtime and SDK together to .NET 10**

- Priority / effort: **P1 / XL**.
- Current Bookshelf state: missing .NET 10; existing projects use `net6.0`.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Common/Sonarr.Common.csproj](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Common/Sonarr.Common.csproj#L3).
- Bookshelf counterpart/integration point: [src/NzbDrone.Common/Readarr.Common.csproj](../src/NzbDrone.Common/Readarr.Common.csproj#L3).

**Bookshelf status:** missing .NET 10; existing projects use `net6.0`. **Evidence:** B `src/NzbDrone.Common/Readarr.Common.csproj:3`, `src/NzbDrone.Core/Readarr.Core.csproj:3`, `src/NzbDrone.Host/Readarr.Host.csproj:3`; B `mise.toml:2` pins SDK `6.0.428`, while `.github/workflows/build.yml:20` declares `6.0.427`. S5 `global.json:3` pins `10.0.403`, and `src/NzbDrone.Common/Sonarr.Common.csproj:3`/Core/Host target `net10.0`.

**Action/dependencies:** establish one SDK authority, update all executable/library/test targets and build/output paths, then update Docker packaging, `test.sh`, CI, and native dependencies. Keep B's central package management. This is the enabling change for P09–12, P17–19; small P0 hardening can precede it. Target the supported runtime declared by the pinned v5 source; changing only the SDK does not update a packaged runtime.

**Book/audio caution:** validate TagLibSharp-Lidarr, PdfSharpCore, EPUB/PDF handling, Calibre integration, SQLite native loading and bundled MediaInfo under the new runtime; a successful CLI start alone is insufficient.

**Acceptance:** clean restore/build/test with the pinned SDK; self-contained linux-x64/linux-musl-x64/linux-musl-arm64 artifacts start in disposable homes; an upgraded copy preserves authors, books, editions, audiobook parts/narrators, root folders, history and credentials; clean Docker startup and shutdown. No test was run in this research.

**2026-11-15 SDK prerequisite record:** P01 is **In progress**. P09 exposed a
build prerequisite: the repository's SDK 6 setup does not implement the NuGet
auditing policy used for verification. `global.json` now selects SDK 10.0.403
with latest-patch roll-forward and prereleases disabled; mise uses the same
baseline. CI derives its SDK path from the selected `dotnet --version` rather
than the old hard-coded 6.0.427 location. This makes direct and transitive
package auditing apply on the normal contributor/CI path.

The application, library, test targets and self-contained payloads still use
net6.0. This SDK step does not complete the supported .NET 10 runtime migration,
Docker/native-platform acceptance, saved-home migration proof or remaining
platform-specific build tooling. The obsolete SDK-patching option now fails
explicitly. CI separately installs the old test runtime, and the test wrapper
now preserves failures with six isolated regression cases. P01 remains **In progress**; P09's verified
security-maintenance slice does not imply complete P01/P12/P13/P21/P22 gates.

**2026-11-15 runtime implementation record:** implementation commit `d572aaa2b`
and API-binding correction `2b0edf8ef` move all 25 application, library, test
and Windows GUI project targets to .NET 10. `global.json`, mise and active
build/test/publish paths use SDK **10.0.404** and `net10.0`; executable release
payloads explicitly include runtime **10.0.15**. Microsoft's
[release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
identifies the 2026-11-10 update as a security release. The pinned Sonarr v5 donor
is unchanged; its SDK 10.0.403 is superseded by this supported patch.

Framework-provided package references and the separate SourceLink package are
removed where the SDK/framework now supplies them. Compatibility changes use
TimeProvider, X509CertificateLoader for the existing password-protected PFX
contract, and System.Net.IPNetwork for explicit proxy trust. Array Contains
expression lowering, bounded Transmission ETAs, complete stream reads and NLog
configuration replacement retain their regression coverage. No Bookshelf
database migration is added by the runtime change.

The packaged HTTP check exposed ASP.NET Core's post-.NET-6 inference of
DI-registered action parameters. Bookshelf's DryIoc registrations also include
API resource types; `DisableImplicitFromServicesParameters` preserves JSON-body
binding, including manual-backup commands. Five hosted HTTP regression cases
cover this boundary. This follows Microsoft's
[API-controller binding compatibility guidance](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/7/api-controller-action-parameters-di?view=aspnetcore-10.0).

Validation for the final `2b0edf8ef` source and matching candidates:

- All 25 locks passed clean locked restore and a strict transitive NuGet audit,
  with no suppressions or audit-warning exception. Both solution configurations
  built without warnings; Windows GUI verification is a cross-compile only.
- The canonical locked `./build.sh --backend` and all three Linux packages
  passed, including self-contained executables, updaters, matching dependency
  notices, bundled MediaInfo 26.10 and the expected native architectures.
- Offline suites passed **4,551 cases**, with **103 not-executed TRX records**:
  Core 3,460, Common 691, API/authentication 273, Host 49, Mono 52, Libraries 1,
  Update 17 and API-unit 8. The not-executed records comprise Core 55, Common 38,
  Host 4 and Mono 6; runner console summaries report 99 skips. Eight coverage
  cases produced real XML output; six isolated shell cases retained test-runner
  exit statuses.
- Packaged current/genuine-legacy backup restoration passed **51/51** checks.
  A genuine P09 .NET 6 baseline upgraded in the **same disposable home** to
  .NET 10 and passed **24/24** checks, preserving user/credential encodings,
  config/API key, root folders, author/book/recording identities, two recording
  parts and narrators, history and media hashes. Packaged lockout passed **50/50**.
- Native probes passed **35** glibc-x64 and **55** musl-x64 checks. The actual
  repository Dockerfile, using Alpine **3.24.4** for helper builds and
  LinuxServer Alpine **3.24** at runtime, passed **54/54** S6 startup, HTTP,
  native-format, MediaInfo, notice and graceful-shutdown checks on x64.
- ARM64 native checks passed **33** under explicit QEMU. Default-environment
  HTTP startup failed twice on unchanged bits: target signal 11, then a managed
  AccessViolation during Dapper DynamicClass parameter setup for
  InsertMissingQualityDefinitions, followed by QEMU target signal 6. Both failed
  results are retained. A private compatibility experiment setting
  `DOTNET_TieredCompilation=0` and `DOTNET_EnableWriteXorExecute=0` passed
  **53/53** checks, including the 33 native checks, HTTP on runtime 10.0.15,
  API-key preservation and graceful shutdown. Candidate hashes stayed unchanged
  and owned containers were removed. These flags are not shipped.

Microsoft's [.NET 10 support notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#notes)
exclude QEMU for running .NET applications. These are bounded emulation results;
the failures' cause is not established, and the compatibility experiment does
not establish default behavior on native ARM64 hardware.

P01's implementation and x64/musl-x64 acceptance are complete. It remains
**In progress** solely for default ARM64 startup acceptance on a **native ARM64
runner**. Run the default startup checks on that runner before closing P01.
Windows/macOS execution, the missing Intel macOS native-image asset and the
complete platform matrix remain P22 work. Legacy Azure jobs are manual-only and
default off; actual remote GitHub/Azure CI was not run. No live development or
production home, service, credentials or media was changed. The
[current dependency record](dependency-audit.md#net-10-runtime-alignment-2026-11-15)
contains the new inventory and remaining P09/P12/P13/P21/P22/P25 scope.

### P02

**Replace unsalted password hashes with versioned salted derivation**

- Priority / effort: **P0 / M**.
- Current Bookshelf state: partial authentication; password storage is the gap.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Authentication/UserService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Authentication/UserService.cs#L79).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Authentication/UserService.cs](../src/NzbDrone.Core/Authentication/UserService.cs#L70).

**Bookshelf status:** partial authentication; password storage is the gap. B `src/NzbDrone.Core/Authentication/UserService.cs:31` (`Add`) stores `password.SHA256Hash()`; `FindUser` at line 70 compares that hash directly. B `User.cs:10` has no salt/iteration fields. S5 `UserService.cs:21` sets 220,000 iterations; `FindUser:79` upgrades a successful legacy SHA-256 login and rehashes lower iteration counts; `SetUserHashedPassword:124` and `GetHashedPassword:143` use random 128-bit salts and PBKDF2/HMAC-SHA512; `IsMatchingHash:161` uses fixed-time comparison. S5 `Migration/174_add_salt_to_users.cs:9` adds nullable Salt/Iterations.

**Action/dependencies:** add a new **Bookshelf** migration after its current sequence, not Sonarr migration 174; port the user fields and upgrade path as one change. Audit API resource mapping so salts, hashes and iteration metadata never become returned credentials. Benchmark the chosen work factor on supported ARM hardware.

**Book/audio caution:** retain the saved user's Identifier and existing API key; do not reset credentials or touch provider secrets when upgrading a library home.

**Acceptance:** the original legacy password logs in and is upgraded once; wrong passwords never migrate a record; new equal passwords have different salts; an older salted record is upgraded after successful login; unchanged-password settings saves preserve the password; Forms/Basic compatibility is explicitly decided and tested. Old binary rollback uses a pre-upgrade backup because it cannot validate the new hashes.

**2026-11-14 implementation record:** [UserService](../src/NzbDrone.Core/Authentication/UserService.cs)
now derives new passwords with PBKDF2-HMAC-SHA512, 220,000 iterations, random
16-byte salts and 32-byte keys using the existing .NET 6 cryptography APIs.
Legacy and lower-work-factor credentials upgrade only after successful
verification; fixed-time comparisons, exact decoded sizes and a maximum stored
work factor bound malformed metadata. Existing user Id/Identifier, API keys,
Forms/Basic support and authentication defaults are preserved. JWT, roles, OIDC
and unrelated authentication policy changes are outside this batch.

[Migration 048](../src/NzbDrone.Core/Datastore/Migration/048_add_salt_to_users.cs)
adds nullable Salt/Iterations columns to the existing Users table. It does not
replace identities or rewrite library data. Rollback must restore the matching
pre-upgrade database backup and runtime. [UserRepository](../src/NzbDrone.Core/Authentication/UserRepository.cs)
guards rehash/reset writes against stale identity and credential snapshots.
Login conflicts permit one reload/reverification; unchanged-password settings
saves update only Username, preserving a concurrent login upgrade. Conflicting
explicit resets fail with a model conflict instead of overwriting newer values.

[HostConfigController](../src/Readarr.Api.V1/Config/HostConfigController.cs)
returns a mask rather than a digest, preserves null/omitted or unchanged masked
passwords, rejects the reserved mask for account creation, and validates new
password confirmation. Current legacy-digest round trips remain unchanged;
stale digests after upgrade cannot become unintended plaintext resets.
Password/PasswordConfirmation are excluded from configuration dictionaries.
[User SQL diagnostics](../src/NzbDrone.Core/Datastore/Extensions/BuilderExtensions.cs)
redact password, salt and iteration parameters, including insert exception data.

Focused regression fixtures cover [derivation and upgrade behavior](../src/NzbDrone.Core.Test/Authentication/UserServiceFixture.cs),
[additive migration](../src/NzbDrone.Core.Test/Datastore/Migration/048_add_salt_to_usersFixture.cs),
[stored metadata and diagnostic redaction](../src/NzbDrone.Core.Test/Authentication/UserRepositoryFixture.cs),
[credential concurrency](../src/NzbDrone.Core.Test/Authentication/UserCredentialConcurrencyFixture.cs),
[settings compatibility](../src/NzbDrone.Integration.Test/ApiTests/HostConfigPasswordFixture.cs)
and [legacy/current Forms/Basic authentication](../src/NzbDrone.Integration.Test/Authentication/PasswordAuthenticationFixture.cs).
Validation on the uncommitted `fix/sonarr-v5-foundation` working tree, based on
`38f48b1716fb51adb648c087c33ebe1fa620438e`: the offline core suite passed
3,301 cases (55 existing skips); focused API/authentication fixtures passed
81 cases; PostgreSQL repository, migration, concurrency and import fixtures
passed 125 cases (one existing skip). These are overlapping regression suites,
not a unique-case total. The packaged linux-x64 candidate passed seven HTTP
smoke scenarios against a disposable home, including legacy schema upgrade,
one-time rehash, rejected logins, settings preservation, confirmed reset and
Basic authentication after restart. Restoring the saved pre-upgrade database
and configuration with the previous runtime preserved the original login.
One legacy Forms login, including derivation and HTTP handling, took 138 ms on
the development x64 host; this is a single smoke measurement, not an ARM or
load benchmark.

P02 remains **In progress** until the supported ARM work-factor benchmark is
recorded. Other artifact/RID gates belong to P01/P21/P22. No live development
or production home was changed, and the P09 dependency blocker prevents
deployment. The existing host-config save order remains: other settings are
persisted before credentials, so a credential conflict can return 409 after
those settings have been saved. This batch does not claim an atomic transaction
across the configuration file and user database.

### P03

**Restrict forwarded-header trust and local-address auth bypass**

- Priority / effort: **P0 / M**.
- Current Bookshelf state: partial reverse-proxy support with a trust gap.
- Approach: adapt as one unit.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Host/ForwardedHeadersConfigurator.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Host/ForwardedHeadersConfigurator.cs#L17).
- Bookshelf counterpart/integration point: [src/NzbDrone.Host/Startup.cs](../src/NzbDrone.Host/Startup.cs#L59).

**Bookshelf status:** partial reverse-proxy support with a trust gap. B `src/NzbDrone.Host/Startup.cs:59` clears both KnownNetworks and KnownProxies; B `src/Readarr.Http/Authentication/UiAuthorizationHandler.cs:25` authorizes local-address bypass based on the resulting remote IP without checking remaining forwarding headers. S5 `src/NzbDrone.Host/ForwardedHeadersConfigurator.cs:17` retains framework defaults, accepts configured CIDRs, and sets forwarding behavior; `src/Sonarr.Http/Authentication/UiAuthorizationHandler.cs:26` additionally requires `IsClientAddressKnown:54` (no unconsumed X-Forwarded-For) for LAN bypass. `ConfigFileProvider.cs:312` exposes TrustedNetworks.

**Action/dependencies:** port configurator, parser, option/config validation, guard, documentation and proxy tests together. Keep existing CGNAT opt-in false. P08 depends on trustworthy client-IP resolution. Record how to configure the existing reverse proxy so a secure default does not silently strand users.

**Book/audio caution:** external metadata/download clients must continue using their configured outbound proxy; this is the inbound trust boundary and should not change those settings.

**Acceptance:** direct public requests with a forged loopback/LAN XFF remain unauthenticated; untrusted proxies cannot override scheme/host/IP; a trusted multi-hop chain resolves as intended; IPv4, IPv6, mapped IPv4, invalid CIDRs and CGNAT opt-in are covered; an actual configured proxy still supports login, API and SignalR.

**2026-11-14 implementation record:** the uncommitted
`fix/sonarr-v5-foundation` working tree adapts the pinned v5 configurator and
remaining-header guard to the current .NET 6 runtime. [Startup](../src/NzbDrone.Host/Startup.cs)
resolves saved proxy options through DI; [ForwardedHeadersConfigurator](../src/NzbDrone.Host/ForwardedHeadersConfigurator.cs)
retains framework loopback trust and accepts additional configured proxy IPs or
networks. It consumes complete trusted chains and stops at an unknown peer.
An invalid startup entry warns and is ignored, without falling back to trusting
all peers. Untrusted peers cannot replace the request address, host or scheme.

[IPNetworkParser](../src/NzbDrone.Common/Network/IPNetworkParser.cs) accepts
unambiguous IPv4/IPv6 addresses and aligned CIDRs. It rejects abbreviated/octal/hex
IPv4, scoped/bracketed IPv6, hostnames, wildcards, host bits and trust-all `/0`
entries. Mapped IPv4 `/96` and native IPv6 networks covering every mapped IPv4
peer are also rejected. These are local adaptations beyond the donor parser.

[UI authorization](../src/Readarr.Http/Authentication/UiAuthorizationHandler.cs)
requires no remaining `X-Forwarded-For` header before local-address bypass.
The .NET 6 wrapper also preserves empty/dequoted-empty client-address evidence
and skips forwarding when the transport peer is unknown. This prevents host or
scheme processing from erasing an unresolved header before the guard runs.
Remote-IP lookup handles absent addresses safely and normalizes mapped IPv4.
Authentication-required settings are read per request: separate DI handler
registrations cannot retain a stale bypass choice after settings change.
CGNAT bypass remains opt-in and defaults false. Existing authenticated
Forms/Basic/API principals continue to satisfy authorization normally.

`TrustedNetworks` is available through XML and `Readarr__Server__TrustedNetworks`,
the host API and an advanced Host settings field. Its default is empty, meaning
loopback proxies only. API updates validate the entire supplied list before any
write; omitted/null preserves the saved value and explicit empty clears it.
The mapper now preserves the actual CGNAT choice on settings round trips.
The UI prompts for a restart after a trust change. No new database migration,
credential reset, outbound proxy change or recording/library mutation is added.
The [deployment guide](trusted-proxies.md) explains exact proxy peers, TLS/header
configuration, multi-hop trust and upgrading an existing reverse proxy safely.

Regression fixtures cover [parser syntax](../src/NzbDrone.Common.Test/NetworkTests/IPNetworkParserFixture.cs),
[XML/environment preservation](../src/NzbDrone.Common.Test/ConfigFileProviderTest.cs),
[real forwarding middleware and DI](../src/NzbDrone.Host.Test/ForwardedHeadersFixture.cs),
[authorization](../src/NzbDrone.Integration.Test/Authentication/UiAuthorizationHandlerFixture.cs),
[null/mapped remote IPs](../src/NzbDrone.Integration.Test/Authentication/RequestRemoteIpFixture.cs),
[host API compatibility](../src/NzbDrone.Integration.Test/ApiTests/HostConfigTrustedNetworksFixture.cs)
and [frontend save/restart behavior](../frontend/test/trustedProxiesSettings.test.mjs).
Validation passed **93** parser/configuration cases, **36** host/middleware/DI
cases, **161** API/authentication cases and **32** frontend cases, with no
failures or skips in these focused suites. The API suite includes P02 and
recording-identity regressions. Frontend lint and the production webpack/type
build passed. The self-contained linux-x64 package passed **39** checks in a
fresh synthetic home and local TLS proxy, including a real untrusted transport
peer, spoofed/empty/quoted-empty headers, saved/omitted/cleared trust, Secure
Forms cookies, legacy rehash, invalid credentials, API key/no-key behavior,
SignalR negotiation and an actual authenticated WebSocket JSON handshake and
clean close under `/bookshelf`. Changing authentication-required settings took
effect without restarting. The same checks passed with the framework's
auto-forwarding environment flag present on Bookshelf's current bare host-builder
path. All owned test processes and listeners were stopped.

P03 is **Verified** locally on this uncommitted working tree, based on
`38f48b1716fb51adb648c087c33ebe1fa620438e`. No live development or production
server, settings, home or media was changed. This is not a full browser or
cross-platform audit. P09 still prevents deployment; no package or committed
audit-policy change is part of P03. This batch was followed by [P05](#p05).

### P04

**Add optional allowed-host filtering**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing host allowlist, not missing bind-address configuration.
- Approach: adapt.
- Prerequisites: [P03](#p03).
- Pinned v5 source: [src/NzbDrone.Host/ConfigureHostFilteringOptions.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Host/ConfigureHostFilteringOptions.cs#L28).
- Bookshelf counterpart/integration point: [src/NzbDrone.Host/Startup.cs](../src/NzbDrone.Host/Startup.cs#L245).

**Bookshelf status:** missing host allowlist, not missing bind-address configuration. S5 `src/NzbDrone.Host/ConfigureHostFilteringOptions.cs:28` parses AllowedHosts, removes bind addresses, includes loopback/server hostnames, and intentionally permits all hosts when the list is empty; `Startup.cs:333` invokes `UseHostFiltering()`. B `Startup.cs:245` invokes forwarded headers without host filtering; B ServerOptions/ConfigFileProvider have no AllowedHosts setting.

**Action/dependencies:** adapt parser, service registration, config/API/UI validation and middleware order; coordinate P03 so the forwarded Host is trusted only from a configured proxy. Do not describe upstream's empty default as DNS-rebinding protection enabled by default: it accepts `*`.

**Book/audio caution:** document extra hostnames used by library frontends and book-server integrations; do not confuse listener addresses (`0.0.0.0`, `::`) with client Host names.

**Acceptance:** empty config preserves today's access; configured valid names work; other hosts are rejected; localhost recovery works; IPv6 literals, ports, wildcard policy, base paths and proxy host forwarding are tested.

**2026-11-18 implementation record:** P04 is **Verified** locally in
`c03ea78e2`. `AllowedHosts` is available through XML, the
`Readarr__Server__AllowedHosts` environment setting, the host API and an advanced
Host settings field. The default remains empty and permissive. A configured list
accepts exact DNS/IP names or `*.domain` subdomains; wildcard matching excludes
the apex. Commas and semicolons separate entries. Names are normalized to IDN
ASCII/lowercase, IPv6 literals to brackets, and duplicates removed. Configured
entries cannot contain schemes, ports, paths, CIDRs, scoped IPv6, listener bind
wildcards or a lone `*`; request ports are ignored when matching valid names.

Localhost, loopback IPv4/IPv6 and a valid normalized server hostname remain
available for local recovery. Invalid startup configuration fails before serving
requests. API omission/null preserves the saved value, explicit empty clears it,
and invalid saves fail before configuration/account writes. Environment values,
including explicit empty, take precedence over XML. Changes require restart.
Host filtering runs after trusted forwarded headers and before UI/API/SignalR
access, so a trusted proxy's effective host must also be allowed. An untrusted
peer cannot override that host through forwarding headers.

The **89 focused source cases** pass, including grammar, configuration/API
preservation and hosted middleware/proxy boundaries. Initial packaged
`accesspolicy` validation passes **62 checks** including its unchanged-input
check: exact/wildcard/IPv6-with-port/local hosts, rejection before authentication,
trusted-loopback forwarding, settings/environment precedence, restart behavior,
and invalid startup with the synthetic library/accounts intact. A genuine
pre-P04 package fails `host_reject_unknown` as expected, with cleanup and input
fingerprints intact. Source fixtures cover forged remote peers; the packaged
loopback proof does not claim a physical reverse-proxy deployment audit. Final
clean-build evidence is recorded separately in [local validation](local-validation.md).

**Final upgrade follow-up:** `0015c349f` makes an absent `AllowedHosts` read
non-persistent. The older-home upgrade initially exposed an unwanted empty-key
write; the exact-byte regression and clean-package upgrade now pass. This raises
the focused source coverage to **90 cases**. The final artifact, authentication,
browser and candidate proofs are recorded in
[local validation](local-validation.md#p24-p04-and-local-p25-verification-2026-11-18).

### P05

**Read the current API key and compare it in fixed time**

- Priority / effort: **P1 / S**.
- Current Bookshelf state: partial; all three key transports already exist.
- Approach: port with compatibility tests.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/Sonarr.Http/Authentication/ApiKeyAuthenticationHandler.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Http/Authentication/ApiKeyAuthenticationHandler.cs#L65).
- Bookshelf counterpart/integration point: [src/Readarr.Http/Authentication/ApiKeyAuthenticationHandler.cs](../src/Readarr.Http/Authentication/ApiKeyAuthenticationHandler.cs#L64).

**Bookshelf status:** partial; all three key transports already exist. B `src/Readarr.Http/Authentication/ApiKeyAuthenticationHandler.cs:26` captures `_apiKey` on construction; `HandleAuthenticateAsync:55` uses `==`. S5 counterpart `src/Sonarr.Http/Authentication/ApiKeyAuthenticationHandler.cs:28` retains IConfigFileProvider; line 65 compares the current key using `CryptographicOperations.FixedTimeEquals`.

**Action/dependencies:** port the small handler change with framework constructor adaptation. Keep existing X-Api-Key, query-key and Bearer parsing precedence unless a separately versioned contract change is approved. Confirm actual DI/request lifetime before claiming the old implementation requires restart to rotate a key.

**Book/audio caution:** preserve the saved API key and integration authentication contracts used by Readarr-compatible clients.

**Acceptance:** current keys work in each supported transport; incorrect/empty keys fail; after rotation, newly authenticated requests reject the old key and accept the new key; SignalR's access_token scheme remains functional; logs never include supplied keys. Fixed-time comparison is observable source hardening, not evidence of a demonstrated remote timing exploit.

**2026-11-14 implementation record:** the uncommitted
`fix/sonarr-v5-foundation` working tree adapts the pinned v5 handler to .NET 6,
retaining its existing constructor/clock contract. The
[API-key handler](../src/Readarr.Http/Authentication/ApiKeyAuthenticationHandler.cs)
retains `IConfigFileProvider` and reads its effective key once when a supplied
nonblank credential is authenticated. A null/blank configured key returns no
authentication result. The comparator uses `CryptographicOperations.FixedTimeEquals`
on read-only UTF-16 byte spans, preserving exact ordinal equality without an
encoding fallback or temporary key-byte allocations. Distinct malformed UTF-16
values cannot collapse into the same replacement bytes. Different lengths may
return early; this is fixed-time content comparison, not a timing benchmark.

Parsing is unchanged: the configured query parameter takes priority over
`X-Api-Key`, which takes priority over the existing Authorization fallback.
An empty or incorrect higher-priority value does not fall through; repeated
values retain first-value behavior. The legacy `Bearer ` replacement behavior,
ticket/claim names and 401/403 responses remain compatible. SignalR continues to
use `access_token` as its query name. No key is regenerated by this port, and
credentials, database schema, proxy settings and library/media are unaffected.

The previous transient handler normally refreshed its constructor snapshot on
the next request already. This change narrows the construction-to-authentication
window and hardens comparison; it does not establish a previous restart-required
rotation defect. Framework authentication results remain cached within one
request, and established connections are not forcibly disconnected. A configured
`Readarr__Auth__ApiKey` override retains precedence over an XML reset; clients
must use the effective configured key. Existing key generation/reset and client
reload policies are unchanged.

[API-key regression fixtures](../src/NzbDrone.Integration.Test/Authentication/ApiKeyAuthenticationFixture.cs)
passed **50 new cases**, including post-initialization rotation, credential
precedence, malformed UTF-16, claims, 401/403 responses and captured handler logs.
The affected API/authentication suite passed **211 cases** total with no failures
or skips, including the existing P02/P03 and recording-identity cases. The HTTP
project build and self-contained linux-x64 publish passed.

The packaged candidate passed **89 checks** through a local HTTPS proxy in a
fresh disposable home. These include the P03 forwarding/login/WebSocket
regressions, every supported API/SignalR credential transport and precedence,
and the real `ResetApiKey` command. Once the command completed, old keys returned
401 and new keys succeeded on fresh requests using each transport, without a
server restart. A fresh SignalR WebSocket using the new `access_token` completed
its JSON handshake and closed cleanly. Rotation preserved the user and every
other XML setting. Three private debug log files contained neither synthetic
key. All owned servers, proxy listeners and WebSockets were stopped.

P05 is **Verified** locally on this uncommitted working tree, based on
`38f48b1716fb51adb648c087c33ebe1fa620438e`. No live development or production
server, home, API key or media has been changed. This is not a full browser or
cross-platform audit, nor proof of immediate revocation within an existing
request/connection. P09 remains the deployment blocker, with no package or
committed audit-policy change in P05. This batch was followed by [P06](#p06).

### P06

**Bound log-cleansing regex work and fail closed on timeout**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial; secret cleansing already exists and includes book-specific rules.
- Approach: adapt/merge.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs#L101).
- Bookshelf counterpart/integration point: [src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs](../src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs#L65).

**Bookshelf status:** partial; secret cleansing already exists and includes book-specific rules. B `src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs:10` defines regexes without a match timeout and `Cleanse:65` loops directly. S5 `CleanseLogMessage.cs:12` supplies a one-second match timeout, bounded fallback rendering and `TryCleanse:101`; its catch at line 129 fails closed.

**Action/dependencies:** merge the bounded execution/fallback algorithm into B's rules rather than replacing the ruleset. Keep B's broader token/access_token matching and Goodreads rules (`B:13`, `B:49`); add relevant upstream email/announce improvements. Audit all log layouts/targets using the new overload.

**Book/audio caution:** Goodreads/Hardcover/Audible credentials and book-provider URLs must remain cleansed; Sonarr's TV service list is not a sufficient replacement.

**Acceptance:** malformed or very long adversarial strings return within the limit and never fall back to raw secret text; existing token/provider fixtures remain redacted; normal logs preserve useful diagnostics; render failures and exceptions are covered. Use synthetic sentinel secrets only.

**2026-11-14 implementation record:** the uncommitted
`fix/sonarr-v5-foundation` working tree adapts the pinned v5 algorithm while
preserving Bookshelf's broader API/access-token/Goodreads and Readarr webhook
rules. The [cleansing processor](../src/NzbDrone.Common/Instrumentation/CleanseLogMessage.cs)
adds the donor's query/announce refinements and email local-part redaction.
Every regex has a 100 ms match timeout. A one-second cleansing budget is checked
between rules, matches and captures and after IP masking; rules with infinite
or longer engine timeouts are rejected. Input, intermediate capture replacement
and final output are capped at 65,536 UTF-16 characters. This bounds processing
and expansion; it is not a hard real-time scheduling guarantee.

On timeout, oversize output, evaluator error or an observable rendering exception,
the complete payload becomes `(removed, log message could not be safely cleansed)`.
There is no raw-prefix truncation, partially cleansed result, exception diagnostic
echo or second rendering attempt. This deliberately avoids the donor fallback:
truncating before cleansing can cut away the delimiter needed to recognize a
secret. Null/empty/ordinary messages retain their existing behavior below the
size limit. The render callback runs inside the protected boundary, but arbitrary
third-party rendering/`ToString` code cannot be preempted by the regex deadline.

[CleansedLayout](../src/NzbDrone.Common/Instrumentation/CleansedLayout.cs)
protects configured console, debugger, update-file and syslog layouts;
[application file targets](../src/NzbDrone.Common/Instrumentation/NzbDroneFileTarget.cs)
cleanse once around layout rendering. The [database target](../src/NzbDrone.Core/Instrumentation/DatabaseTarget.cs)
protects message/exception rendering independently while preserving NLog's
cached message snapshot, normal exception metadata and real database error
propagation. Its fields have separate cleansing budgets. JSON string leaves
inherit the safe processor without adding a raw malformed-JSON parser fallback.
[Emergency exception handlers](../src/NzbDrone.Common/Instrumentation/GlobalExceptionHandlers.cs)
also guard and cleanse their direct console output, rendering each exception
once while preserving Error/Fatal and the legacy heartbeat-warning behavior.
NLog may format events or emit its own internal diagnostics before application
target guards run; these changes do not replace that third-party subsystem or
guarantee recognition of arbitrary unknown secret formats.

New regression fixtures cover [bounds and failures](../src/NzbDrone.Common.Test/InstrumentationTests/CleanseLogMessageSafetyFixture.cs),
[ordinary/provider rules](../src/NzbDrone.Common.Test/InstrumentationTests/CleanseLogMessageFixture.cs),
[real files and layouts](../src/NzbDrone.Common.Test/InstrumentationTests/CleansedLogTargetsFixture.cs),
[JSON string leaves](../src/NzbDrone.Common.Test/InstrumentationTests/CleansingJsonVisitorFixture.cs),
[emergency console handlers](../src/NzbDrone.Common.Test/InstrumentationTests/GlobalExceptionHandlersCleansingFixture.cs),
[database rendering/cached snapshots](../src/NzbDrone.Core.Test/Instrumentation/DatabaseTargetRenderingFixture.cs)
and [configured UDP syslog](../src/NzbDrone.Core.Test/Instrumentation/ReconfigureLoggingCleansingFixture.cs).
Validation passed **214 focused Common cases**, **3,310 offline Core cases**
(55 existing skips) and **211 affected API/authentication cases**, with no
failures. The Core suite includes the 14-case logging slice, real database
rendering, cached mutable-parameter snapshots, SQLite failure propagation and
loopback UDP syslog. File/layout and emergency-console fixtures exercise actual
output boundaries, without registering or crashing a real AppDomain. The
self-contained linux-x64 package built successfully.

The candidate passed **108 packaged checks** in a fresh disposable home with
console/file Trace logging and a local HTTPS proxy. A normal secret-bearing
User-Agent retained its useful context with the secret removed; a bounded-size
header whose redactions expanded beyond the output cap produced only the
failure placeholder, and the server remained healthy. Four private text logs
and 52 log-database rows contained neither API key nor probe secrets; console
and trace-file checks confirmed the oversize payload's context was discarded.
P03/P05 proxy, login, real key rotation and fresh authenticated WebSocket checks
also passed. All owned processes and listeners were stopped.

P06 is **Verified** locally on this uncommitted working tree, based on
`38f48b1716fb51adb648c087c33ebe1fa620438e`. This is not a full browser/platform
audit, a guarantee about arbitrary third-party renderers, or a rewrite of
already stored logs. No live development or production logs, settings, homes,
keys or media have been modified. P09 remains the deployment blocker; no
dependency or committed audit-policy change is part of P06. This batch was
followed by [P07](#p07).

### P07

**Validate every restore payload before replacing config/database files**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial; scheduled/manual backups and SQLite online backup already exist.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Backup/BackupService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Backup/BackupService.cs#L132).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Backup/BackupService.cs](../src/NzbDrone.Core/Backup/BackupService.cs#L129).

**Bookshelf status:** partial; scheduled/manual backups and SQLite online backup already exist. B `src/NzbDrone.Core/Backup/BackupService.cs:129` restores zip members by filename and line 165 moves any non-zip upload to the DB restore path. S5 `BackupService.cs:132` cleans stale temp contents, validates all candidate config/database files before moves, and cleans in finally; `ValidateConfigFile:265`/`IsValidConfigFile:273` parse XML; `IsSqliteDatabase:288` checks SQLite magic. S5 tests `src/NzbDrone.Core.Test/BackupTests/BackupServiceFixture.cs:72` cover invalid XML/DB, mixed-invalid zip and cleanup.

**Action/dependencies:** adapt Bookshelf names and retain legacy Readarr backup acceptance. Preserve backups before schema work. Header validation establishes type, not full DB integrity or schema compatibility; test those separately. Keep archive containment safeguards independent of this change.

**Book/audio caution:** preserve B's cache-database semantics and clarify cache rebuild versus persistent catalog/history; upstream has no equivalent third cache DB. PostgreSQL backup remains a separately managed operation.

**Acceptance:** invalid direct DB/XML and mixed-invalid archives change neither live config nor restore DB; failed extraction/validation leaves no stale restore files; valid legacy zip restores a disposable upgraded home with identity/history intact; truncated SQLite, wrong schema and version mismatch receive safe errors. Do not claim two-file move is transactionally atomic solely because both payloads are validated first.

**2026-11-14 implementation record:** [BackupService](../src/NzbDrone.Core/Backup/BackupService.cs)
validates all restored payloads before replacing configuration or a pending
database. Direct XML restores target configuration; direct SQLite restores
stage the pending database. ZIP restores accept the application's root-level
`config.xml` and `readarr.db` payloads while preserving the original backup
archive. [ZIP metadata preflight](../src/NzbDrone.Core/Backup/BackupArchivePreflight.cs)
runs before the archive reader can allocate from untrusted entry counts,
including ZIP64 metadata. Only backup restoration uses this extraction path. Archives are limited to 64
flat, regular, unencrypted entries; unsafe paths, links, duplicate names and
unsupported entry forms are rejected. Restored entries are streamed with
declared/actual-size limits and CRC verification. Configuration is limited to
1 MiB and a database payload to 8 GiB; these validation limits do not increase
existing HTTP upload limits.

[BackupRestoreValidator](../src/NzbDrone.Core/Backup/BackupRestoreValidator.cs)
requires well-formed XML with one unqualified `Config` root, prohibits DTDs and
external entity resolution, and limits parser input. Database validation opens
the staged file read-only with pooling disabled, checks the SQLite header and
`integrity_check(1)` with a 30-second cooperative progress limit, and requires
native Bookshelf book tables and their base
identity columns. Applied migration identifiers must be integral, unique and
known to this source, including the existing legacy identifier 158; a maximum
version comparison alone would reject legitimate homes or accept unknown IDs.
Newer password/audiobook columns are not required from an older backup. This is
a structural/compatibility check, not a complete business-data audit or semantic
validation of every configuration value.

Each [upload](../src/Readarr.Api.V1/System/Backup/BackupController.cs) has a private
directory (0700 on Unix) and owned input stream, with cleanup after success or failure.
Restore staging and preimages use a separate private directory on the home's
filesystem. Existing shared temporary directories are not consumed or deleted.
Restores serialize within the process, retain preimages of configuration and an
existing pending database until publication succeeds, and roll back attempted
replacements on synchronous errors. XML-only restoration leaves any existing
pending database intact. Failed rollback retains private recovery material and
reports failure instead of claiming a successful restore.

At the next startup, [DatabaseRestorationService](../src/NzbDrone.Core/Datastore/DatabaseRestorationService.cs)
revalidates the pending database before moving live files. It rotates the old
main database, metadata cache and their journal/WAL/SHM sidecars into a common
unique `.pre-restore-…` recovery group. A successful database restore rebuilds
the cache and retains the recovery files; the log database and media are not
replaced. Configuration-only restoration does not purge the cache. Move errors,
including failures reported after a move or leaving both copies, trigger bounded
per-file rollback while preserving available originals and the candidate. A
rollback failure still aborts startup and leaves recovery copies available.

PostgreSQL configuration-only XML/ZIP restores remain supported. Restoring a
SQLite database while PostgreSQL is configured is rejected both when staging
and at startup; PostgreSQL data backup/restore remains separately managed.
This batch adds no database migration or book/recording-model change. Restores
preserve the credentials, identities and history contained in the backup and
do not change media ownership or the existing API-key policy.

[Restore-service fixtures](../src/NzbDrone.Core.Test/BackupTests/BackupServiceRestoreFixture.cs)
cover malformed payloads, mixed-invalid archives, SQLite/schema/version checks,
archive safety, cleanup, serialized attempts and publication rollback.
[Startup fixtures](../src/NzbDrone.Core.Test/Datastore/DatabaseRestorationServiceFixture.cs)
cover pending validation, main/cache recovery groups and failures before/after
moves. [API upload fixtures](../src/NzbDrone.Integration.Test/ApiTests/BackupControllerRestoreFixture.cs)
cover validation responses, independent private uploads, stream disposal and
cleanup. **89 restore/startup cases passed within 3,399 offline Core cases**
(55 existing skips), and **224 API/authentication cases** passed, including
13 upload/controller cases. There were no failures. The self-contained
linux-x64 publish passed. Its **51 packaged checks** passed, including
11 invalid uploads, current and genuine pre-P02 application-generated backup
round trips, cache rebuild/config-only preservation, and owned-process cleanup.
The packaged gate uses only fresh synthetic homes and application-generated
backups, comparing author/book/edition/file identities, user Identifier, API key,
history and owned fake-media hashes. Its genuine legacy case uses the available
pre-P02 runtime at the reviewed base (schema 47 plus legacy migration 158); it
does not establish acceptance of every ancient Readarr backup.

Recovery assumes ordinary local filesystem behavior and application-owned
files; it does not defend against a hostile concurrent filesystem writer or
coordinate separate processes. The configuration/pending-database publication
and main/cache rotation are not durable multi-file transactions. Power loss or
process termination can require manual recovery, and successful promotion does
not provide automatic rollback if a subsequent schema migration fails. Keep a
matching database/configuration/runtime backup, and treat retained recovery
copies as private application data. No live development or production home,
backup, credentials or media was changed. P09 dependency advisories remain a
deployment blocker. P07 is **Verified** locally on this uncommitted working
tree; that batch was followed by [P08](#p08).

### P08

**Add bounded login-attempt lockout**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing lockout; existing authentication logging is present.
- Approach: adapt.
- Prerequisites: [P03](#p03).
- Pinned v5 source: [src/Sonarr.Http/Authentication/AuthenticationService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Http/Authentication/AuthenticationService.cs#L57).
- Bookshelf counterpart/integration point: [src/Readarr.Http/Authentication/AuthenticationService.cs](../src/Readarr.Http/Authentication/AuthenticationService.cs#L29).

**Bookshelf status:** missing lockout; existing authentication logging is present. S5 `src/Sonarr.Http/Authentication/AuthenticationService.cs:30` defines five failed attempts and five-minute lockout; `Login:57`, `IsLockedOut:237`, `RecordFailedAttempt:246` and success clearing implement it. `AuthenticationController.cs:73` exposes an end time for UI feedback. B `src/Readarr.Http/Authentication/AuthenticationService.cs:29` directly checks a password and logs failure.

**Action/dependencies:** P03 first, then port cache-backed policy and UI feedback; decide whether Bookshelf retains Basic and how equivalent protection applies there. Upstream uses per-IP in-memory counters; evaluate concurrency and cache cardinality, and do not present it as persistent/distributed protection.

**Book/audio caution:** library appliances often share one reverse-proxy/NAT address; count resolved clients correctly to avoid one user's failures blocking everyone.

**Acceptance:** failures at the threshold block the correct source, successful authentication clears counts, expiry reopens access, parallel attempts behave predictably, spoofed XFF cannot evade/change attribution, and shared proxies do not collapse configured clients accidentally. Check Forms/Basic/External/API-key modes separately.

**2026-11-15 implementation record:** the earlier foundation is committed as
`a80a97d6c`. [LoginLockoutPolicy](../src/Readarr.Http/Authentication/LoginLockoutPolicy.cs)
now shares bounded in-memory counters across application scopes. Five failed
password checks from one resolved client address lock that source for five
minutes. Blocked attempts do not extend the deadline or check passwords;
success clears the failure count, and five minutes of inactivity expires an
incomplete failure sequence. Entries remain pinned while callers wait for a
per-address verification gate, so concurrent attempts cannot create a second
counter or overshoot the threshold. Password hashing does not hold the global
bookkeeping lock.

Tracking is capped at 4,096 addresses. Expired idle entries are removed; active
lockouts are not evicted to admit new addresses. At capacity, untracked sources
are denied with fixed retry feedback while tracked sources retain their own
state. Space becoming available reopens admission; continued pressure can keep
new sources unavailable. This is process-local protection, not persistent,
distributed or comprehensive denial-of-service protection. Restart clears it.

[AuthenticationService](../src/Readarr.Http/Authentication/AuthenticationService.cs)
applies the policy to Forms and Basic password authentication, using P03's
resolved connection address and IPv4-mapped normalization. It never trusts a
raw forwarded header. API keys, existing cookies, None and External retain
their existing authentication behavior; the External login path does not gain
password throttling. Authentication mode is read per call rather than kept in a
static constructor snapshot. Existing password derivation/migration is unchanged.

The [Forms controller](../src/Readarr.Http/Authentication/AuthenticationController.cs)
returns an encoded `lockedUntil` UTC timestamp for lockout feedback and encodes
`returnUrl` independently, preventing nested query injection. Ordinary failure
continues to use `loginFailed=true`. The [login page](../frontend/src/login.html)
shows an accessible lockout status, disables submission until expiry and resumes
after browser history restoration; the server remains authoritative.
[Basic authentication](../src/Readarr.Http/Authentication/BasicAuthenticationHandler.cs)
keeps HTTP 401 and its challenge and adds `Retry-After` when locked. Malformed,
invalid UTF-8, missing-colon and oversized (over 8 KiB) authorization headers
fail authentication instead of throwing an HTTP 500. Password colons and
case-insensitive Basic schemes remain supported.

Validation passed **225 authentication/API cases**, including **43 new cases**
in [policy](../src/NzbDrone.Integration.Test/Authentication/LoginLockoutPolicyFixture.cs),
[service](../src/NzbDrone.Integration.Test/Authentication/AuthenticationLockoutFixture.cs)
and [presentation](../src/NzbDrone.Integration.Test/Authentication/LoginLockoutPresentationFixture.cs)
fixtures. These cover fake-clock expiry, capacity pressure, concurrency, shared
DI state, source normalization, modes and safe response handling. All **39
frontend cases** passed, including [seven login-page cases](../frontend/test/loginLockout.test.mjs);
the production webpack/type build passed with its existing configuration warnings.
The self-contained linux-x64 candidate passed **50 packaged checks**, including
real Forms/Basic thresholds, correct-password rejection while locked, client
isolation through a trusted HTTPS proxy, rotating untrusted forwarded headers,
secure cookies, API-key availability and restart reset. Real five-minute expiry
was covered with the injected clock rather than a five-minute package wait.
**Nine isolated Chromium checks** of built login HTML covered phone layout,
Enter while disabled, accessible status, expiry/history restoration and ordinary
failure display. No failures or skips occurred in these focused suites.

P08 is **Verified** locally. All owned test servers were stopped; no live
development/production home, server, key, user or media was changed. P09 remains
the next item and the current dependency-advisory deployment blocker. Its
runtime-independent security work precedes the larger P01/P12/P13 upgrades;
this does not mark those runtime/native-platform gates complete.

### P09

**Refresh managed/native packages as a staged compatibility change**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: partial; already centralizes and locks dependency versions.
- Approach: adapt, not blanket replacement.
- Prerequisites: [P01](#p01).
- Pinned v5 source: [src/NzbDrone.Core/Sonarr.Core.csproj](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Sonarr.Core.csproj#L18).
- Bookshelf counterpart/integration point: [src/Directory.Packages.props](../src/Directory.Packages.props#L2).

**Bookshelf status:** partial; already centralizes and locks dependency versions. B `src/Directory.Packages.props:2` is the authoritative inventory (e.g. System.Text.Json 6.0.10, Npgsql 7.0.10, Servarr FluentMigrator 3.3.2.9, NUnit 3.14). S5 `src/NzbDrone.Core/Sonarr.Core.csproj:18` uses FluentMigrator 8.0.1 and line 26 Npgsql 10.0.3; `src/NzbDrone.Common/Sonarr.Common.csproj:20` uses System.Data.SQLite 2.0.4; S5 `Directory.Build.props:159` starts its newer test package inventory.

**Action/dependencies:** after P01, update framework-aligned packages, then database/native libraries (P12/13), test tooling and the remaining compatibility surface. Preserve central management/lockfiles rather than adopting upstream's per-project duplication. Establish an audit/SBOM baseline using current package advisories during implementation.

**Book/audio caution:** S5 does not exercise Bookshelf-only PDF/EPUB, TagLib, Calibre, Goodreads/Hardcover dependencies. Upstream `Directory.Build.props:100` suppresses several ImageSharp advisories with a comment that patches are only in 4.x; **do not inherit those suppressions as a security fix**. B's Polly 8 and MediaInfo 26.10 already differ from upstream and should not be downgraded.

**Acceptance:** locked clean restore and no unexplained dependency downgrade/suppression; package audit reviewed with dated evidence; import/metadata/cover parsing and sqlite native-load smoke cases pass on each supported RID; newer NUnit test discovery is confirmed.

**2026-11-14 advisory record:** the existing central versions remain MailKit
4.8.0 and SixLabors.ImageSharp 3.1.11; this batch has not upgraded packages.
MailKit's [STARTTLS response-injection advisory](https://github.com/jstedfast/MailKit/security/advisories/GHSA-9j88-vvj5-vhgr)
identifies 4.16.0 as patched. Six Labors identifies ImageSharp 3.2.0 as the v3
security update and requires a [valid license key for that update](https://sixlabors.com/posts/security-support-and-license-enforcement/).
P09 must address these existing advisories and the patch's build requirements;
it remains Planned, and dependency advisories currently block deployment.

Isolated P26/P02 tests use only a command-line NU1902/NU1903 warning-as-error
exception, leaving auditing enabled and advisory warnings visible. No audit
suppression is committed, and passing those tests does not resolve the security
or deployment gate. No live development or production installation was changed.

**2026-11-15 security-maintenance record:** P09 is **In progress**, with a
runtime-independent security slice implemented ahead of the larger P01 runtime
migration. The SDK prerequisite now uses 10.0.403 through `global.json`, mise and
CI so the audit policy is enforced consistently; runtime targets stay net6.0. The original 24-lock baseline matched nine package IDs and 16
advisories. Central transitive pinning and auditing of all dependencies now keep
those indirect packages visible. The updated graph uses MailKit/MimeKit 4.17.0,
CodePages 8.0.0, Pkcs 8.0.2, JWT/JsonWebTokens 6.35.0, Uri 4.3.2 and
RegularExpressions 4.3.1; ImpromptuInterface 8.1.0 and removal of the obsolete
Runtime.Loader package remove unnecessary legacy dependency edges. No advisory
suppression or audit-warning exception is part of the change.

Builds no longer require a Six Labors key: ImageSharp and PdfSharpCore are replaced
with bundled Magick.NET 15.1.0 raster resizing and managed PdfPig 0.1.16 PDF
metadata. Removing both former libraries also removes their Six Labors
transitives. Image format/size/resource safeguards and owned-stream PDF handling
have dedicated regression cases; EPUB parsing and recording/media ownership are
preserved. Complete upstream notices are included in application output and
publish packages. The [dated dependency audit](dependency-audit.md) records
versions, source links, scope, SBOM and the final validation evidence.

Clean locked restores and the 25-project transitive NuGet audit passed with
zero reported vulnerabilities. The schema-validated CycloneDX snapshot includes
25 locks, 235 package IDs and 242 exact components. Regression checks passed
691 Common cases (35 existing skips), 3,439 Core cases (55 existing skips),
267 API cases, and 64 focused media cases. Published-binary probes passed
22 checks on glibc x64 and 40 each in musl x64/ARM64 containers; packaged
authentication and backup/restore checks passed 50 and 51 cases. All three
Linux publications include matching notices and their proper native libraries.
The ARM helper was genuinely cross-built and directly tested with version/WAV
JSON under QEMU; full ARM child-process/media-import acceptance is separate.
Pinned-v5 runtime OS detection was adapted in commit `698a8d594` to remove
the unnecessary `uname` child process. This confirms the security-maintenance slice, not
the remaining P01/P12/P13 framework/database updates or full P21/P22 platform
matrix. P09 therefore remains **In progress**. Zero package-advisory matches do
not establish absence of embedded native or OS issues. No live development or
production home, credentials or media was changed.

**2026-11-15 framework-alignment record:** `d572aaa2b` and `2b0edf8ef` implement
the .NET 10 portion of P01 and advance P09's managed/test-tooling slice. Applicable
Microsoft framework-aligned packages use **10.0.15**, MailKit/MimeKit **4.18.2**,
NLog **5.5.1** with NLog.Extensions.Logging **5.5.0**, test SDK **18.12.0**,
NUnit adapter **6.3.1**, XML logger **8.1.0** and coverage collector **10.1.1**.
NUnit itself remains **3.14.0**; this is not an NUnit 4 migration. Framework
assembly pruning and built-in SourceLink remove unnecessary package edges.
Six Labors libraries remain absent; no license key or advisory suppression is
introduced.

The separate [2026-11-15-net10 snapshot](dependencies/2026-11-15-net10/summary.json),
captured from clean `2b0edf8ef`, has **145 package IDs / 145 exact components /
25 locks / zero matching NuGet advisories**. Its
[CycloneDX inventory](dependencies/2026-11-15-net10/inventory.cdx.json),
[lock hashes](dependencies/2026-11-15-net10/lock-sha256.json) and
[advisory-feed provenance](dependencies/2026-11-15-net10/advisory-feed-provenance.json)
supplement rather than overwrite the earlier security snapshot. The strict
clean restore/audit, 4,551-case regression result, genuine same-home upgrade and
Linux/native/Docker evidence are recorded under [P01](#p01).

P09 stays **In progress** for the remaining managed/database/native compatibility
surface. FluentMigrator and SQLite engine/provider changes remain separate
P12/P13 work; full delivery integration, the platform matrix and source/release
identity remain P21/P22/P25 gates. Zero NuGet matches does not audit every native
codec, OS package or artifact's reachable code. Default ARM64 HTTP acceptance
still requires the native ARM64 runner recorded under P01.

### P10

**Carry cancellation through the HTTP client stack**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: partial asynchronous HTTP, missing caller-supplied cancellation.
- Approach: adapt.
- Prerequisites: [P01](#p01), [P09](#p09).
- Pinned v5 source: [src/NzbDrone.Common/Http/HttpClient.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Common/Http/HttpClient.cs#L31).
- Bookshelf counterpart/integration point: [src/NzbDrone.Common/Http/HttpClient.cs](../src/NzbDrone.Common/Http/HttpClient.cs#L19).

**Bookshelf status:** partial asynchronous HTTP, missing caller-supplied cancellation. S5 `src/NzbDrone.Common/Http/HttpClient.cs:31` adds optional CancellationToken to the public async surface and `ExecuteAsync:66` carries it through redirect/request execution; `Dispatchers/ManagedHttpDispatcher.cs:47` accepts it and line 62 links caller cancellation with request timeout. B counterparts expose async calls without caller cancellation and make a timeout-only source.

**Action/dependencies:** port interfaces, dispatcher, redirects, stream copies, implementations/mocks and concrete caller propagation together. Adopt modern APIs after P01. Upstream currently maps linked cancellation to `WebExceptionStatus.Timeout`; decide and test how Bookshelf differentiates user cancellation from actual server timeout instead of blindly inheriting retry behavior.

**Book/audio caution:** metadata refreshes across many editions/narrators must stop promptly on shutdown/cancel; stream disposal must not leave partial cover/download files or interrupt a completed import transaction.

**Acceptance:** cancellation during DNS/connect/response body/redirect/download stops promptly and disposes resources; request timeouts still work; retry layers do not restart canceled work; non-canceled concurrent requests survive; command cancellation and clean host shutdown are exercised with synthetic endpoints.

**2026-11-16 implementation record:** explicit cancellation overloads preserve the
existing synchronous/asynchronous HTTP surface and carry tokens through rate
limits, redirects, typed responses, the managed dispatcher and streamed downloads.
The dispatcher links caller cancellation with request timeout, preserves the
caller token in `OperationCanceledException`, and retains
`WebExceptionStatus.Timeout` for a real timeout. Cancellation does not dispose a
shared client or alter the IPv6 fallback decision. Download staging uses a unique
owned file and replaces the destination only after successful completion.

Running author/book refresh commands opt into API cancellation. Command tokens
also observe host shutdown; expected cancellation becomes `Cancelled`, while an
unrelated exception still fails the command. Cancelled scheduled tasks do not
advance their last-execution timestamp. Audible/Audnexus catalog discovery,
detail/chapter requests, response caching and cover requests observe the token
without caching cancellation as a failure or starting a provider cooldown.

Refreshes stage book fallbacks and missing-parent profiles before database
updates. Once an entity's apply unit begins, it finishes its existing book,
edition, series and tag writes before observing cancellation again. Completed
work still publishes its update; cancellation prevents subsequent refresh,
rescan and cover work. This does not make an in-progress import cancellable or
change recording/marketplace matching rules.

The reusable [local validator](local-validation.md#check-refresh-cancellation-and-shutdown)
adds an explicit `--checks cancellation` proof against a real packaged server
and owned loopback metadata. Deterministic transport tests cover a pending
connection callback, headers, response bodies, simultaneous timeout/cancellation,
redirects and downloads. Actual operating-system DNS interruption is not
simulated; the production socket connection receives the linked token. P11
retains the separate connection-family, DNS failure and losing-socket gates.

Implementation commits: `25f0540bb` (HTTP/transport/downloads) and `a13f988a1`
(command, metadata, cover and safe-apply propagation). The final offline suites
passed **722 Common**, **3,618 Core**, **8 API** and **65 Host** cases, with
**94 existing skips** in total. Legacy external-provider/updater integration
fixtures were excluded from the offline Core run; this is not a live-provider
regression claim. All **47 Python validation tests** passed.

A fresh self-contained Linux x64 package passed the owned-loopback cancellation
proof: **38 checks**, plus an unchanged-input check. A running refresh accepted
API cancellation and became `Cancelled` within five seconds, closed its partial
response, preserved the saved catalog/media, and completed a subsequent refresh
without cooldown/cache poisoning. A separate synthetic home stopped gracefully
within ten seconds after SIGTERM during a held body, preserving catalog/media
and excluding the incomplete response from cache. The actual application,
command API, HTTP dispatcher and database were used. Development and production
homes were not inputs, and the running dev service was not restarted.

The same candidate repeated **193 local package/app/restore/upgrade/media/Docker
checks**, with no failures and unchanged input packages. The upgrade used a
genuine .NET 6 baseline, and Docker used the actual repository Dockerfile and a
native musl-x64 package. Locked backend, frontend and both package builds passed.
GitHub workflows were neither modified nor run. Native ARM64/P01 and broader
P21 gates remain open; this HTTP change does not close them.

### P11

**Use per-connection Happy Eyeballs and expire pooled connections**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial; pooled HTTP/2, proxies and IPv6 fallback already exist.
- Approach: adapt.
- Prerequisites: [P01](#p01), [P10](#p10).
- Pinned v5 source: [src/NzbDrone.Common/Http/HappyEyeballs/HttpHappyEyeballs.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Common/Http/HappyEyeballs/HttpHappyEyeballs.cs#L24).
- Bookshelf counterpart/integration point: [src/NzbDrone.Common/Http/Dispatchers/ManagedHttpDispatcher.cs](../src/NzbDrone.Common/Http/Dispatchers/ManagedHttpDispatcher.cs#L22).

**Bookshelf status:** partial; pooled HTTP/2, proxies and IPv6 fallback already exist. B `src/NzbDrone.Common/Http/Dispatchers/ManagedHttpDispatcher.cs:22` maintains global IPv6 availability flags and its `onConnect` globally disables IPv6 after a failure when IPv4 is routable. S5 `HappyEyeballs/HttpHappyEyeballs.cs:24` resolves addresses and races interleaved connections with 250ms staggering (`line 15`); `HappyEyeballs.cs:35` handles sorted attempts/cancellation/disposal. `ManagedHttpDispatcher.cs:158` adds a 30-minute PooledConnectionLifetime. S5 `NzbDrone.Common.Test/Http/HappyEyeballsFixture.cs` supplies algorithm cases.

**Action/dependencies:** port the reusable algorithm and tests after P01/10, retaining outbound proxy/certificate/cookie handling. Confirm disposal of losing sockets and behavior on networks without IPv6. Evaluate connection lifetime separately from request timeout.

**Book/audio caution:** provider refreshes may touch multiple hosts with different IPv6 reachability; one metadata host must not disable IPv6 for every audiobook provider.

**Acceptance:** IPv4-only/IPv6-only/dual-stack success, broken first family, multiple address failures, DNS failure, cancellation and losing-socket cleanup pass deterministically; SOCKS/HTTP proxy and TLS validation still work; DNS endpoint changes are eventually picked up by pooled renewal.

**2026-11-16 implementation record:** `2512159c9` adapts the two connector classes
from the pinned Sonarr v5 revision. Each new connection resolves its own endpoint
and interleaves IPv6/IPv4 attempts with a 250 ms stagger. Failed attempts advance
immediately. DNS duplicates and unsupported or explicitly excluded address
families are filtered before dialing. The old process-wide IPv6 availability
flags and interface probing are removed, so one host's failure does not change
another host's address-family selection.

The port strengthens the donor's ownership handling: synchronous attempt/delay
failures still cancel and drain existing attempts, every losing or late successful
socket is disposed, and cancellation during cleanup also disposes a selected
winner. Simultaneous DNS/connection failures and caller cancellation preserve the
original caller token. The connector owns the winning socket until its
`NetworkStream` handoff succeeds. P10's request-timeout versus caller-cancellation
classification remains intact.

The managed handler now sets a **30-minute pooled connection lifetime**. Expiry
renews connections within the cached HTTP client and does not interrupt an active
response. The callback connects to the handler's actual endpoint, including a
configured proxy; target routing, TLS/SNI, certificate validation, cookie
isolation, credentials and HTTP version settings remain handler-owned.

**Local test evidence:** **56 focused cases passed without skips**: **19 generic
race/ownership cases**, **15 DNS/connector cases**, **12 real loopback transport
cases**, and the **10 existing P10 dispatcher cases**. The wire checks exercised
IPv4 and IPv6 listeners, a forced IPv6 refusal followed by IPv4 success and an
independent IPv6 success, HTTP proxying, SOCKS5 remote DNS, HTTPS CONNECT/SNI,
certificate acceptance/rejection and cookie isolation. Separate pooling cases
prove default connection reuse, sequential idle-connection expiry and DNS endpoint
change, and survival of an active response. The expiry tests use a zero lifetime
to force the production renewal path deterministically; they do not wait 30
minutes or depend on changing public DNS.

The broader offline suites passed **768 Common**, **3,618 Core**, **8 API** and
**65 Host** cases: **4,459 passed**, with existing platform/fixture skips. Legacy
external provider/updater fixtures remain excluded from the offline Core run. DNS
interruption/failure is tested through an injected resolver sharing the production
connector path; the operating system's resolver is not artificially stalled.

Locked backend and frontend builds and fresh Linux x64/musl-x64 packages passed.
The combined local validator then passed **231 checks, zero failures**: package
**14**, app **15**, current restore **39**, genuine .NET 6 upgrade **27**, media
**42**, cancellation **38**, actual Dockerfile **53**, and **three unchanged-input
checks**. All library homes, metadata endpoints and media were synthetic. No
GitHub workflow was changed or run; the existing development service and
production appdata/media were unchanged. Native ARM64/P01 and broader P09/P21
acceptance remain open. **P12 is the next implementation slice.**

### P12

**Modernize the migration engine while preserving Bookshelf schema history**

- Priority / effort: **P1 / XL**.
- Current Bookshelf state: partial; its migration framework already handles SQLite/Postgres and table reconstruction.
- Approach: adapt.
- Prerequisites: [P01](#p01), [P09](#p09).
- Pinned v5 source: [src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs#L37).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs](../src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs#L33).

**Bookshelf status:** partial; its migration framework already handles SQLite/Postgres and table reconstruction. S5 `src/NzbDrone.Core/Datastore/Migration/Framework/MigrationController.cs:37` uses processor constants and `AddPostgres15_0:49`; `MigrationExtension.cs:41` wires FluentMigrator 8, SQLite type map and quoter; `NzbDroneSqliteProcessor.cs:51` adds AlterDefaultConstraintExpression handling; `NzbDroneSQLiteTypeMap.cs:45` maps DateTimeOffset consistently. B `NzbDroneSQLiteProcessor.cs:29` supports older alterations and `MigrationController.cs:49` uses the Servarr fork.

**Action/dependencies:** P01/09; port the infrastructure as one compatibility slice and append B migrations normally. Never replace its migration sequence with Sonarr's TV schema. Preserve quote/GUID/date-time conventions and test every existing migration that is replayed with the newer engine. S5 `Database.Version:47`/`DatabaseVersionParser.ParseServerVersion:10` is a useful small cleanup of B's exception-driven version probe.

**Book/audio caution:** B has migrations 001–047, metadata overrides, narrator aliases, editions, multipart files and a cache DB absent from Sonarr. All require upgrade fixtures; date conversion must preserve release dates and override timestamps.

**Acceptance:** empty SQLite and PostgreSQL homes migrate; representative old Bookshelf/Readarr schemas upgrade copies preserving IDs/constraints/indexes/data; rename/drop/default/date cases pass both DBs; injected migration failure leaves a recoverable backup; test expected UTC/timezone serialization and edition constraints.

**2026-11-16 implementation record:** `bb4cd939e` adapts the pinned v5 migration
infrastructure to **FluentMigrator 8.0.1** and **Npgsql 10.0.3**. Only the Core,
SQLite and PostgreSQL runners are referenced; unused provider dependencies and
the Servarr migration feed are removed. All 25 package lockfiles were restored
and checked in locked mode. The SQLite provider and busy timeout remain unchanged
for P13. No historical migration body or version was edited. Files 001–048 retain
their actual version set **1–33, 35–48, 158**: migration file 034 has always used
version 158 and is not renumbered.

The controller selects modern processor IDs, scopes/disposes migration services,
restores its previous context in `finally`, and isolates contexts with
`AsyncLocal`. It honors the supplied logger provider and omits connection strings
from migration-start logs. Database version checks use provider metadata instead
of deliberately issuing PostgreSQL SQL against SQLite and swallowing exceptions.

SQLite rename/drop operations use supported native ALTER statements. Type,
nullability and default changes rebuild from stored DDL rather than the old lossy
column approximation. Untouched definitions, CHECK/UNIQUE/foreign-key constraints,
collations, partial/expression indexes, triggers, views, generated columns and
AUTOINCREMENT high-water marks survive. Copying values stays inside SQL and does
not reinterpret dates or GUIDs. Unsupported ambiguous definitions and referenced
table rebuilds with foreign keys enabled fail before destructive work; native
DROP rejects dependent schema rather than silently removing it. The scoped
`legacy_alter_table` setting is restored after rebuilding.

A small PostgreSQL generator adapter retains the old ability to insert explicit
IDs by default; explicit `GENERATED ALWAYS` requests remain explicit. This covers
direct table declarations as well as `TableForModel`, without rewriting historical
migrations. Application GUID identifiers keep their existing text schema and
converter. Ordinary SQLite migration errors no longer masquerade as corruption
or trigger deletion/recreation of the log/cache database; actual corruption keeps
the existing recovery behavior.

**Source validation:** **87 focused cases passed without skips** on real SQLite
and owned PostgreSQL 16, covering all three histories, seven seeded checkpoints,
DDL changes, raw schema preservation, UTC/offset instants, nullable dates, native
GUID storage, rollback, context isolation and failure classification. The broader
offline suites passed **768 Common**, **3,682 Core**, **8 API** and **65 Host**
cases (**4,523 total**, with existing platform/fixture skips). All **23 existing
migration cases** also passed separately against owned PostgreSQL. Focused cases
overlap the broader suites. All **63 Python validation tests** passed. Locked
solution and Windows GUI restores passed; the refreshed dependency inventory
records **zero reported NuGet vulnerabilities** across 25 audited projects.

**Packaged acceptance:** fresh x64/musl-x64 delivery packages built successfully.
The actual pre-P12 binary created and seeded SQLite and PostgreSQL libraries;
the new binary upgraded those same databases. The explicit
[`datastore` check](local-validation.md#check-the-migration-engine-on-sqlite-and-postgresql)
passed **37 checks**, plus **two unchanged-input checks**, with no failures.
It preserved catalog identities, two file parts, narrators, overrides, cleanup
rules, history, settings, dates and the saved PostgreSQL schema. Fresh main/log/
cache creation passed. Deliberately incompatible migration-158 replay failed,
rolled back partial DDL and retained catalog rows. Application-generated SQLite
backup recovery and point-in-time PostgreSQL dump recovery both restarted the
server and preserved the expected data/schema. The validator compares logical
visible-column order because `pg_dump` compacts internal positions left by
dropped columns; types, defaults, nullability, indexes and constraints still must
match. Synthetic servers use `-exitimmediately` so intentional startup failures
do not wait for interactive input.

The same candidate also passed **231 package/app/restore/.NET 6 upgrade/media/
cancellation/Docker checks**. Owned PostgreSQL containers, sockets and app
processes were removed; input packages stayed unchanged. GitHub workflows, the
running development service and production appdata/media were unchanged. Native
ARM64/P01 and broader P09/P21 gates remain open. **P13 is next.**

### P13

**Upgrade SQLite provider and measure busy-timeout behavior**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial; SQLite WAL/pooling/backup already work in source.
- Approach: adapt.
- Prerequisites: [P01](#p01), [P09](#p09), [P12](#p12).
- Pinned v5 source: [src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs#L61).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs](../src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs#L59).

**Bookshelf status:** partial; SQLite WAL/pooling/backup already work in source. B `src/Directory.Packages.props:56` pins `System.Data.SQLite.Core.Servarr` 1.0.115.5-18; B `ConnectionStringFactory.cs:59` uses BusyTimeout 100. S5 `src/NzbDrone.Common/Sonarr.Common.csproj:20` uses System.Data.SQLite 2.0.4; S5 `ConnectionStringFactory.cs:61` preserves pooling/WAL (Truncate on macOS) and line 67 sets BusyTimeout 1000.

**Action/dependencies:** P01/09/12; verify native package RID assets and backup API changes. Measure contention before choosing a timeout; increasing it is a targeted reliability candidate, not a guaranteed throughput improvement.

**Book/audio caution:** simultaneous metadata refresh, audio analysis, covers and large multipart import updates have a different write load than episode updates. Retain B's cache-database connection path.

**Acceptance:** simultaneous read/write/import/backup activity has bounded latency and appropriate busy behavior on disposable DBs; SQLite backup/restore and journal changes remain valid; no data loss or permanent lock follows cancellation; native loading succeeds on glibc/musl/arm64. Preserve explicit values and platform journaling behavior.

**Implemented locally in `238cc9a0e`, 2026-11-17; native ARM64 acceptance remains open.**
The provider is now **System.Data.SQLite 2.0.4**, with separately bundled
**SourceGear.sqlite3 / SQLite 3.53.4** from the pinned v5 donor. Central versions,
all 25 lockfiles, licenses and the native RID/checksum lock are updated; the old
Servarr SQLite feed is removed. The resolver loads explicit bundled paths and
fails if delivery is incomplete. The glibc native assets require **glibc 2.34+**;
musl has a separate native asset. No host SQLite fallback is accepted.

The new native build defaults to foreign-key enforcement. Bookshelf explicitly
keeps its legacy **ForeignKeys=false** contract so the unchanged historical
migrations and application deletion behavior retain their existing semantics.
Production repositories preserve GUID TEXT identities and exact fractional UTC
instants across fr-FR/de-DE and America/Chicago/Asia/Kathmandu. Main/log/cache
connections retain UTC, pooling and the cache budget; Linux uses WAL and macOS
retains TRUNCATE plus FullFSync. The new **1000 ms busy timeout** is the measured
v5 policy choice, with the separate **30-second default managed command budget**
unchanged. It is neither a total-operation deadline nor a demonstrated throughput
improvement.

Online backups now copy to a unique unpooled staging database, check standalone
rollback journaling and `quick_check`, close the destination and publish by rename.
Failures retain the prior backup. Source-path and symlink-directory aliases,
existing destination sidecars, invalid snapshots and locked sources have
regression coverage. Copy retries use the source's no-progress timeout, resetting
only on progress; explicit zero stays unlimited. No unrelated global connection
pool is cleared. Each database snapshot is separate; this is not a transaction
across the three databases.

Paired probes against the genuine pre-P13 package and the candidate pass **98
checks** using the production connection factory and backup implementation. The
synthetic workload verifies exact author/book/history/part values, 30 multipart
recordings, concurrent WAL readers, metadata writes, log/cache activity, three
consistent backups, unchanged media and later writer recovery after interruption.
With a 350 ms held lock and a three-second command budget, both 100/1000 ms policies
succeed; with a 1600 ms held lock and one-second command budget, both report BUSY.
The supported connection interrupt completes in about 5–7 ms and rollback removes
the unfinished recording. The corrected probe joins the active query before
disposal; initial command-cancellation probe failures were harness errors and are
excluded from acceptance evidence. Candidate evidence includes the actual native
mapping/hash, source ID and compile options; deleting the engine from an owned
clone produces a DllNotFound failure without loading the host engine.

Local source suites pass **4,538 tests** (Common 768, Core 3,697, API 8, Host 65),
with existing skips retained; **23 PostgreSQL migration cases** also pass on an
owned PostgreSQL 16 server. All **73 Python validation tests** pass. Fresh package
startup, backup/restore, refresh cancellation and genuine pre-P12 SQLite/PostgreSQL
upgrade/recovery checks pass **131 checks**, including unchanged package inputs.
The dependency audit covers **120 exact components / 25 projects**, with zero
NuGet-reported vulnerable packages. See [local validation](local-validation.md)
and the [dependency record](dependency-audit.md#p13-sqlite-provider-and-engine-2026-11-17).

Fresh glibc and musl-x64 packages also pass **141 package/.NET 6 upgrade/media/
Docker checks**, including input fingerprints. The exact repository S6 image
exercises bundled SQLite writes/reads/integrity, codecs, PDF, audio tags and
MediaInfo, along with API startup/shutdown. The musl-ARM64 package passes **17
static package/input checks**; native execution is explicitly unrequested.

Native ARM64 runtime acceptance remains required for full P13 verification;
static assets or QEMU do not satisfy that gate. No GitHub workflows or live
service/appdata/media changes are included.

### P14

**Support full PostgreSQL connection strings with corrected validation**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: verified locally; full main/cache/optional-log connection strings coexist with preserved legacy component mode.
- Approach: adapt with upstream bug fixed.
- Prerequisites: [P09](#p09), [P12](#p12), [P15](#p15).
- Pinned v5 source: [src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs#L106).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs](../src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs#L20).

**Comparison baseline:** partial; PostgreSQL host/user/password/port/main/log/cache configuration already exists. S5 `src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs:106` selects full-string versus component settings; `GetPostgresConnectionInfoFromConnectionString:92` uses NpgsqlConnectionStringBuilder and Enlist=false. B constructor at line 20 supports component settings only. S5 `PostgresOptions.cs:13` adds the string options; `MigrationController.cs:92` removes Password through NpgsqlConnectionStringBuilder before logging.

**Action/dependencies:** P09/12/15; design all three B database strings and validation rules. S5 line 120 tests `!isMainDBConnectionStringSet && isLogDBConnectionStringSet`; line 125 repeats the same logical predicate, so main-only + enabled log is not caught there. Correct both one-sided cases and enforce mixed-source rules across host/user/port/password rather than copying this method.

**Book/audio caution:** Bookshelf has a third CacheDb; do not remove it to match Sonarr. Preserve existing component defaults/names and secrets, allow TLS parameters without returning the connection string to UI/logs.

**Acceptance:** exhaustive config table covers no Postgres, components, all strings, each missing string, log disabled, cache choices, mixed sources, invalid syntax, TLS flags and quoted/semicolon passwords; errors/logs contain sentinel-free redaction; existing homes choose the same databases after upgrade.

**Implemented and verified locally in `787a593e2`, 2026-11-17.**
Bookshelf now accepts `PostgresMainDbConnectionString`,
`PostgresCacheDbConnectionString` and `PostgresLogDbConnectionString`, with matching
`Readarr__Postgres__*DbConnectionString` environment options. Environment values
win over matching XML. Main/cache are always required in full-string mode; log
is required only when database logging is enabled. Disabled log strings/names
are ignored for mode selection, validation and connection creation. Partial
active sets fail before opening a database and never select SQLite instead.
No active strings preserves the old SQLite/component selection and names.

The mixed-source check reads actual source presence, including explicit empty,
zero and default ports, rather than treating effective default values as user
settings. The nullable typed port plus raw bound presence handle .NET 10 binding
without changing the public effective port contract. Active host/user/password/
port/main/cache names cannot coexist with full strings; the log name matters only
when logging is enabled. Each active string explicitly requires Host and Database.
`NpgsqlConnectionStringBuilder` preserves quoted passwords, TLS, pooling and other
provider options while forcing `Enlist=false`. No historical migration or package
version is changed.

Environment strings are not materialized into default XML or unrelated settings
saves, and existing XML strings retain exact quoting. Host API resources do not
expose them. XML strings intentionally remain in configuration backups. Parser,
XML and options-binding errors discard sensitive inner exceptions. PostgreSQL
startup errors retain only database purpose and strict SQLSTATE codes; connection
opens, logging-target failures and direct/wrapped API errors do not render raw
provider payloads. Migration classes retain stage/version logs, while arbitrary
PostgreSQL runner diagnostics are suppressed. Bounded cleansing removes whole
assigned/recognizable connection strings and handles quoted credential aliases;
SQLite corruption recovery and constraint responses remain unchanged.

**Validation:** **4,671 source tests** passed (Core 3,756, Common 828, API 13,
Host 74), including **111 new configuration/privacy cases**. Existing platform
skips remain; **23 PostgreSQL migration cases** and **90 Python tests** pass.
One existing loopback HTTP cancellation teardown returned Linux EINVAL once;
the unchanged Common suite passed on a clean rerun. The canonical locked backend
builds, frontend and glibc/musl-x64 packages passed.

The explicit `--checks postgresconnections` proof passes **115 checks**, including
input fingerprints. A genuine pre-P14 binary creates component-mode databases;
the candidate opens those same main/log/cache databases using full strings,
preserving schema, identities, two recording parts, history, cache, media and both
legacy/salted account logins. An owned SCRAM role genuinely authenticates a
password with semicolons, quotes and backslashes. XML/environment precedence,
settings saves, inactive log handling, 19 invalid configurations and three server
failure cases pass. Canaries remain absent from application logs/API outputs.
The previous package correctly fails the explicit SQLite-fallback assertion.
The P15 optional-log regression also passes **63 checks**, the SQLite native/locking
regression passes **52 checks**, and the normal package/app/restore/.NET 6 upgrade/
media/cancellation/S6 Docker command passes **233 checks**. These proofs use owned
PostgreSQL 16 with no network/published ports. TLS parameter retention is tested;
this Unix-socket proof does not claim TLS transport/certificate integration.

See [configuration](../README.md#postgresql-connection-strings) and
[local validation](local-validation.md#check-postgresql-connection-strings).
No running development service, production data or GitHub workflow was changed;
P13 native ARM64 and the broader platform/release gates remain open.

### P15

**Make SQL log storage optional**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: verified locally; database logging defaults on and can be disabled independently of main/cache storage.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates.
- Pinned v5 source: [src/NzbDrone.Core/Datastore/Extensions/CompositionExtensions.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Datastore/Extensions/CompositionExtensions.cs#L15).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Datastore/Extensions/CompositionExtensions.cs](../src/NzbDrone.Core/Datastore/Extensions/CompositionExtensions.cs#L8).

**Comparison baseline:** missing switch; it unconditionally registers/opens log and cache DBs. B `src/NzbDrone.Core/Datastore/Extensions/CompositionExtensions.cs:8` registers all three and `Startup.cs:225` initializes them. S5 `CompositionExtensions.cs:15`/`:29` chooses real/dummy log DB, `Bootstrap.cs:175` selects via config, `Startup.cs:313` opens/registers DatabaseTarget only when enabled; `ConfigFileProvider.cs:288` defaults LogDbEnabled to true.

**Action/dependencies:** keep default true; audit every log repository/controller/health task for dummy behavior and documentation. P14's full-string requirements must match this switch. Console/file logging remain independently configured.

**Book/audio caution:** the metadata cache is not the log DB. A log switch must never disable library history or the metadata cache. A valid no-log mode may help appliance deployments with limited disk writes.

**Acceptance:** default retains log API/history behavior; disabled mode starts without opening/creating log DB and leaves library/download history intact; log browsing fails gracefully; file/console logs work; PostgreSQL config doesn't require an unused log string; reenabling preserves the existing log DB where possible.

**Implemented and verified locally in `9c36a3bc9`, 2026-11-17.**
The optional XML `LogDbEnabled` / environment `Readarr__Log__DbEnabled` setting
uses a nullable option and defaults to **true**. Explicit false survives binding,
with environment precedence over XML. Both console-host and utility bootstrap use
the same selection. Changes require a restart: `ILogDatabase.IsEnabled` records
the registered database's effective startup state, so later configuration reads
cannot accidentally attach SQL logging to a disabled instance.

Disabled mode registers a dummy log database, initializes real main/cache
databases and leaves the SQL logging target unregistered. Log paging returns a
non-null empty page with existing sort/filter/paging behavior; clear and trim
commands are no-ops. The existing frontend empty state works without a new API
contract or UI setting. File/console/syslog logging and diagnostic `LogSql` tracing
remain independently configured. Library/download history, metadata cache,
authentication and backups keep their existing storage paths and contracts.

Application update-install history is also stored in the log database. Disabled
mode skips its reads/writes and the corresponding update-installed notifications,
matching the pinned donor behavior. Existing database contents are retained for
reenabling, subject to the usual enabled-mode retention rules. P14's full
connection-string validation remains separate; an unused PostgreSQL log database
is neither required nor opened in disabled mode.

**Validation:** all **4,560 source tests** passed (Core 3,704, Common 774, API 8,
Host 74), including **22 new config/registration/consumer/API cases**. Existing
platform skips remain. **23 PostgreSQL migration cases** and **81 Python
regression tests** passed. Canonical locked backend builds for all three declared
Linux RIDs, the frontend and glibc/musl-x64 delivery packages passed. The normal
local package/app/restore/.NET 6 upgrade/media/cancellation/S6 Docker command
passed **233 checks**, preserving default-enabled behavior and input packages.

The new explicit `--checks logstorage` command passed **63 checks**, including
its input fingerprint and cleanup. Fresh SQLite and owned PostgreSQL homes
exercise omitted/default true, XML false and environment false over XML true;
an unopenable SQLite log pathname and a PostgreSQL log database refusing
connections prove disabled mode never opens those databases. Enable/disable/
reenable cycles retain exact log/update rows, library/history/settings identities,
metadata cache and synthetic media, then verify a new SQL event after reenabling.
Disabled backups and ongoing file/console output pass. The genuine pre-P15 package
correctly fails the new disabled-log creation check, demonstrating that the proof
detects the missing feature. Only disposable homes and owned PostgreSQL 16
containers/private sockets are used; all owned processes/containers are stopped.

See [configuration](../README.md#database-log-storage) and
[local validation](local-validation.md#check-optional-database-log-storage).
No running dev service, production data or GitHub workflow was changed. P13's
native ARM64 gate and the broader platform/release gates remain open. **P14 is
next**, now that its optional-log prerequisite is verified.

### P16

**Review v5 authentication defaults, bootstrap and recovery as product policy**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: compatible authentication defaults, explicit account editing and local password recovery verified locally on SQLite/PostgreSQL.
- Approach: adapt account/recovery safeguards while retaining Bookshelf's authentication policy.
- Prerequisites: [P02](#p02), [P03](#p03), [P06](#p06), [P08](#p08).
- Pinned v5 source: [src/NzbDrone.Core/Authentication/InitialUserService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Authentication/InitialUserService.cs#L58).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Configuration/ConfigFileProvider.cs](../src/NzbDrone.Core/Configuration/ConfigFileProvider.cs#L193).

**Comparison baseline:** Bookshelf has a different policy, not simply missing
login. B `ConfigFileProvider.cs:193` preserves Basic/None and B AuthOptions has
no initial/reset options. S5 `ConfigFileProvider.cs:216` converts Basic/None
persisted modes to Forms/default Forms; `AuthenticationRequiredType.cs:7` adds
loopback-only bypass; `InitialUserService.cs:58` creates a first user or migrates
config credentials; `AuthenticationService.cs:112`/`:148` provides expiring/one-use
reset tokens and an environment-token recovery path.

**Donor boundary:** S5 `InitialUserService.cs:88` logs the generated password,
and `AuthenticationService.cs:143` logs the reset token. Its configured reset
token is disabled only in process memory after use (`:189`), so leaving the
environment setting reintroduces it at restart. Bookshelf does not adopt generated
bootstrap credentials, a forced Basic/None-to-Forms conversion, HTTP reset tokens
or an environment recovery secret.

**2026-11-18 implementation and acceptance record:** P16 is **Verified** locally
in `0b6c53444`, with the policy and evidence below. Existing Forms/Basic/None
defaults, local-address policy, API-key requirements and library identities stay
intact. Existing accounts remain shared administrators of one application and
library; this change does not introduce registration, roles or per-account
libraries.

The [host-settings controller](../src/Readarr.Api.V1/Config/HostConfigController.cs)
now selects an account by its saved identifier. A valid Forms/Basic identity is
pinned to its own credentials. API-key-only requests retain their administrative
scope and explicitly select an account when several exist. Unselected multi-account
GETs return safe account choices and a selection-required flag instead of failing
through `FindUser()` / `SingleOrDefault`. The frontend provides **Account to edit**.
Ambiguous credential/authentication-mode changes, unknown selectors and duplicate
usernames are rejected before settings writes. Unrelated general saves remain
available without a selection when credentials and authentication mode are
unchanged. Single-account editing remains compatible. Account choices expose only
identifier, username and whether a password exists; hashes and salts remain
private. The endpoint still requires the API key; optional Forms/Basic
authentication supplies the account identity without granting additional API
access.

The [password-recovery utility](../src/NzbDrone.Host/PasswordResetUtility.cs)
requires the server to be stopped and an absolute existing home:

```sh
Readarr -passwordreset -data=/absolute/existing/home
```

It uses the server's existing database configuration, including environment
overrides. Hidden interactive prompts accept the existing username or `id:GUID`,
then a new password and confirmation. There is one attempt with a total two-minute
input deadline and bounded input lengths. Account/input/provider failures produce
generic messages without echoing supplied values. The utility locks the home,
rejects a live recorded server PID and requires the current compatible existing
schema; it neither creates nor migrates a home or database. Recovery opens only
the main database and updates only the selected account's salted password through
the existing credential compare-and-swap path. Username, row ID, account
identifier, authentication mode, API key and library stay unchanged. No token is
created or consumed, so restarting cannot reenable a recovery token.

Forms cookies now carry a credential stamp checked against the saved account.
Changing/recovering credentials invalidates previous sessions for that account.
Cookies from before this check require one fresh login. Basic continues to verify
the supplied password per request, and API keys are independent of account
password recovery. Authentication validation responses redact attempted values.

**Compatibility limits:** general configuration and account credentials retain
the existing separate persistence order; a later credential conflict can occur
after unrelated general settings were saved. Account renames reject collisions,
with database serialization for competing PostgreSQL renames. Concurrent first
account creation is not a registration workflow or a new supported concurrency
contract. No identities are deleted or rotated to resolve ambiguity.

**Measured acceptance:** the explicit `authpolicy` proof passes **111 checks**
against a genuine pre-P16 baseline and owned SQLite/PostgreSQL 16 homes. It proves
Forms/Basic/None and API-key boundaries, multi-account selection and own-account
pinning, targeted recovery, old-cookie revocation, live-home and invalid-selector/confirmation rejection,
recovery-to-startup lock handoff, no credential canaries in responses/logs, and
preserved identities, other accounts, schema, library/history/cache/media,
authentication mode and API keys. The pre-P16 package fails specifically at the
old-cookie assertion when baseline and candidate occupy the same owned content
root. Every owned process/container/socket is cleaned up; input packages remain
unchanged.

Canonical locked backend and frontend builds and both x64 package preparations
pass. Source suites pass Core **3,776**, Common **828**, API **16**, Host **110**,
plus **44** authentication integration cases and **23** PostgreSQL migration
cases, retaining existing skips. An existing HTTP fixture teardown race was fixed
in `ad11a435b`: cancellation now joins the pending accept before closing its native
socket; the full Common suite then passes. Python validation passes **101 tests**.
Normal delivery passes **233 checks**; P14/P15 regressions pass **115 PostgreSQL
connection-string checks** and **63 optional-log checks**. The browser smoke test
passes desktop and 390px phone account selection, masked passwords, confirmation
clearing and a selected-account rename with other credentials unchanged.

Evidence is recorded in [local validation](local-validation.md#p16-verification-2026-11-18).
The running development service, production appdata/media and GitHub workflows
were unchanged. P02's native ARM work-factor benchmark and P13's native ARM64
runtime acceptance remain open, as do the broader platform and release gates.

### P17

**Add OIDC only if SSO is wanted**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: missing native OIDC; existing External authentication is not OIDC.
- Approach: adapt, optional.
- Prerequisites: [P01](#p01), [P03](#p03), [P04](#p04), [P06](#p06), [P16](#p16).
- Pinned v5 source: [src/Sonarr.Http/Authentication/AuthenticationBuilderExtensions.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Http/Authentication/AuthenticationBuilderExtensions.cs#L77).
- Bookshelf counterpart/integration point: [src/Readarr.Http/Authentication/AuthenticationBuilderExtensions.cs](../src/Readarr.Http/Authentication/AuthenticationBuilderExtensions.cs#L36).

**Bookshelf status:** missing native OIDC; existing External authentication is not OIDC. S5 `src/Sonarr.Http/Authentication/AuthenticationBuilderExtensions.cs:77` configures OIDC, `TryAuthorize:242` enforces the configured user identifier, `AddForms:222` retains Forms; `OidcOptionsChangeTokenSource.cs:33` reloads changed options; Core `OidcDiscoveryService.cs:27` probes authority; `AuthenticationController.cs:39` starts login and `:153` handles signout.

**Action/dependencies:** P01/03/04/06/16, options/API validation/UI and the framework OIDC package. Retain Forms fallback/recovery deliberately. Upstream implements one configured authorized user identifier, **not multi-user roles/RBAC**. Review subject versus verified-email matching, revocation on settings change, token retention and issuer/TLS validation before adopting.

**Book/audio caution:** SSO should not change API-key access for clients or imply separate per-user book collections. Sonarr's certificate-bypass hook in its OIDC backchannel is a policy to review, not a security benefit.

**Acceptance:** a disposable identity provider verifies correct/wrong identity, unverified email, callback/base path, cancellation, logout with/without provider end-session endpoint, changed config/principal rejection, invalid authority, HTTPS and proxy scheme handling; provider errors/logs do not leak tokens/client secrets.

**2026-11-18 product decision:** P17 is **Deferred** at the user's explicit
request to skip it. This batch adds no OIDC package, provider configuration,
callback flow or identity-provider dependency. Forms/Basic, API keys and the P16
local recovery policy remain the supported access paths. Reconsider SSO only
after a new product decision; completing adjacent prerequisites does not enable
this optional feature.

### P18

**Load PEM certificates, separate private keys and certificate chains**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial HTTPS; B `src/NzbDrone.Host/Bootstrap.cs:269` loads a certificate via older X509Certificate2 constructor and `ServerOptions` has no SslKeyPath.
- Approach: adapt.
- Prerequisites: [P01](#p01), [P09](#p09).
- Pinned v5 source: [src/NzbDrone.Common/Http/SslCertificateLoader.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Common/Http/SslCertificateLoader.cs#L10).
- Bookshelf counterpart/integration point: [src/NzbDrone.Host/Bootstrap.cs](../src/NzbDrone.Host/Bootstrap.cs#L269).

**Bookshelf status:** partial HTTPS; B `src/NzbDrone.Host/Bootstrap.cs:269` loads a certificate via older X509Certificate2 constructor and `ServerOptions` has no SslKeyPath. S5 `src/NzbDrone.Common/Http/SslCertificateLoader.cs:10` handles PEM and PKCS12, finds leaf/private key and creates an offline certificate context with intermediates; `Bootstrap.cs:203` passes it to Kestrel. `NzbDrone.Common.Test/Http/SslCertificateLoaderFixture.cs` covers the loader.

**Action/dependencies:** P01/09; add optional key path, preserve existing PFX/password configuration, API schema and redaction; package correct file permissions and actionable errors. Decide cert reload separately; the inspected path loads at host initialization.

**Book/audio caution:** many homes terminate TLS at an existing proxy; keep that deployment working and avoid forcing internal provider connections to inherit server-cert policy.

**Acceptance:** existing PFX still works; PEM bundled/separate keys and full chain handshake correctly; missing key/wrong password/unsupported types fail safely; IPv6/base-path listener and proxy configurations remain reachable; no private key/cert password appears in logs or API responses.

### P19

**Study in-process host restart and service lifecycle**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: existing restart spawns a process, with Windows-service exceptions.
- Approach: study first.
- Prerequisites: [P01](#p01), [P10](#p10).
- Pinned v5 source: [src/NzbDrone.Host/Bootstrap.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Host/Bootstrap.cs#L119).
- Bookshelf counterpart/integration point: [src/NzbDrone.Host/AppLifetime.cs](../src/NzbDrone.Host/AppLifetime.cs#L71).

**Bookshelf status:** existing restart spawns a process, with Windows-service exceptions. B `src/NzbDrone.Host/AppLifetime.cs:71` spawns a new process and `Handle:105` ignores this route for Windows services. S5 `Bootstrap.cs:119` loops host creation via `RunHostUntilShutdown`; `RunWithRestartCheck:311` reads RestartPending; `RestartableServiceLifetime.cs:15` keeps a service wrapper across host reinitialization; `AppLifetime.cs:88` handles requests for all modes.

**Action/dependencies:** P01/10 and lifecycle audit. This is not a drop-in process-restart fix: identify singleton/static state, background commands, event subscriptions, native MediaInfo processes, and open DBs before choosing it. Compare actual container/service restart expectations with supervisor behavior.

**Book/audio caution:** metadata caches, narrator/edition override caches and long audio scans must drain/dispose without duplicate subscriptions or zombie processes. Do not resume an interrupted import twice.

**Acceptance:** repeated setting-change restarts, explicit shutdown, service stop and container SIGTERM release ports/DB locks, don't duplicate workers/subscriptions, and don't leak resources; saved settings reload; updater handoff still replaces files correctly. A retained external-process strategy can be the correct outcome.

### P20

**Keep API versions coexistable and generate matching OpenAPI documents**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial; it already has versioned `api/v1` route attributes and OpenAPI.
- Approach: adapt pattern.
- Prerequisites: [P01](#p01), [P09](#p09).
- Pinned v5 source: [src/NzbDrone.Host/Startup.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Host/Startup.cs#L183).
- Bookshelf counterpart/integration point: [src/NzbDrone.Host/Startup.cs](../src/NzbDrone.Host/Startup.cs#L100).

**Bookshelf status:** partial; it already has versioned `api/v1` route attributes and OpenAPI. B `src/Readarr.Http/VersionedApiControllerAttribute.cs:13` uses version in the route but doesn't store a Version property; B `Startup.cs:100` exposes one document. S5 `VersionedApiControllerAttribute.cs:18` stores Version and `:37` adds V5; `Startup.cs:94` registers both API assemblies and `DocInclusionPredicate:183` separates controller/feed versions. `ConfigureHttpJsonOptions:103` aligns typed HTTP result serialization with MVC.

**Action/dependencies:** P01/09 if adopting new APIs; preserve Bookshelf's v1 contract for existing clients. Use a Bookshelf-owned new version only for an intentional breaking contract; Sonarr's API version number is not a target. Add one contract/schema authority for typed results and controllers.

**Book/audio caution:** Authors/Books/Editions/narrators/parts and audio-versus-ebook distinctions must retain correct names/types; TV Series/Episode resources are irrelevant.

**Acceptance:** current v1 clients still work; each version's OpenAPI includes only its intended routes/contracts/auth; enum/date/null/property casing stays compatible; generated client tests and SignalR payload validation pass; documented endpoints match the actual listener/base path.

### P21

**Run the delivery artifact through unit, DB and HTTP integration gates**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [P01](#p01), [P09](#p09), [P12](#p12).
- Pinned v5 source: [.github/workflows/build_v5.yml](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/.github/workflows/build_v5.yml#L126).
- Bookshelf counterpart/integration point: [.github/workflows/build.yml](../.github/workflows/build.yml#L109).

**Bookshelf status:** partial. B `.github/workflows/build.yml:109` runs `./test.sh Linux Unit Test` and then publishes two Docker variants; integration/automation projects exist but are not explicitly gated there. Its push/PR path filters exclude all `.github/**` changes (`:14`/`:18`). S5 `.github/workflows/build_v5.yml:126` runs OS-specific unit jobs, `:155` runs PostgreSQL 16/17/18, `:176` runs packaged-binary integration tests, and `:214` deploys only after those gates. `.github/actions/test/action.yml:82` runs discovered test DLLs.

**Action/dependencies:** P01/09/12; start with supported Linux/musl artifacts, SQLite plus one supported Postgres version, real hosted auth/API tests, and existing import regression suites. Run CI workflow changes in CI; remove the blanket workflow-path omission. Split build/test/publish privileges, gate publication and add concurrency cancellation as appropriate. Audit the current legacy softcover/hardcover release variants and keep only explicitly supported Bookshelf channels/config (P25).

**Book/audio caution:** CI must include representative EPUB/PDF, audio chapters/parts, narrator metadata, overrides and MediaInfo ABI/package tests. A Sonarr TV fixture suite cannot validate those.

**Acceptance:** PR jobs exercise workflow-only changes; failing unit/DB/import/auth tests prevent image publication; tested hashes/artifacts are the packaged ones; clean-state and upgrade integration tests use disposable homes; test discovery counts/categories are visible; deploy credentials are unavailable to untrusted PR execution. Add Windows/macOS gates only when those platforms are supported.

**2026-11-16 local-validation implementation record:** GitHub workflow work is
deferred while billing is unavailable. The local portion is implemented through
[scripts/validate-local.py](../scripts/validate-local.py) and the
[local validation guide](local-validation.md). The command runs package checks,
HTTP startup/authentication, current backup/restore and published media adapters
using fresh synthetic homes. Explicit older binary packages enable genuine
same-home upgrades and pre-password-schema backup restoration. Optional Docker
checks build the actual repository Dockerfile and exercise its S6 service and
native media adapters on the resulting image.

Each run has private evidence, bounded owned-process cleanup, stable failure
codes and nonzero failure/interruption exit status. Candidate, baseline and
Docker-package fingerprints must remain unchanged. Existing homes and nested
portable data folders are rejected; generated credentials and raw logs stay in
private ignored output. Paths with spaces and semicolons are exercised. The
runner neither dispatches workflows nor publishes images.

Preparing fresh artifacts exposed concurrent writers in the shared backend
output directories. Commit `8b7e07065` serializes solution publication; the
subsequent locked backend build and fresh frontend/Linux packages succeeded.
The local validation record is in [the guide](local-validation.md#verification-record).
An unsuitable legacy binary is rejected rather than counted as legacy coverage.
The genuine pre-password-schema positive path still requires that older binary.

P21 remains **In progress**: GitHub CI, PostgreSQL/migration-engine integration,
and the remaining P12/P13/P22/P25 acceptance are separate. Native ARM64 startup
still requires real ARM64 hardware; this x64 local suite does not close P01's
ARM gate. No workflow file, running development server or production data was
changed by this slice.

### P22

**Define a truthful support matrix and package each native dependency**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: partial: B `src/Directory.Build.props:13` declares linux-x64/linux-musl-x64/linux-musl-arm64; CI builds Docker amd64/arm64; scripts/build-mediainfo.sh explicitly limits bundled MediaInfo to Linux.
- Approach: adapt selectively.
- Prerequisites: [P01](#p01), [P09](#p09), [P13](#p13), [P21](#p21).
- Pinned v5 source: [src/Directory.Build.props](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Directory.Build.props#L11).
- Bookshelf counterpart/integration point: [src/Directory.Build.props](../src/Directory.Build.props#L13).

**Bookshelf status:** partial: B `src/Directory.Build.props:13` declares linux-x64/linux-musl-x64/linux-musl-arm64; CI builds Docker amd64/arm64; scripts/build-mediainfo.sh explicitly limits bundled MediaInfo to Linux. B macOS default RID remains osx-x64 near the end of Directory.Build.props despite detecting arm64; packaging scripts contain other historic targets. S5 `Directory.Build.props:11` declares Windows x64/x86, macOS x64/arm64 and several Linux RIDs and chooses `osx-$(Architecture)` at line 221; `.github/workflows/build_v5.yml:48` builds a broad matrix, with packaging in `.github/actions/build/action.yml:101`.

**Action/dependencies:** P01/09/13/21; keep current supported Linux artifacts reliable first. Fix architecture detection/output references if desktop support is chosen. Do not copy FreeBSD SDK-editing workarounds (`S5 .github/actions/build/action.yml:50`) or advertise macOS/Windows merely because a RID can compile. Maintain checksums/licenses for all bundled native dependencies.

**Book/audio caution:** B has current MediaInfo 26.10 source/checksum/license packaging (`scripts/mediainfo.lock:3`, Dockerfile:47); preserve it. Porting Sonarr's ffprobe packaging does not automatically replace B's MediaInfo/TagLib feature and format requirements.

**Acceptance:** documented platform rows match actual signed/published/tested artifacts; every supported package starts, reads formats, analyzes audio, imports/renames and updates on that architecture; native libraries, executable bits and licenses are present; unsupported packaging paths fail clearly rather than shipping incomplete artifacts.

### P23

**Add opt-in request/SQL profiling only after core correctness**

- Priority / effort: **P3 / M**.
- Current Bookshelf state: missing MiniProfiler; existing SQL/application logs are available.
- Approach: study/adapt.
- Prerequisites: [P01](#p01), [P09](#p09), [P03](#p03).
- Pinned v5 source: [src/NzbDrone.Host/Startup.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Host/Startup.cs#L242).
- Bookshelf counterpart/integration point: [src/NzbDrone.Host/Startup.cs](../src/NzbDrone.Host/Startup.cs#L36).

**Bookshelf status:** missing MiniProfiler; existing SQL/application logs are available. S5 `Startup.cs:242` configures MiniProfiler, `:360` enables middleware only with ProfilerEnabled and `:378` authorizes result POST with the UI policy; `Core/Datastore/ProfiledImplementations.cs:6` adds a profiled DB connection wrapper; `ConfigFileProvider.cs:344` defaults profiling false.

**Action/dependencies:** P01/09, reliable auth and DB abstractions first. Decide on request timing/SQL parameter retention, memory limits and private diagnostic access. Inspect the complete profiler route/security behavior; a single authorized POST mapping is not proof that every results endpoint is protected.

**Book/audio caution:** profiling can help expensive edition joins and refresh fan-out, but metadata and queries may contain user-specific library names/paths/provider parameters. It must not become part of ordinary import flow.

**Acceptance:** off by default with negligible disabled overhead; unauthorized access cannot read results; no raw secrets in traces; representative large-library refresh/import bottlenecks are measurable without correctness or cancellation regressions; profiling data expires predictably.

### P24

**Correct URL-base boundary handling on login redirects**

- Priority / effort: **P2 / S**.
- Current Bookshelf state: partial; already rejects non-local return URLs.
- Approach: port.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/Sonarr.Http/Authentication/AuthenticationController.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Http/Authentication/AuthenticationController.cs#L190).
- Bookshelf counterpart/integration point: [src/Readarr.Http/Authentication/AuthenticationController.cs](../src/Readarr.Http/Authentication/AuthenticationController.cs#L75).

**Bookshelf status:** partial; already rejects non-local return URLs. B `src/Readarr.Http/Authentication/AuthenticationController.cs:75` uses IsLocalUrl, then line 80 checks an unbounded case-sensitive StartsWith(UrlBase). S5 `AuthenticationController.cs:190` extracts GetRedirectUrl; it requires equality or `urlBase + "/"` with ordinal-ignore-case before treating a local return URL as already prefixed.

**Action/dependencies:** port a small helper and share it with any future OIDC flow. Keep current public redirect behavior except for the prefix-boundary defect; do not call this an open-redirect vulnerability, since B already applies IsLocalUrl.

**Book/audio caution:** users may run adjacent paths such as `/books` and `/bookshelf`; returning to the wrong sibling app after login is disruptive to browser integrations.

**Acceptance:** return URLs `/`, absent, malformed, external, exact base, base child, similarly prefixed sibling, case variants and encoded paths produce the intended local URL with one prefix; reverse proxy and base-path login/logout work.

**2026-11-18 implementation record:** P24 is **Verified** locally in
`9dda441fd`. Login now uses a redirect helper that retains ASP.NET's existing
`IsLocalUrl` rejection and compares the raw path using an ordinal, case-insensitive
exact-base or `base + "/"` boundary. A sibling such as `/bookshelf` no longer
counts as a child of `/books`. Query/fragment suffixes on the exact base are
recognized, and application-relative `~/...` destinations receive the configured
base once. Encoded path/query bytes are preserved without decoding/reencoding.
Existing logout destinations remain unchanged. This corrects local application
routing; the prior non-local URL rejection already existed.

All **41 focused source cases** pass, covering absent/empty/root, exact/base child,
sibling, case, query/fragment, encoded bytes, trailing slash, application-relative,
malformed-relative, external, protocol-relative, backslash and control-character
inputs, plus logout with and without a base. The initial packaged `accesspolicy`
proof additionally exercises fourteen real login destinations and base-path
logout. Its combined P24/P04/P25 result is **62 checks passed**, including input
preservation and cleanup. Final clean-package evidence remains a separate local
validation record; no live home or running server was changed.

### P25

**Isolate fork release channels and require matching source/artifact identity**

- Priority / effort: **P0 / L**.
- Current Bookshelf state: partial packaging/updater foundation with legacy product/channel configuration.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [.github/workflows/deploy.yml](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/.github/workflows/deploy.yml#L127).
- Bookshelf counterpart/integration point: [src/NzbDrone.Common/Cloud/ReadarrCloudRequestBuilder.cs](../src/NzbDrone.Common/Cloud/ReadarrCloudRequestBuilder.cs#L15).

**Bookshelf status:** partial packaging/updater foundation with legacy product/channel configuration. B `src/NzbDrone.Common/Cloud/ReadarrCloudRequestBuilder.cs:15` points Services to `https://readarr.servarr.com/v1/`; `UpdatePackageProvider.cs:24` uses those Services and `GetLatestUpdate:32` requests `/update/{branch}` with includeMajorVersion=true. B `ConfigFileProvider.cs:217` defaults branch to develop; `BuildInfo.cs:29` reports Readarr. CI `.github/workflows/build.yml:119`/`:128` still tags softcover/hardcover, and Dockerfile:26 and CI:169 embed legacy bookinfo metadata endpoints. README.md:3 describes the current audiobook/Audnexus application. S5 `src/NzbDrone.Common/Cloud/SonarrCloudRequestBuilder.cs:15` has its own service namespace; `UpdatePackageProvider.cs:35` selects version/OS/architecture/runtime/database; `.github/workflows/deploy.yml:127` records branch/version/commit and hashes and line 169 publishes to Sonarr's service. Its build workflow:214 gates deploy by repository, branch and required jobs.

**Action/dependencies:** adapt the product-owned release gate, manifest and source/artifact association pattern; never adopt Sonarr service endpoints. Define Bookshelf product ID, binary channels, update feed and supported container tags. Disable or fail closed for built-in updates until a fork-owned compatible feed exists; protect current configured identities/keys/history. Require a release manifest tying commit, version, runtime/RID, schema and checksums to corresponding GPL source, then reject mismatched product/channel/architecture before replacement. Audit legacy metadata environment options for actual current usage; their presence alone is not evidence runtime still calls those services. P01/P09/P21/P22 precede a modernized release, while isolation is a prerequisite for safely distributing any work.

**Book/audio caution:** a Sonarr or older Readarr binary cannot consume the native audiobook schema safely. Product branding can be changed independently from persistent filesystem paths/API compatibility; avoid renaming `/config`, DB/files or user identities as part of release-channel cleanup. Current softcover/hardcover tags need an explicit retention/migration decision tied to the audiobook product.

**Acceptance:** a dry release consumes only tested artifacts and emits matching commit/version/hash/source manifests; wrong product, branch, RID, runtime/schema or checksum is rejected; Docker updater behavior remains container-managed; a copied Bookshelf home never accepts a Sonarr/legacy Readarr update; CI cannot publish unsupported legacy metadata variants accidentally; release and rollback use disposable fixtures, with no live update endpoint called in tests.

**2026-11-18 local isolation record:** P25 is **In progress**. Production
safeguards and compiled provenance are implemented in `736ffe104`; this completes
a local prerequisite for release work, not the full release/publication gate.
The product identity is `bookshelf-audiobooks`. Existing executable names,
filesystem/database paths, account identities, API contracts and saved update
choices remain compatible.

`BookshelfUpdatePolicy` always denies in-app updates because no compatible
fork-owned feed is configured. Checks and update listings do not call the legacy
Readarr update service; manual installation commands fail with the explicit
unavailable reason. Startup leaves legacy branch/mechanism choices and an
`update_required` marker intact. The package provider, verification and installer
boundaries also deny access, and the standalone updater exits nonzero before
logging, argument parsing or constructing replacement services. Status/UI explain
that a verified Bookshelf release must be installed manually. No configuration
switch enables an unreviewed feed.

Builds embed product ID, source commit/tree/branch and dirty state in the backend;
the frontend carries matching `UI/source-provenance.json`. The optional
`releaseidentity` proof reads the actual hosted binary's values and compares the
frontend. Initial validation passes **8 checks**, including its unchanged-input
check. Source tests cover the denied HTTP/download/script/replacement boundaries;
the packaged access-policy proof checks status, empty update results, no-op
checks, rejected installation commands, retained settings/marker and the refused
standalone updater. It does not claim a network-capture audit.

Local candidate tooling requires a clean checkout, matching compiled backend/UI
identity and passing evidence for the same package fingerprint. It stages a
private package copy, corresponding Git source archive with GPL license, evidence
receipts and an unsigned manifest tying product/channel, version, source commit,
runtime/RID, schema and checksums together. Verification rejects mismatches or
changed bytes. The current local channel is `develop`, with Linux x64/.NET 10
only; the manifest explicitly sets `publishable=false` and
`builtInUpdatesEnabled=false`. This is a local attestation, not a trusted update
manifest or a signed release.

Initial source verification passes **4,985 cases**: Core **3,856**, Common **835**,
API **16**, Host **181**, authentication integration **54**, PostgreSQL migration
**23** and updater **20**, with the existing **97 skips** retained. Python
validation currently passes **131 tests**. The initial access-policy proof passes
**62 checks**, and identity passes **8**.

**Final clean-artifact acceptance:** `0015c349f` passes **301 delivery checks**
(including access policy, identity, a genuine .NET 6 upgrade and Docker) and
**111 authentication-policy checks** against a genuine pre-P16 P14 package.
Source suites pass **4,986 cases** after the configuration-preservation follow-up
(Common **836**, other counts above unchanged), with **97 existing skips**.
Python validation passes **131 tests**. Desktop/390px Chromium checks pass **20
UI/API assertions and seven preservation/cleanup guards**.

The actual local CLI stages and verifies one unsigned candidate at version
`10.0.0.36421`, with the matching GPL source archive and both passing reports.
**41 checks** prove rejection of failed/incomplete/foreign evidence, wrong
product/channel/commit/branch/runtime/version/schema, changed payload bytes/modes,
source archives and unexpected files. Every controlled mutation is restored; the
candidate verifies again and the canonical package remains unchanged. It is
bound to exact source commit `0015c349f`, remains `publishable=false`, and enables
no updater. See [the final record](local-validation.md#p24-p04-and-local-p25-verification-2026-11-18).

No GitHub workflow, publication, signing, feed service or container-tag migration
is implemented here. P25 remains open for those release-policy and enforcement
gates, the supported artifact/platform matrix and native ARM acceptance. Existing
P02/P13 ARM and broader P01/P09/P21/P22 gates remain open. This work does not
publish a release or change the running development service or production
appdata/media.

### P26

**Propagate repository write failures and transact general bulk updates**

- Priority / effort: **P0 / M**.
- Current Bookshelf state: import-specific transactions exist; ordinary UpdateFields catches and suppresses SQL failures.
- Approach: adapt the generic repository behavior; preserve import-specific guarantees.
- Prerequisites: [D01](#d01).
- Pinned v5 source: [src/NzbDrone.Core/Datastore/BasicRepository.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Datastore/BasicRepository.cs#L260).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Datastore/BasicRepository.cs](../src/NzbDrone.Core/Datastore/BasicRepository.cs#L400).

Bookshelf BasicRepository.UpdateFields attaches SQL context to an exception but does not rethrow in either single or list overload; general UpdateMany also has no explicit transaction. The new import-only transactional Update overload and ImportFiles repository already guard their own path, so retain them. Pinned Sonarr v5 uses exception-propagating execution and wraps UpdateMany/SetFields bulk work in a transaction. Port the failure and transaction semantics first; adopt retry policy only for narrowly classified transient failures and never replay a non-idempotent batch blindly. Sanitize SQL diagnostic context where models can contain secrets.

**Acceptance:** Injected single-row SQL failure reaches the caller and emits no success/model-updated event. Failure on the second bulk row rolls back the first. Zero/missing-row behavior is explicit, constraints fail without retry loops, and original import rollback/provenance tests still pass. API callers do not receive a successful save for a failed database write.

**2026-11-14 implementation record:** [BasicRepository](../src/NzbDrone.Core/Datastore/BasicRepository.cs)
now propagates ordinary update failures, validates each affected row and reports
missing rows explicitly. General UpdateMany and bulk SetFields now run within
explicit transactions and propagate failures. Empty batches retain
their existing successful no-op behavior. Failed saves cannot publish a success
event, and bulk updates keep their existing event behavior. The protected
import update retains its existing affected-row check and transaction. No
automatic retry policy was added.

[Repository write fixtures](../src/NzbDrone.Core.Test/Datastore/BasicRepositoryWriteFixture.cs)
cover failure propagation, missing rows, bulk rollback, committed event visibility
and empty batches. [API error fixtures](../src/NzbDrone.Api.Test/ErrorManagement/ReadarrErrorPipelineFixture.cs)
cover missing-model 404, SQLite constraint 409 and other SQLite-save 500 responses.
Existing [MediaFileRepository import transactions](../src/NzbDrone.Core/MediaFiles/MediaFileRepository.cs),
[staged-import regression cases](../src/NzbDrone.Core.Test/MediaFiles/ImportApprovedTracksFixture.cs)
and [recording-identity cases](../src/NzbDrone.Integration.Test/ApiTests/BookControllerRecordingIdentityFixture.cs)
remain preservation gates: recording/file identities, provenance and recoverable
staged replacements are not replaced by donor behavior. P26 adds no schema
migration; P02's additive migration 048 is recorded separately above.

Validation on the same uncommitted working tree recorded under P02 passed the
3,301-case offline core suite, 125 PostgreSQL cases and 81 focused API/auth
cases, with the existing skips recorded there. A packaged HTTP request that
updated one valid quality row followed by a missing row returned 404; a direct
database read proved that the first row rolled back. P26's regression gate is
**Verified** locally. No live development or production home, server or library
was changed. The P26/P02 batch was followed by
[P03](#p03), the proxy/auth boundary unit. Existing dependency advisories in
[P09](#p09) prevent deployment; no package upgrade or committed audit suppression
is part of this batch.

## Downloads, acquisition and filesystem operations

### D01

**Preserve Bookshelf's staged replacement and ownership boundary**

- Priority / effort: **P0 / S**.
- Current Bookshelf state: existing local advantage.
- Approach: skip wholesale port / study individual deltas.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs#L37).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs](../src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs#L53).

**P0 · existing local advantage · skip wholesale port / study individual deltas · no dependency · S (review gate).** S:`src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs:37` `UpgradeEpisodeFile` recycles/deletes old files at lines 55–74 before moving the new file at lines 78–85. B:`src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs:53` `UpgradeBookFile`, `CommitUpgrade:117`, `FinalizeUpgrade:187`, `AbortUpgrade:234`; B:`src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs:331` commits staged replacements. B:`src/NzbDrone.Core/MediaFiles/MediaFileDeletionService.cs:125` `Handle(AuthorDeletedEvent)` uses owned file IDs and handles foreign files. Keep these implementations when taking any Sonarr importer/scanner work. Acceptance: retain old library and source files on stage/copy/tag/database failure, preserve unrepresented audiobook parts, never delete another author's owned files, and emit history/notifications only after successful commit. Existing evidence: B:`src/NzbDrone.Core.Test/MediaFiles/UpgradeMediaFileServiceFixture.cs:78,150,198` and B:`src/NzbDrone.Core.Test/MediaFiles/ImportApprovedTracksFixture.cs:489,504`.

### D02

**Add machine-readable download and import rejection codes**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/DecisionEngine/DownloadRejectionReason.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/DecisionEngine/DownloadRejectionReason.cs#L3).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/DecisionEngine/Decision.cs](../src/NzbDrone.Core/DecisionEngine/Decision.cs#L3).

**P1 · partial · adapt · foundational for D06–08, D11 · L.** S:`src/NzbDrone.Core/DecisionEngine/DownloadRejectionReason.cs:3`, `DownloadSpecDecision.cs`, `DownloadRejection.cs`; S:`src/NzbDrone.Core/MediaFiles/EpisodeImport/ImportRejectionReason.cs:3`, `ImportSpecDecision.cs`. B:`src/NzbDrone.Core/DecisionEngine/Decision.cs:3` and `Rejection.cs:3` carry human strings and permanence; B:`src/NzbDrone.Core/MediaFiles/BookImport/ImportDecision.cs` uses that older decision model. Add stable book/edition/recording rejection codes while keeping readable text and temporary/permanent classification. Acceptance: RSS/search/manual-import/queue API can identify the same rejection without parsing English; localized messages do not alter behavior; unknown codes round-trip safely. Do not copy episode/season/anime rejection enum members without a Bookshelf meaning.

### D03

**Fix tag isolation when choosing download clients**

- Priority / effort: **P0 / S**.
- Current Bookshelf state: partial, concrete routing gap.
- Approach: port logic with author terminology.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Download/DownloadClientProvider.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/DownloadClientProvider.cs#L174).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/DownloadClientProvider.cs](../src/NzbDrone.Core/Download/DownloadClientProvider.cs#L40).

**P0 · partial, concrete routing gap · port logic with author terminology · no dependency · S.** S:`src/NzbDrone.Core/Download/DownloadClientProvider.cs:174` `GetFilteredDownloadClients` normalizes absent tags to an empty set at line 178, then always filters tagged clients. B:`src/NzbDrone.Core/Download/DownloadClientProvider.cs:40` `GetDownloadClient` filters tags only inside `if (tags is { Count: > 0 })`. Consequently an untagged author can select a client intended only for tagged authors. Preserve existing indexer overrides, blocked-client behavior, priority, and round robin. Acceptance: an untagged author never selects a tagged-only client; matching tags beat untagged defaults; no eligible client gives an explicit error; indexer overrides cannot bypass tag isolation accidentally.

**2026-11-18 implementation:** `f7ee205d8` normalizes missing author tags and
always applies tag eligibility before indexer overrides, blocked-client handling,
priority and round robin. Matching tags retain preference over untagged defaults;
null/empty author tags only permit untagged clients. When no client is eligible,
selection reports the existing explicit unavailable error. No-protocol-provider
behavior stays compatible. The focused fixture passes **36 cases**, including **24 new
regressions** for both protocols, overrides, blocked matching clients and eligible
round robin. Final published acquisition evidence is recorded with D04 below.

### D04

**Audit completion against distinct books and complete recording parts**

- Priority / effort: **P0 / M**.
- Current Bookshelf state: partial; source demonstrates a TV assumption that needs adaptation.
- Approach: adapt/test local logic.
- Prerequisites: [D01](#d01).
- Pinned v5 source: [src/NzbDrone.Core/Download/CompletedDownloadService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/CompletedDownloadService.cs#L216).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/CompletedDownloadService.cs](../src/NzbDrone.Core/Download/CompletedDownloadService.cs#L121).

**P0 · partial; source demonstrates a TV assumption that needs adaptation · adapt/test local logic · D01 · M.** S:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:216` `VerifyImport` counts imported episode identities and falls back to history. B:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:121` counts imported `ImportResult` objects (`Select(...Book).Count()` at 123–125), not distinct books or expected audiobook parts; B:`src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadAlreadyImported.cs:23` also treats a null remote-book mapping as imported whenever any history exists (33–35). These are audit targets, not proof every multipart import is currently broken. Define expected Bookshelf identity/part coverage from its new provenance and compare actual committed results to it. Acceptance: two parts of one book cannot satisfy a two-book download; partial multipart imports never trigger source/client cleanup; a restart with complete committed history is idempotent; unrelated or grab-only history is insufficient evidence of completion.

**2026-11-18 verified implementation:** `ef8beac21` and `3aeb8c56b` establish
one shared recording/file receipt for completion. Automatic and
manual import, restart restoration, event-driven client cleanup and periodic
cleanup must all establish the same scope. Tracked imports retain source copies
through staged/database publication. History receives additive per-file identity
and part metadata; completion writes its receipt synchronously before state,
source retirement or notifications. Saved legacy markers alone cannot authorize
removal. Explicit Copy intent survives later retry history and client removal
uses `deleteData=false` when preserving those sources. Manual Copy also saves a
state-neutral retention marker before any selected tracked job imports, preserving
that choice through an incomplete selection or a restart before completion.
Cleanup requires client status `Completed`, refreshes mapped paths, retries source
retirement, and preserves data when the client cannot move its files.

The [completion policy](download-completion.md) describes book/edition/file IDs,
discovered/declared part coverage, current inventory, mapped paths, configured
library aliases and conservative unknown/legacy handling. This makes no claim to
know publisher totals or files absent from local inventory; D07 and B03 remain
separate work. Source verification passes **5,205 cases** with **97 existing
skips**, followed by **29 focused cleanup cases**, including two additional
nonmovable-source regressions. Tooling passes **141 Python tests**.

Clean artifact source `3aeb8c56b` passes **433 delivery checks**, including **132
published acquisition checks**. Separate proofs pass **111 authentication checks**
and **43 actual candidate CLI checks**. The latter rejects missing acquisition evidence. Both
Linux x64 packages build; the actual musl Docker proof passes **53 checks**.
Evidence is recorded in [local validation](local-validation.md#d03-and-d04-source-verification-2026-11-18).
The owned probe uses physical synthetic files, production completion/history
services and SQLite-backed repository interfaces; its committed-import result
boundary is controlled, with real importer coverage in source tests. No actual
download client, existing home, production media or running server was used. P17
remains deferred, and wider platform/feed/signing gates are unchanged.

### D05

**Prevent custom-format downgrades during import and rename**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D01](#d01), [D02](#d02), [B01](#b01).
- Pinned v5 source: [src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/UpgradeSpecification.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/UpgradeSpecification.cs#L61).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MediaFiles/BookImport/Specifications/UpgradeSpecification.cs](../src/NzbDrone.Core/MediaFiles/BookImport/Specifications/UpgradeSpecification.cs#L27).

**P1 · partial · adapt · D01, ideally D02 · M.** S:`src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/UpgradeSpecification.cs:61` checks current/new custom-format scores and distinguishes a score lost after renaming (76–93); S:`src/NzbDrone.Core/MediaFiles/EpisodeImport/LocalEpisodeCustomFormatCalculationService.cs:26` calculates intended-name and original-name scores. B:`src/NzbDrone.Core/MediaFiles/BookImport/Specifications/UpgradeSpecification.cs:27` only checks quality/revision, despite injecting `_customFormatCalculationService`; B:`src/NzbDrone.Core/CustomFormats/CustomFormatCalculationService.cs:108` already calculates local-book matches. Extend this to book/edition/recording groups before staged commit, preserving scene-name and original release metadata. Acceptance: same-quality lower-score downloads cannot replace better recordings; renaming away a preferred token is explained; permitted manual overrides are explicit; multipart parts use a coherent recording-level comparison.

### D06

**Classify rejected extensions, including downloads with no importable files**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing specific capability.
- Approach: adapt.
- Prerequisites: [D02](#d02).
- Pinned v5 source: [src/NzbDrone.Core/MediaFiles/EpisodeImport/RejectedFileExtensionService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/RejectedFileExtensionService.cs#L24).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/CompletedDownloadService.cs](../src/NzbDrone.Core/Download/CompletedDownloadService.cs#L93).

**P1 · missing specific capability · adapt · D02 · M.** S:`src/NzbDrone.Core/MediaFiles/EpisodeImport/RejectedFileExtensionService.cs:24` `GetRejection`, S:`src/NzbDrone.Core/MediaFiles/FileExtensions.cs`, S:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:290` `ProcessRejectedDownloadClientFiles`. B:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:93` currently reports only “No files found are eligible for import” for an empty import result; B:`src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs` is the book extension authority. Implement a book-aware allow/reject policy and typed explanations. Acceptance: mixed-case extensions, sidecars, archives, valid ebook containers, `.exe`-only downloads, user rejected extensions, and valid audio plus incidental sidecars are covered. Do not conflate an audiobook `.cue`/`.m3u` sidecar with a malicious executable.

### D07

**Ask download clients for file inventory when the filesystem has no eligible files**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: missing interface/capability.
- Approach: port generic contract and selected adapters.
- Prerequisites: [D06](#d06).
- Pinned v5 source: [src/NzbDrone.Core/Download/DownloadClientBase.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/DownloadClientBase.cs#L102).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/IDownloadClient.cs](../src/NzbDrone.Core/Download/IDownloadClient.cs).

**P1 · missing interface/capability · port generic contract and selected adapters · D06 · M–L.** S:`src/NzbDrone.Core/Download/DownloadClientBase.cs:102` `GetFiles` has a safe empty default; S:`src/NzbDrone.Core/Download/DownloadClientFile.cs`; S:`src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrent.cs:394`; S:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:323` catches inventory failures. B:`src/NzbDrone.Core/Download/IDownloadClient.cs` and `DownloadClientBase.cs` have no equivalent. Acceptance: qBittorrent relative file paths can explain rejection before local mounts are readable; unsupported clients still work; inventory errors do not erase import diagnostics or change completion; remote paths never authorize filesystem deletion.

### D08

**Make automatic failure/blocklist/redownload actions explicit per indexer**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D02](#d02), [D06](#d06), [D07](#d07).
- Pinned v5 source: [src/NzbDrone.Core/Download/RejectedImportService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/RejectedImportService.cs#L25).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/FailedDownloadService.cs](../src/NzbDrone.Core/Download/FailedDownloadService.cs#L63).

**P2 · partial · adapt · D02, D06, D07 · M.** S:`src/NzbDrone.Core/Download/RejectedImportService.cs:25` maps typed dangerous/executable/user-extension rejections to the indexer's `FailDownloads` settings; S:`src/NzbDrone.Core/Indexers/FailDownloads.cs` and `CachedIndexerSettingsProvider.cs`. B:`src/NzbDrone.Core/Download/FailedDownloadService.cs:63` handles encrypted/client-failed downloads, but lacks rejection-directed policy. Add defaults conservatively and expose exactly whether a case warns, marks failed, blocklists, searches again, or removes client data. Acceptance: ordinary filename/matching failures do not enter destructive retry loops; unknown/deleted indexers warn; explicit opt-outs/false values survive upgrades; existing blocklist and seed rules remain enforced.

### D09

**Fail over among eligible download clients**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt rather than copy catch-all behavior.
- Prerequisites: [D03](#d03).
- Pinned v5 source: [src/NzbDrone.Core/Download/DownloadClientProvider.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/DownloadClientProvider.cs#L76).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/DownloadService.cs](../src/NzbDrone.Core/Download/DownloadService.cs#L53).

**P1 · partial · adapt rather than copy catch-all behavior · D03 · M.** S:`src/NzbDrone.Core/Download/DownloadClientProvider.cs:76` `GetDownloadClients` returns ordered priority/round-robin candidates; `ReportSuccessfulDownloadClient:118`; S:`src/NzbDrone.Core/Download/DownloadService.cs:55` tries candidates at lines 83–118. B:`src/NzbDrone.Core/Download/DownloadService.cs:53` chooses one client, with priorities already supported by its provider. Port selection/ordering and classify retryable errors narrowly: Sonarr's broad exception fallback is not sufficient proof an add never succeeded. Acceptance: first unavailable client falls back; explicit user-selected client and indexer-mandated routing retain intent; release-unavailable/blocked/duplicate responses stop; ambiguous add-timeout reconciles client state before a second add; one successful grab/history event is recorded.

### D10

**Remove disappeared tracked items and refresh client configuration promptly**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: port/adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs#L236).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs](../src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs#L228).

**P1 · partial · port/adapt · no dependency · S–M.** S:`src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs:236` `StopTrackingMissing` evicts cache entries for one successfully polled client; S:`src/NzbDrone.Core/Download/TrackedDownloads/DownloadMonitoringService.cs:100` calls it only after a successful poll; `Handle(ProviderUpdatedEvent):188` and `Handle(ProviderDeletedEvent):193` debounce refreshes. B:`src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs:228` has no missing-item method; B:`src/NzbDrone.Core/Download/TrackedDownloads/DownloadMonitoringService.cs:95` records success without reconciliation. Acceptance: a removed client download disappears from queue/cache after success, an offline/partial-failure client is not mistaken for an empty client, a changed category/path gets re-evaluated, and entries from other clients remain intact. Verify same download IDs across clients before preserving the current cache-key design.

### D11

**Separate blocked import state from terminal failure**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D02](#d02), [D04](#d04).
- Pinned v5 source: [src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs#L49).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/CompletedDownloadService.cs](../src/NzbDrone.Core/Download/CompletedDownloadService.cs#L109).

**P1 · partial · adapt · D02, D04 · M.** S:`src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs:49` contains `ImportBlocked`; S:`src/NzbDrone.Core/Download/TrackedDownloads/DownloadMonitoringService.cs:129`, S:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:81`, and S:`src/NzbDrone.Core/Download/FailedDownloadService.cs:88` recheck downloading/blocked items. B:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:109` sets `ImportFailed`, while B:`src/NzbDrone.Core/Download/FailedDownloadService.cs:66` only checks `Downloading`. Keep retryable permission/path/matching blocks visible and reevaluate after relevant changes, with bounded work and idempotent commit. Acceptance: fixing a mapping or permission allows recovery; a client-side failure after an import block is detected; invalid files do not repeatedly transfer/tag; a failed staged import remains recoverable.

### D12

**Notify once when manual intervention is required**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D11](#d11).
- Pinned v5 source: [src/NzbDrone.Core/Download/CompletedDownloadService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/CompletedDownloadService.cs#L339).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/CompletedDownloadService.cs](../src/NzbDrone.Core/Download/CompletedDownloadService.cs#L116).

**P1 · partial · adapt · D11 · M.** S:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:339` `SetStateToImportBlocked` publishes `ManualInteractionRequiredEvent` and tracks `HasNotifiedManualInteractionRequired`; S:`src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs:118` preserves the flag across refresh; S:`src/NzbDrone.Core/Notifications/NotificationService.cs:364` builds a rich message; S:`src/NzbDrone.Core/Notifications/Webhook/WebhookManualInteractionPayload.cs`. B:`src/NzbDrone.Core/Download/CompletedDownloadService.cs:116` already emits `BookImportIncompleteEvent`, and Bookshelf has import-failure notifications, so this is an upgrade in semantics/payload, not “add notifications.” Acceptance: one actionable alert per blocked download, with errors/client/source and correct book identity; no alert spam every poll; recovery/reblocking behavior is defined; provider tag filters and notification failures remain isolated.

### D13

**Add minimum custom-format score improvement for upgrades**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs#L21).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs](../src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs#L19).

**P1 · partial · adapt · D02 optional, D05 complementary · M.** S:`src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs:21` `MinUpgradeFormatScore`; S:`src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs:104` rejects increments below the configured delta and returns a distinct reason. B:`src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs:19` already has minimum and cutoff scores, and B:`src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs:65` accepts any higher custom-format score. Add one profile value across migration/API/UI/export and use it consistently for RSS/history/queue/on-disk comparison. Acceptance: at delta 10, 100→109 rejects and 100→110 accepts; quality/revision upgrades keep documented precedence; negative scores and cutoff behavior work; existing profiles preserve current behavior via a default of zero.

### D14

**Add language custom formats using audiobook evidence**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing specification, partial language model.
- Approach: adapt.
- Prerequisites: [B01](#b01).
- Pinned v5 source: [src/NzbDrone.Core/CustomFormats/Specifications/LanguageSpecification.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/CustomFormats/Specifications/LanguageSpecification.cs#L23).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/CustomFormats/CustomFormatInput.cs](../src/NzbDrone.Core/CustomFormats/CustomFormatInput.cs#L6).

**P1 · missing specification, partial language model · adapt · reliable edition/recording language provenance · M.** S:`src/NzbDrone.Core/CustomFormats/Specifications/LanguageSpecification.cs:23` validates language IDs and supports negation/“except”; S:`src/NzbDrone.Core/CustomFormats/CustomFormatInput.cs` includes languages. B:`src/NzbDrone.Core/CustomFormats/CustomFormatInput.cs:6` has commented-out language constructors; no language specification exists in its specifications directory. Populate language from the best available release/edition/audio evidence and preserve Unknown. Acceptance: English-only, bilingual, unknown, negated, and original-language cases behave deterministically; “original language” means edition/work policy chosen for books rather than Sonarr's series field; existing regex/size/indexer-flag formats retain behavior. Source/Resolution/ReleaseType TV specifications are not audio formats.

### D15

**Implement meaningful audiobook size policy, including per-profile limits**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: quality/duration-based size enforcement is disabled; the global indexer maximum already exists.
- Approach: adapt mathematical pattern only.
- Prerequisites: [B01](#b01).
- Pinned v5 source: [src/NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs#L23).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs](../src/NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs#L22).

**P1 · quality/duration-based enforcement disabled; global cap already enforced · adapt mathematical pattern only · trusted duration model · L.** B:`src/NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs:22` logs “size restriction not implemented” and always accepts; its old code is commented out. S:`src/NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs:23` scales size by runtime; S:`src/NzbDrone.Core/Profiles/Qualities/QualityProfileQualityItem.cs:19` has per-profile min/max/preferred limits. Build audio size/bitrate bounds from reliable recording duration and format, with separate ebook rules and explicit unknown-duration behavior. Preserve the existing global indexer maximum enforced by B:`src/NzbDrone.Core/DecisionEngine/Specifications/MaximumSizeSpecification.cs:23` as a separate cap. Acceptance: a 30-hour audiobook is not rejected using a 45-minute TV fallback; multipart duration sums once; MAM structured duration and local probe provenance are respected; unknown duration/size is handled explicitly; null/unlimited/zero and existing profiles preserve intent. Never port season/special/pilot runtime fallbacks.

### D16

**Rank near preferred audiobook size instead of always favoring the largest**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D15](#d15).
- Pinned v5 source: [src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs#L182).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs](../src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs#L167).

**P2 · partial · adapt · D15 · M.** S:`src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs:182` `CompareSize` favors distance from preferred size × runtime; B:`src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs:167` ranks size in fixed 200 MB buckets, effectively preferring larger releases. Choose audio-appropriate bitrate/size tolerances and apply size only after quality/custom format/protocol priorities. Acceptance: a preferred bitrate can favor a sensible encoding over padded/lossless oversize, long and short recordings compare correctly, no-duration releases use a documented fallback, and deterministic tie-breaking is retained. Sonarr's 200 MB rounding is far too coarse for many ebooks and short audio releases.

### D17

**Check library free space before grabbing**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing pre-grab stage, import check exists.
- Approach: adapt.
- Prerequisites: [D01](#d01).
- Pinned v5 source: [src/NzbDrone.Core/DecisionEngine/Specifications/FreeSpaceSpecification.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/DecisionEngine/Specifications/FreeSpaceSpecification.cs#L32).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs](../src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs#L25).

**P1 · missing pre-grab stage, import check exists · adapt · D01 · M.** S:`src/NzbDrone.Core/DecisionEngine/Specifications/FreeSpaceSpecification.cs:32` estimates post-import remaining space, caches five seconds, and honors an opt-out. B:`src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs:25` checks only at import. Add an advisory/rejection policy before grab, but budget peak space for Bookshelf's staged replacement instead of subtracting old files early. Acceptance: root path resolution, missing/inaccessible mounts, unknown sizes, minimum reserve, batch grabs, copy/hardlink/move differences, and temporary staged duplicates are covered; this never replaces the final import-time check. Avoid claiming cache-based checks reserve disk space across concurrent grabs.

### D18

**Represent one queued download with multiple books**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: partial.
- Approach: adapt with explicit API compatibility decision.
- Prerequisites: [D04](#d04), [P20](#p20).
- Pinned v5 source: [src/NzbDrone.Core/Queue/QueueService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Queue/QueueService.cs#L58).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Queue/QueueService.cs](../src/NzbDrone.Core/Queue/QueueService.cs#L63).

**P2 · partial · adapt with explicit API compatibility decision · D04, API/UI work · L.** S:`src/NzbDrone.Core/Queue/QueueService.cs:58` `MapQueueItem` receives `List<Episode>` and line 85 hashes only client/download; B:`src/NzbDrone.Core/Queue/QueueService.cs:63` maps one Book and line 96 includes book ID. A download-level row with `Books[]`/recording coverage removes duplicate actions and supports multi-book/audiobook bundles. Acceptance: progress/size counts once per download, blocklist/remove/retry operate once, book details remain navigable, polling and selection persist across changing metadata, and old API consumers are handled deliberately. S:`src/NzbDrone.Core/Queue/ObsoleteQueueService.cs:21` is compatibility code for the former per-episode model, not stale queue cleanup.

### D19

**Refresh pending decisions when policy or metadata changes**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: Existing cutoff/tag/configuration events; D13-D16 add integration cases when those features land.
- Pinned v5 source: [src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs#L43).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs](../src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs#L466).

**P2 · partial · adapt · D13–16 when those land · M.** S:`src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs:43` handles series edits/updates, quality-profile updates, configuration saves, and startup in addition to grab/RSS events; its in-memory pending cache is rebuilt by `UpdatePendingReleases`. B:`src/NzbDrone.Core/Download/Pending/PendingReleaseService.cs:466` onward handles author deletion, grabs and RSS completion only; it already deduplicates pending releases and calculates delay. Add targeted invalidation/augmentation for author tags, quality/custom-format/delay changes and book identity refreshes. Acceptance: editing a cutoff or routing tag updates pending eligibility without a restart/new feed item; duplicates remain deduplicated; no stale metadata resurrects deleted books; queued RSS delays do not reset unexpectedly.

### D20

**Sort library/history quality according to each profile**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: missing persisted rank mechanism.
- Approach: adapt if quality sorting is wanted.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Profiles/Qualities/QualityProfileRankService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Profiles/Qualities/QualityProfileRankService.cs#L47).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Profiles/Qualities/QualityProfileService.cs](../src/NzbDrone.Core/Profiles/Qualities/QualityProfileService.cs).

**P2 · missing persisted rank mechanism · adapt if quality sorting is wanted · profile migration/query work · M.** S:`src/NzbDrone.Core/Profiles/Qualities/QualityProfileRankService.cs:47` computes normalized equal ranks for grouped qualities; repository migration `233_quality_profile_quality_ranks.cs`; S:`src/NzbDrone.Core/History/HistoryRepository.cs:167` and `Blocklisting/BlocklistRepository.cs:71` join ranks for sorting. Bookshelf has profile ordering and `QualityModelComparer`, but no `QualityProfileRankService` or rank table. This is database-side sorting correctness, not a replacement for custom-format scores or a proven acquisition-speed optimization. Acceptance: reordered profiles/group equality affect paginated sort consistently, unknown quality sorts predictably, profile save/delete invalidates ranks, and migration preserves IDs/settings.

### D21

**Backport narrow RSS parser fixes, retain existing feed machinery**

- Priority / effort: **P1 / S**.
- Current Bookshelf state: partial.
- Approach: port narrow parser delta; skip wholesale RSS rewrite.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Indexers/Newznab/NewznabRssParser.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Indexers/Newznab/NewznabRssParser.cs#L110).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Indexers/Newznab/NewznabRssParser.cs](../src/NzbDrone.Core/Indexers/Newznab/NewznabRssParser.cs#L107).

**P1 · partial · port narrow parser delta; skip wholesale RSS rewrite · no dependency · S.** S:`src/NzbDrone.Core/Indexers/Newznab/NewznabRssParser.cs:110` splits comma-separated language values with trimming; B:`src/NzbDrone.Core/Indexers/Newznab/NewznabRssParser.cs:107` maps each whole language attribute, so `English, French` is not recognized as two values. S also sets indexer flags at line 95; inspect book-provider flags individually before extending B's parser. `FetchAndParseRssService.cs` was text-identical between the inspected checkouts, and generic `HttpIndexerBase` acquisition differences were chiefly localization/TV criteria. Acceptance: repeated language elements, comma values, case/whitespace, malformed attributes and existing audiobook/ebook author/book fields all work; rate-limit/backoff/pagination regressions are avoided.

### D22

**Persist successful import-list membership snapshots**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: missing snapshot layer; lists/exclusions already exist.
- Approach: adapt.
- Prerequisites: [B01](#b01).
- Pinned v5 source: [src/NzbDrone.Core/ImportLists/ImportListItems/ImportListItemService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/ImportLists/ImportListItems/ImportListItemService.cs#L26).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/ImportLists/FetchAndParseImportListService.cs](../src/NzbDrone.Core/ImportLists/FetchAndParseImportListService.cs#L30).

**P1 · missing snapshot layer; lists/exclusions already exist · adapt · book/work/edition identity policy · L.** S:`src/NzbDrone.Core/ImportLists/ImportListItems/ImportListItemService.cs:26` reconciles list membership; S:`src/NzbDrone.Core/ImportLists/FetchAndParseImportListService.cs:76` updates snapshots/status only when `AnyFailure` is false. B:`src/NzbDrone.Core/ImportLists/FetchAndParseImportListService.cs:30` fetches results, and B:`src/NzbDrone.Core/ImportLists/ImportListSyncService.cs:90` processes/adds them, but lacks the `ImportListItems` service/repository. Use edition/work/author identifiers and Bookshelf's metadata source guards, never TVDB/IMDb integer mappings. Acceptance: a partial/network-failed fetch retains previous membership; legitimate empty success clears that list only; the same work across multiple lists remains represented independently; deletions/exclusions and provider deletion update membership safely.

### D23

**Add additive and synchronized tags for existing list entries**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D22](#d22).
- Pinned v5 source: [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/ImportLists/ImportListSyncService.cs#L288).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/ImportLists/ImportListDefinition.cs](../src/NzbDrone.Core/ImportLists/ImportListDefinition.cs#L11).

**P2 · partial · adapt · D22 · M.** S:`src/NzbDrone.Core/ImportLists/ImportListSyncService.cs:288` accumulates tags across lists for existing series; `RemoveTagsFromSeriesNotOnLists:338` removes synchronized tags only after relevant lists have successfully synced; `TagExistingType.cs`. B:`src/NzbDrone.Core/ImportLists/ImportListDefinition.cs:11` supports monitoring existing items and inherits provider tags, but its sync service has no corresponding `TagExisting` policy. Add separate Add/Sync/None choices, distinguishing manual tags from list-owned tags. Acceptance: shared tags across multiple lists remain while any successful owning list contains the book/author; an offline list never strips tags; manual tags survive; duplicate items aggregate tags without duplicate additions.

### D24

**Offer non-destructive cleanup when books leave all lists**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: missing policy.
- Approach: adapt only after membership reliability.
- Prerequisites: [D22](#d22), [D23](#d23).
- Pinned v5 source: [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/ImportLists/ImportListSyncService.cs#L447).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](../src/NzbDrone.Core/ImportLists/ImportListSyncService.cs#L412).

**P2 · missing policy · adapt only after membership reliability · D22, D23 · M.** S:`src/NzbDrone.Core/ImportLists/ImportListSyncService.cs:447` gates cleaning on all-list success and pending cleanup; `CleanLibrary:460` has log-only, keep-and-unmonitor, keep-and-tag modes. B:`src/NzbDrone.Core/ImportLists/ImportListSyncService.cs:412` executes sync/add behavior without equivalent cleanup. Start disabled with log-only preview; choose book versus author scope explicitly, retaining local recordings/unlisted/manual additions. Acceptance: no action on failed/incomplete refreshes, no file deletion, overlapping lists and excluded items are respected, preview names exactly affected entities, and user-owned monitoring changes are not silently overwritten.

### D25

**Add a documented generic JSON import-list endpoint**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: missing provider, existing list framework.
- Approach: adapt.
- Prerequisites: [D22](#d22).
- Pinned v5 source: [src/NzbDrone.Core/ImportLists/Custom/CustomImport.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/ImportLists/Custom/CustomImport.cs#L33).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](../src/NzbDrone.Core/ImportLists/ImportListSyncService.cs).

**P2 · missing provider, existing list framework · adapt · D22 desirable, authoritative book identity schema · M.** S:`src/NzbDrone.Core/ImportLists/Custom/CustomImport.cs:33` fetches a custom endpoint, S:`src/NzbDrone.Core/ImportLists/Custom/CustomImportProxy.cs:36,67` performs HTTP/JSON requests, and S:`src/NzbDrone.Core/ImportLists/Custom/CustomAPIResource.cs:3` defines TV IDs. Reuse the provider/validation pattern, define a versioned author/work/edition/recording schema suitable for Bookshelf, and provide user-managed list examples. Acceptance: authenticated/private endpoints do not leak secrets in logs, unsupported schema versions and missing identifiers fail clearly, partial errors do not replace snapshots, paging/limits are explicit, and excluded books stay excluded. Do not ship the Sonarr TV payload as a Bookshelf contract.

### D26

**Enrich webhooks with committed completion and score provenance**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [D01](#d01), [D04](#d04), [D05](#d05), [D12](#d12).
- Pinned v5 source: [src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs#L48).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs](../src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs#L39).

**P2 · partial · adapt · D01, D04, D05, D12 · M.** S:`src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs:48,72` includes custom-format info; `BuildOnImportCompletePayload:87`; S:`src/NzbDrone.Core/Notifications/Webhook/WebhookImportCompletePayload.cs:5` contains full download files/source/destination; S:`src/NzbDrone.Core/Notifications/Webhook/WebhookCustomFormatInfo.cs:7`. B:`src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs:39` already sends import events with books/files/deleted files; B:`src/NzbDrone.Core/Notifications/Webhook/WebhookBookFile.cs:24` lacks audiobook recording/part/media-information and CF fields. Add versioned fields and a completion event after the staged batch commits. Acceptance: an incomplete/rolled-back multipart import cannot emit a “complete” webhook; consumers can identify work/edition/recording/parts, before/after scores, release source, and client once; retain existing event compatibility and protect credentials.

### D27

**Send health-restored notifications**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Notifications/NotificationService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Notifications/NotificationService.cs#L512).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Notifications/NotificationService.cs](../src/NzbDrone.Core/Notifications/NotificationService.cs#L320).

**P2 · partial · adapt · health event support · S–M.** S:`src/NzbDrone.Core/Notifications/NotificationService.cs:512` handles `HealthCheckRestoredEvent`; S:`src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs:172` emits `HealthRestored`. B:`src/NzbDrone.Core/Notifications/NotificationService.cs:320` handles failures, and B:`src/NzbDrone.Core/Notifications/NotificationDefinition.cs:16` has `OnHealthIssue`, without a restored trigger. Extend the event pipeline, provider capability/schema and UI toggle. Acceptance: recovery sends once for a previously reported condition, warnings obey inclusion settings, repeated healthy checks do not spam, tags/provider failures remain isolated, and enabling restored notifications does not enable failure notifications implicitly.

### D28

**Coalesce media-server refreshes for multipart imports**

- Priority / effort: **P3 / M**.
- Current Bookshelf state: missing shared queue abstraction; refresh providers already exist.
- Approach: adapt if providers support audiobook libraries.
- Prerequisites: [D01](#d01), [D26](#d26).
- Pinned v5 source: [src/NzbDrone.Core/Notifications/MediaServerUpdateQueue.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Notifications/MediaServerUpdateQueue.cs#L26).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs](../src/NzbDrone.Core/Notifications/Plex/Server/PlexServerService.cs).

**P3 · missing shared queue abstraction; refresh providers already exist · adapt if providers support audiobook libraries · D01, D26 · M.** S:`src/NzbDrone.Core/Notifications/MediaServerUpdateQueue.cs:26` merges updates by item/server; `ProcessQueue:42` serializes refresh work while accepting new entries. B has Plex server integration under `src/NzbDrone.Core/Notifications/Plex/Server/`, without this shared queue class. Batch refreshes after committed book/recording completion to avoid one request per part. Acceptance: a multipart audiobook yields one useful refresh per library/server; concurrent downloads are not lost; an exception releases the refreshing flag and follows a defined retry policy; two configured servers are isolated. Do not copy TV library/episode keys or assume Plex music libraries model audiobooks correctly.

### D29

**Support explicit qBittorrent total versus inactive seed time**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: port/adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentSettings.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentSettings.cs#L89).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrent.cs](../src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrent.cs#L708).

**P2 · partial · port/adapt · no dependency · S–M.** S:`src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentSettings.cs:89` exposes `SeedTimeType`; S:`src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs:243` selects `seedingTimeLimit` or `inactiveSeedingTimeLimit` with API 2.9.2 gating. B:`src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrent.cs:708` already detects inactive-time stop conditions and the torrent/preferences models have inactive limits, but B:`src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs:229` sends only total seed time. Add the configuration and validation while preserving the existing total-time default. Acceptance: supported clients receive the correct parameter, older versions show a clear compatibility result, null/-1/-2/unlimited semantics stay intact, and existing seed-ratio/remove-completed safeguards work.

### D30

**Defer scripted import replacement; study an explicit extension boundary**

- Priority / effort: **P3 / L**.
- Current Bookshelf state: missing Sonarr-style transfer override.
- Approach: study/defer.
- Prerequisites: [D01](#d01), [D04](#d04).
- Pinned v5 source: [src/NzbDrone.Core/MediaFiles/ScriptImportDecider.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/ScriptImportDecider.cs#L23).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs](../src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs).

**P3 · missing Sonarr-style transfer override · study/defer · D01, D04 and explicit product need · L.** S:`src/NzbDrone.Core/MediaFiles/ScriptImportDecider.cs:23` `ImportScriptService`, `ProcessOutput:52`, `TryImport:113` supports script-controlled `MoveComplete`, `DeferMove`, `RenameRequested`, extra files, and rich environment variables. Bookshelf has notification/custom-script facilities but no equivalent importer override. This could help specialized audiobook post-processing, but the single returned media-file protocol does not model multipart recordings, and arbitrary script completion cannot replace staged transaction/provenance guarantees. Acceptance before implementation: defined multi-output manifest, confined destinations/ownership, bounded execution, safe rollback, probe/tag validation, seed-source preservation, and no history/notification commit before all output files are verified. Prefer a post-commit notification hook for tasks that do not need to control transfer.

## Frontend and interaction design

### U01

**Correct accessible names on icon actions**

- Priority / effort: **P1 / S**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: None beyond the shared preservation gates..
- Pinned v5 source: [frontend/src/Components/Link/IconButton.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/Link/IconButton.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/Link/IconButton.js](../frontend/src/Components/Link/IconButton.js).

**P1; small; partial; available in pinned v5; tactical adaptation.** S5 `Components/Link/IconButton.tsx:13-39` removes a shared misleading label, hides the decorative icon, and relies on caller attributes. B `Components/Link/IconButton.js:20-36` currently gives every shared icon action the default `aria-label="Table Options Button"`, even when the action is unrelated.

Port the caller-owned naming pattern and audit monitor, refresh, delete, download, file-browser and table-option actions. No React/router migration is required to correct the JS primitive. **Dependencies:** translations/caller inventory only. **Acceptance:** screen-reader names describe each action; no generic table-options name outside that action; decorative icons are hidden; keyboard activation and disabled behavior stay intact. Do not merely remove the default and leave unlabeled callers.

### U02

**Named, semantic dialogs**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U01](#u01).
- Pinned v5 source: [frontend/src/Components/Modal/Modal.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/Modal/Modal.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/Modal/Modal.js](../frontend/src/Components/Modal/Modal.js).

**P1; small/medium; partial; available in pinned v5; tactical adaptation.** S5 `Components/Modal/Modal.tsx:172-201` and `ModalHeader.tsx:16-26` join dialog `aria-labelledby` to a generated header id and add dialog/modal semantics. B `Components/Modal/Modal.js:184-212` already has portals, focus locking, nested-modal ordering, backdrop/escape handling and iOS scroll locking, but the content container has no dialog role/name.

Add shared title wiring while preserving the established modal behavior. **Dependencies:** U01 for close buttons; a generated unique-id mechanism can precede React 18. **Acceptance:** every open dialog has a unique accessible title, nested confirm dialogs are named, focus remains contained and returns appropriately, only the top modal closes on Escape, backdrop dragging does not close unexpectedly, scroll position survives close on phone. Include local-recording and destructive confirmation dialogs.

### U03

**Keyboard-operated, viewport-aware menus**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: port/adapt.
- Prerequisites: [U01](#u01).
- Pinned v5 source: [frontend/src/Components/Menu/Menu.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/Menu/Menu.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/Menu/Menu.js](../frontend/src/Components/Menu/Menu.js).

**P1; medium; partial; available in pinned v5; selective code port.** S5 `Components/Menu/Menu.tsx:84-90,127-229` uses Floating UI dismissal/placement, menu trigger relations, arrow/Home/End navigation, disabled-item exclusion, focus management, and cleanup. S5 `SelectedMenuItem.tsx:30-31` exposes radio selection semantics. B `Components/Menu/Menu.js` and child primitives provide popper-based menus without these aria/keyboard facilities.

Implement the accessible interaction contract independently or port the component set together; avoid copying only the positioning wrapper. **Dependencies:** U01, menu primitive typing; Floating UI only if selected. **Acceptance:** keyboard open, arrow wrap, Home/End, Enter, Escape, focus return, selected-state announcement and outside dismissal work in filters/sorts/toolbar overflow; the menu stays in the viewport at 200% zoom and on phone. No hidden/disabled item receives focus.

### U04

**Connect form labels, help and validation to inputs**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U02](#u02).
- Pinned v5 source: [frontend/src/Components/Form/FormInput.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/Form/FormInput.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/Form/FormInputGroup.js](../frontend/src/Components/Form/FormInputGroup.js).

**P1; medium; partial; available in pinned v5; design adaptation.** S5 `Components/Form/FormRow.tsx:61-87`, `FormLabel.tsx:26-35`, `FormInput.tsx:187-255`, `FormInputGroupContext.tsx:23-51`, `TextInput.tsx:164-170` connect unique ids, `htmlFor`, description ids and `aria-invalid`; check/select/autosuggest/textarea primitives share the context. B `FormGroup.js:22-35`, `FormInputGroup.js:122-160`, `TextInput.js:143-170` primarily share presentation and error styling.

Adapt those semantics to Bookshelf's controls first, retaining audiobook/edition/profile/device input types. **Dependencies:** U02 ids; do not require wholesale form replacement. **Acceptance:** clicking labels focuses the right input; duplicate forms/modal fields do not duplicate ids; screen readers announce relevant help/errors; advanced-field hiding and explicit false/zero values are preserved; provider and local-recording forms retain server validation. Check every composite select/tag/password control, not only TextInput.

### U05

**Announce asynchronous action results**

- Priority / effort: **P2 / S**.
- Current Bookshelf state: partial.
- Approach: port/adapt.
- Prerequisites: [U01](#u01).
- Pinned v5 source: [frontend/src/Components/Link/SpinnerErrorButton.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/Link/SpinnerErrorButton.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/Link/SpinnerErrorButton.js](../frontend/src/Components/Link/SpinnerErrorButton.js).

**P2; small; partial; available in pinned v5; selective port.** S5 `Components/Link/SpinnerErrorButton.tsx:148-198`, `ClipboardButton.tsx:60-81` and `Components/StatusIndicator.tsx:10-21` retain visible results and add status/alert regions with cleanup. B same-name `.js` components show result icons without these live-region semantics.

Apply to provider test, save, clipboard and similar asynchronous operations. **Dependencies:** U01; localized success/warning/error text. **Acceptance:** successful, warning and failed results each announce once; decorative icons do not repeat the message; completion timeout is cleaned on unmount; long-running operations remain understandable without color alone.

### U06

**Upgrade React independently to 18/createRoot**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing.
- Approach: port/adapt.
- Prerequisites: browser baseline, [B01](#b01).
- Pinned v5 source: [frontend/src/bootstrap.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/bootstrap.tsx).
- Bookshelf counterpart/integration point: [frontend/src/bootstrap.tsx](../frontend/src/bootstrap.tsx).

**P1; medium; missing; available in pinned v5; bounded platform port.** S5 `bootstrap.tsx:7-13` uses createRoot. B `bootstrap.tsx:9-16` uses `react-dom.render`, while runtime React 17 differs from installed React 18 type packages (`package.json`).

Align runtime and type versions and change the entry point without changing router, Redux or styling simultaneously. This can be a bounded adaptation of the entry point without replacing the rest of the app providers. **Dependencies:** baseline browser coverage; compatibility review of focus/measure/virtualization/DnD dependencies. **Acceptance:** library, settings, local-recording preview/save, modal stacks, drag actions, SignalR reconnect and route navigation work; no duplicate requests/listeners/subscriptions or relevant runtime warnings. Do not port S5 `index.ts:12-36`, which suppresses React defaultProps/findDOMNode warnings globally; resolve or explicitly track those instead.

### U07

**Strict TypeScript and explicit frontend type checking**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: typed API/domain models.
- Pinned v5 source: [frontend/tsconfig.json](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/tsconfig.json).
- Bookshelf counterpart/integration point: [frontend/tsconfig.json](../frontend/tsconfig.json).

**P1; medium/large staged; partial; available in pinned v5.** S5 `frontend/tsconfig.json:9-18` enables strict/noImplicitAny/noUnused/noImplicitReturns and bundler module resolution; `package.json:12-14` adds CSS type generation and standalone typecheck. B config allows unchecked JS and lacks strictness (`:2-18`); Webpack's checker is build-coupled (`frontend/build/webpack.config.js`, `ForkTsCheckerWebpackPlugin`).

Add a CI typecheck command, migrate shared models/API boundaries/new code in coherent slices, then raise strictness for those slices and ratchet the remainder. **Dependencies:** declared book/edition/recording/local-recording types; CSS declaration strategy. **Acceptance:** typecheck fails on invalid field names/nullable state/identity mixups and stale CSS names; conversions contain no broad `any`/unchecked `as` that erase the boundary; JS files remain explicitly tracked because `allowJs:true, checkJs:false` does not validate them. Preserve retained ebook fields rather than deleting them to satisfy new audiobook-centric types.

### U08

**Typed fetch transport with cancellation and structured errors**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U07](#u07), P20 for any intentional API contract change.
- Pinned v5 source: [frontend/src/Utilities/Fetch/fetchJson.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Utilities/Fetch/fetchJson.ts).
- Bookshelf counterpart/integration point: [frontend/src/Utilities/createAjaxRequest.js](../frontend/src/Utilities/createAjaxRequest.js).

**P1; medium; partial; v5 expansion; adaptation required.** S5 `Utilities/Fetch/fetchJson.ts:3-88`, `anySignal.ts`, `getQueryPath.ts:3-4` support typed errors, timeout plus caller abort, JSON requests and 204 responses. B `Utilities/createAjaxRequest.js:28-62` already returns an abort function, separates external URLs from authenticated relative calls, and marks intentional aborts. Do not lose these behaviors in a fetch migration.

Introduce a transport behind one read-only screen while preserving Bookshelf's initialization/API root and auth/CSRF contract. **Dependencies:** U07 typed errors; transport regression fixtures. **Acceptance:** urlBase/root deployment, auth expiry, 400 validation arrays, JSON/text/empty errors, network error, 204, timeout and unmount abort behave correctly; API key never goes to an external URL; stale requests cannot overwrite newer state. Adapt rather than copy S5's hardcoded `/api/v5` at `fetchJson.ts:37`; use a defined body check rather than `body ?` (`:58`) when false/zero/null can be valid; clear timeouts/listeners in failure paths as well as successful responses. TypeScript casts alone are not runtime response validation.

### U09

**Introduce Query for one server-owned state slice**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: missing.
- Approach: adapt.
- Prerequisites: [U06](#u06), [U08](#u08).
- Pinned v5 source: [frontend/src/Helpers/Hooks/useApiQuery.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Helpers/Hooks/useApiQuery.ts).
- Bookshelf counterpart/integration point: [frontend/src/Store/Actions/Creators/createFetchHandler.js](../frontend/src/Store/Actions/Creators/createFetchHandler.js).

**P1; medium; missing locally; available in pinned v5.** S5 `Helpers/Hooks/useApiQuery.ts:20-45` provides path/params keys, query options and AbortSignal; `useApiMutation.ts:33-64,70-108` handles mutations and validation failures. B `Store/Actions/Creators/createFetchHandler.js:5-42` uses shared Redux fetch flags and update callbacks.

Use a temporary Query-plus-Redux composition as a proposed migration design: start with read-only health/system data, then move one feature at a time. This incremental composition is a Bookshelf adaptation, rather than the final v5 App structure. Keep one authoritative owner per resource and bridge old consumers deliberately. **Dependencies:** U06/U08; cache-key convention including resource/query identities; explicit retry/stale/focus behavior. **Acceptance:** duplicate consumers share a request; unmounted queries abort; stale results cannot replace different-resource results; errors/loading are coherent; library identity keys use local ids/full marketplace-qualified foreign identity, never title or ASIN alone. Remove the converted Redux handlers only after all consumers are migrated.

### U10

**Keyed pagination that retains previous rows**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: port/adapt.
- Prerequisites: [U08](#u08), [U09](#u09).
- Pinned v5 source: [frontend/src/Helpers/Hooks/usePagedApiQuery.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Helpers/Hooks/usePagedApiQuery.ts).
- Bookshelf counterpart/integration point: [frontend/src/Store/Actions/Creators/createFetchServerSideCollectionHandler.js](../frontend/src/Store/Actions/Creators/createFetchServerSideCollectionHandler.js).

**P1; medium; partial; available in pinned v5; selective port after a Query trial.** S5 `Helpers/Hooks/usePagedApiQuery.ts:30-101` includes page, pageSize, sort, filters and extra params in the key, consumes the cancellation signal, and retains previous data while fetching. B `Store/Actions/Creators/createFetchServerSideCollectionHandler.js:8-63` has paging/sort/filter support but does not expose cancellation/request-identity handling like the single-item fetch helper; callbacks write the same section.

Apply to History, Queue and Wanted after transport/query conventions are proven. **Dependencies:** U08/U09; existing API paging/filter semantics. **Acceptance:** rapid page/sort/filter changes with intentionally reordered responses show only the latest selection; changing page size/filter resets page appropriately; last-page deletion clamps/refetches the page; stale retained rows cannot trigger operations against the wrong page; empty vs initial vs background loading are distinct. Preserve Bookshelf's navigation/current-page behavior rather than blindly copying S5 `usePage.ts:32-35`, which resets on POP.

### U11

**Deliberate SignalR-to-cache synchronization**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U09](#u09), [U10](#u10), event contract, [B01](#b01).
- Pinned v5 source: [frontend/src/Components/SignalRListener.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/SignalRListener.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/SignalRConnector.js](../frontend/src/Components/SignalRConnector.js).

**P1; medium/large; existing behavior, new cache owner missing; available in pinned v5.** S5 `Components/SignalRListener.tsx:69-82,89-106,240-257` reconnects/invalidate/patches Query data; the rest of the file updates/removes matching resource ids. B `Components/SignalRConnector.js:136-149,230-244,325+` dispatches Redux updates and repopulates the active page.

Each migrated resource needs an event-to-cache map and reconnect reconciliation; temporarily update legacy Redux only for still-legacy consumers. **Dependencies:** U09/U10 and backend event contract review. **Acceptance:** create/update/delete/import/command events update the affected list/detail/counts, alternate marketplace recordings stay separate, history remains attached to stable ids, disconnect/reconnect heals missed events, and unmount stops listeners. Do not copy S5's `version < 5` filter (`:104-106`), event names or episode/series DTO assumptions into Bookshelf. Never log credential-bearing resource bodies during debugging.

### U12

**Preserve preferences while splitting state into small stores**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U07](#u07), [U09](#u09), preference migration, [B01](#b01).
- Pinned v5 source: [frontend/src/Helpers/Hooks/useOptionsStore.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Helpers/Hooks/useOptionsStore.ts).
- Bookshelf counterpart/integration point: [frontend/src/Store/Middleware/createPersistState.js](../frontend/src/Store/Middleware/createPersistState.js).

**P1; medium; existing persistence, new architecture optional; available in pinned v5.** S5 `Helpers/createPersist.ts:4-19`, `Helpers/Hooks/useOptionsStore.ts:41-111,114-175`, `Activity/Queue/queueOptionsStore.ts:19-25,148-159` split local view choices from server data and merge current column definitions with persisted order/visibility. B `Store/Middleware/createPersistState.js:22-63,83-112` already does column merging and migrates a single `readarr` storage key; `Store/Migrators/migrate.js:6-10` includes audiobook-specific migrations.

Split only when a feature migrates; preserve custom columns/filter keys/defaults through an explicit one-time migration, with versioning, validation, corrupt-storage recovery and a rollback policy. **Dependencies:** U07/U09; list of every persisted field. **Acceptance:** default, custom and old stored layouts preserve visibility/order including ebook Pages; removed columns disappear, new columns gain defined defaults, translations still refresh, false/zero survive, corrupt/partial data cannot prevent startup. Do not directly adopt instance-name-derived keys (`createPersist.ts:9-12`): renaming or duplicate instance names must not silently lose/collide with preferences. S5 `mergeColumns` assumes persisted columns exist (`:133-139`); guard malformed/missing data.

### U13

**Local id-based selection with row subscriptions**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U07](#u07), stable local ids, [B01](#b01).
- Pinned v5 source: [frontend/src/App/Select/useSelectStore.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/App/Select/useSelectStore.ts).
- Bookshelf counterpart/integration point: [frontend/src/Helpers/Hooks/useSelectState.tsx](../frontend/src/Helpers/Hooks/useSelectState.tsx).

**P2; medium; partial; available in pinned v5.** S5 `App/Select/useSelectStore.ts:49-62,105-153,234-259,321-344` centralizes disabled-item/range selection, derives row subscriptions and reconciles items by id while keeping survivors selected. B `Helpers/Hooks/useSelectState.tsx` already implements a reducer, and `BookFile/Editor/BookFileEditorTableContent.js` has recent survivor-selection fixes covered by `frontend/test/libraryUiEdges.test.mjs:69-90`.

Adopt behavior/shared utilities first; Zustand is optional until profiling demonstrates rerender costs. **Dependencies:** U07, U21 if virtualization changes; stable local ids. **Acceptance:** select-all includes offscreen items within the documented scope, disabled rows stay disabled, sort/filter/row deletion/addition clears invalid anchors without losing surviving selections, and bulk operations never use grouped-volume ids or stale hidden rows. Keep the existing file deletion regression cases. Check S5's modern iterator-helper calls against supported browsers before copying them.

### U14

**Typed isolated pending settings state**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U04](#u04), [U08](#u08), [U09](#u09).
- Pinned v5 source: [frontend/src/Settings/useSettings.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Settings/useSettings.ts).
- Bookshelf counterpart/integration point: [frontend/src/Store/Actions/Creators/createSaveProviderHandler.js](../frontend/src/Store/Actions/Creators/createSaveProviderHandler.js).

**P1; medium; partial; available in pinned v5.** S5 `Helpers/Hooks/usePendingChangesStore.ts:23-81`, `Settings/useSettings.ts:47-98` isolates each form's pending changes, compares changes with fetched values, clears only after success and updates Query data after a save (`:23-44`). B `Store/Actions/Creators/createSaveProviderHandler.js:8-41,55-75` and global settings reducers/connector state already handle saves, warnings and pending changes.

Migrate one settings form while retaining schema-driven providers, test/force-save behavior and credentials semantics. **Dependencies:** U08/U09; U04 for form associations. **Acceptance:** independent forms do not leak drafts; editing back to the original value clears dirty state; save failure retains edits; successful save uses canonical returned values; omitted/unchanged secrets stay unchanged; explicit false/zero and ordered audio metadata survive. Do not copy S5's Set-based array equality (`usePendingChangesStore.ts:15-20`) for ordered narrators, chapters, naming tokens or other order-significant values.

### U15

**Unified route/close/unload protection for dirty forms**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: [U02](#u02), [U14](#u14), U16 for exact useBlocker port.
- Pinned v5 source: [frontend/src/Helpers/Hooks/useUnsavedChanges.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Helpers/Hooks/useUnsavedChanges.ts).
- Bookshelf counterpart/integration point: [frontend/src/Settings/SettingsToolbarConnector.js](../frontend/src/Settings/SettingsToolbarConnector.js).

**P1; medium; partial; available in pinned v5.** S5 `Helpers/Hooks/useUnsavedChanges.ts:10-49`, `useBeforeUnload.ts:3-17`, `useModalCloseGuard.ts:4-11`, `Components/Modal/Modal.tsx:73-82`, `Settings/useSettingsSave.ts:20-45` unify navigation blockers, close confirmation, unload and save-shortcut cleanup. B `Settings/SettingsToolbarConnector.js:35-108` already prompts for route changes but explicitly assumes every POP is back (`:94-98`); modal connectors often clear pending changes on close.

First fix/cover current form loss paths, then use the data-router blocker when router migration is justified. **Dependencies:** U02/U14; U16 only for the exact v5 useBlocker implementation. **Acceptance:** cancel retains draft and position; confirm continues the actual link/back/forward action including query/hash; Escape/backdrop/X use the same guard; reload warns only while dirty; successful save removes the warning; nested modal close does not discard a parent draft; keyboard shortcuts/listeners clean up. Never use an unload prompt to replace a server conflict check.

### U16

**Data-router navigation with a single basename**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: missing.
- Approach: adapt.
- Prerequisites: [U06](#u06), dirty-navigation browser fixtures.
- Pinned v5 source: [frontend/src/App/App.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/App/App.tsx).
- Bookshelf counterpart/integration point: [frontend/src/App/App.js](../frontend/src/App/App.js).

**P2; large; missing; available in pinned v5.** S5 `App/App.tsx:15-37` creates a data router with one urlBase basename and `Outlet`; `App/AppRoutes.tsx:166` supplies a not-found route. B `App/App.js:10-21`, `App/AppRoutes.js`, `Components/Router/Switch.js` use connected-react-router/history 4/router 5; B `Components/Link/Link.tsx:65-69` prepends urlBase itself.

Do this separately from Query or Vite. Define compatibility for all existing Bookshelf URLs, author/book/recording details, current-page/scroll restoration and dirty-form navigation. **Dependencies:** U06, U15 browser fixtures; audit links/redirects/withRouter/connectors. **Acceptance:** direct links/reload/back/forward/query/hash and both root/subpath hosting work without duplicate prefixes; browser bookmarks preserve targets; not-found and auth redirects work; retained ebook and local-recording routes remain available. Do not move Sonarr route names into Bookshelf.

### U17

**Keep Queue stable while a user acts, and coalesce refreshes**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: Existing selection/dialog/mutation behavior tests; U13 only if replacing shared selection.
- Pinned v5 source: [frontend/src/Activity/Queue/queueRefresh.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Activity/Queue/queueRefresh.ts).
- Bookshelf counterpart/integration point: [frontend/src/Activity/Queue/QueueConnector.js](../frontend/src/Activity/Queue/QueueConnector.js).

**P1; medium; partial; available in pinned v5.** S5 `Activity/Queue/queueRefresh.ts:4-29` throttles events to five seconds and remembers a blocked refresh; `Queue.tsx:248-274` blocks/unblocks refresh around interaction and forces a fresh query after the explicit refresh command completes. B `Components/SignalRConnector.js:233-244` and `Activity/Queue/QueueConnector.js:45-82,119-130` refresh via Redux/page populators; inspect selection/dialog conditions before defining the local block policy.

Adapt the coordinator now to existing Redux if useful; do not require Query merely to avoid row churn. **Dependencies:** existing selection/dialog/mutation behavior tests; U13 only if replacing shared selection. **Acceptance:** bursts of progress events produce bounded fetches; a selected row/modal target does not jump or become a different item mid-confirmation; one pending refresh runs on release; manual Refresh completes visibly and refreshes once; leaving the page releases the block; stale or removed targets produce a safe server error. Refresh coordination must not silently conceal status indefinitely.

### U18

**Rich Queue/History filter builders**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: backend paging/filter contract, saved-filter migration.
- Pinned v5 source: [frontend/src/Activity/Queue/useQueue.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Activity/Queue/useQueue.ts).
- Bookshelf counterpart/integration point: [frontend/src/Store/Actions/queueActions.js](../frontend/src/Store/Actions/queueActions.js).

**P2; medium; partial; available in pinned v5.** S5 `Activity/Queue/useQueue.ts:19-85` and `Activity/History/useHistory.ts:88-129` define rich custom filter builders. B Queue currently offers `includeUnknownAuthorItems` (`Store/Actions/queueActions.js:29-32,176-178`) without the richer builder; B History has fixed event presets (`historyActions.js:104-200`) rather than equivalent filterBuilderProps.

Extend the existing filter framework to author/book-or-recording ids, status, protocol, quality, language and event type only where the API implements them. Add marketplace/narrator/local provenance filters as separate Bookshelf designs with server support. **Dependencies:** backend paging/filter contract, validation, U12 migration if persisted. **Acceptance:** combined filters round-trip, counts/paging use the same predicate, unknown-author items remain controllable, invalid/deleted saved filters recover safely, and empty/filtered-empty states stay clear. Never copy Sonarr event numbers: B import failure=7, retagged=9, ignored=10, while S5 History ignored=7 (`useHistory.ts:75-83`). Preserve Bookshelf's existing custom author/release filters.

### U19

**Responsive release cards with variable-height virtualization**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: missing.
- Approach: adapt.
- Prerequisites: [U03](#u03), [U04](#u04), [U06](#u06), [U07](#u07), release DTO review.
- Pinned v5 source: [frontend/src/InteractiveSearch/InteractiveSearch.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/InteractiveSearch/InteractiveSearch.tsx).
- Bookshelf counterpart/integration point: [frontend/src/InteractiveSearch/InteractiveSearch.js](../frontend/src/InteractiveSearch/InteractiveSearch.js).

**P2; medium/large; missing release layout/virtualization; available in pinned v5.** S5 `InteractiveSearch/InteractiveSearch.tsx:83-174,214-235`, `InteractiveSearchResult.tsx`, `InteractiveSearchCard.tsx:90-165` switch wide rows to narrow cards based on measured container width and use VariableSizeList/ResizeObserver for measured result heights. B `InteractiveSearch/InteractiveSearch.js:140-162` renders every result row in a regular table.

Adapt the presentation around Bookshelf release and rejection data; emphasize narrator, language, duration/codec/bitrate, marketplace-qualified match and quality score where actually available. **Dependencies:** U06, U07, U03/U04, release DTO review; measured baseline before adding a virtualizer. **Acceptance:** desktop/modal/phone widths display title, match/rejection reasons, audio qualities and Download access without hidden hover-only information; dynamic details, text zoom and resize do not overlap/skip rows; result scrolling stays smooth with a large synthetic fixture and bounded DOM nodes; actions keep stable release identity. Do not port TV scene/episode/season content or collapse alternative recordings.

### U20

**Text filtering and intentional caching of interactive searches**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: partial.
- Approach: adapt.
- Prerequisites: U09 for caching, release freshness policy.
- Pinned v5 source: [frontend/src/InteractiveSearch/useReleases.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/InteractiveSearch/useReleases.ts).
- Bookshelf counterpart/integration point: [frontend/src/Store/Actions/releaseActions.js](../frontend/src/Store/Actions/releaseActions.js).

**P2; medium; partial; available in pinned v5.** S5 `InteractiveSearch/InteractiveSearchFilters.tsx:28-74` adds a text filter next to sort/custom filters; `useReleases.ts:405-455` keys searches by payload, caches 30 minutes, disables focus refetch/retry and filters titles locally. B has custom release filters and empty/hidden feedback already (`InteractiveSearch.js:124-170`, `Store/Actions/releaseActions.js:155-224`), so keep that as a baseline.

Add a simple local text filter with clear result counts; separately evaluate cache lifetime against audiobook indexer cost and freshness. **Dependencies:** U09 only for Query caching; U19 optional. **Acceptance:** typing does not issue fresh indexer searches; changing title/recording/marketplace search context cannot show another search's cached releases; reopening a search has an explicit freshness/refresh behavior; changing filter/sort does not re-search; grabbing updates the correct release result; filtered-empty text stays distinct from no provider results. Do not blindly adopt a 30-minute cache if local semantics require fresher data.

### U21

**Simplify existing library virtualization after measuring it**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: existing alternative.
- Approach: adapt if measured.
- Prerequisites: [U06](#u06), [U07](#u07), [U13](#u13), performance baseline.
- Pinned v5 source: [frontend/src/Components/Table/VirtualTable.tsx](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Components/Table/VirtualTable.tsx).
- Bookshelf counterpart/integration point: [frontend/src/Components/Table/VirtualTable.js](../frontend/src/Components/Table/VirtualTable.js).

**P2; medium; existing, alternative implementation; available in pinned v5.** S5 `Components/Table/VirtualTable.tsx:31-115` provides a typed FixedSizeList primitive with external scroll synchronization, cleanup and 20-row overscan. B `Components/Table/VirtualTable.js:31-170` already uses react-virtualized and handles item/order recomputation, jump targets and scroll restoration; author poster/overview views also exist.

Benchmark before changing libraries. If bundle size or frame times justify the work, port the typed interfaces/cleanup and fixed-size primitives for appropriate layouts while retaining variable-size handling where needed. **Dependencies:** U06/U07, U13; representative library fixtures. **Acceptance:** large author/book libraries retain selection, jump-to-letter, sorting, column changes, images, resize, back-navigation restoration and phone scrolling; DOM node count stays bounded; no listener leaks; the change meets measured frame/bundle targets. Preserve alternate-recording volume summaries and retained ebook layouts. S5's shared table alone does not include all B restoration behavior.

### U22

**Vite/HMR and a separately verified asset pipeline**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: missing.
- Approach: adapt.
- Prerequisites: CSS type strategy, Node/browser policy, package/CI integration, [P21](#p21).
- Pinned v5 source: [vite.config.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/vite.config.ts).
- Bookshelf counterpart/integration point: [frontend/build/webpack.config.js](../frontend/build/webpack.config.js).

**P2; medium/large; missing; available in pinned v5.** S5 `vite.config.ts:18-65` copies static assets, `:67-110` copies localization in dev, `:112-137` watches CSS typings, `:139-202` configures aliases/HMR/build URL handling; `package.json:5-16,125-129` supplies ESM scripts/toolchain. B `frontend/build/webpack.config.js` supplies content-hashed entry/chunks, runtime public path, workers, fonts, CSS extraction/checking and output to `_output/UI`; `frontend/src/index.ts:12-15` sets `__webpack_public_path__`.

Treat build tooling as its own change after runtime/query trials. Record the complete current asset contract and backend reverse-proxy/dev-server requirements before replacement. **Dependencies:** CSS module naming/generation, Node compatibility choice, build/package/CI integration. **Acceptance:** clean production package boots without a dev server; both root and nonempty urlBase load entry, lazy chunks, fonts/images/worker assets and login/oauth HTML; cache-busted assets work after update; localization and CSS types update during HMR; maps and profiling policy are deliberate; packaged output parity is checked on supported OSes. Do not copy Sonarr Vite port/environment names or `target: esnext` without a browser support decision. This is a developer-productivity migration, not a prerequisite for fixing accessibility or adding filters.

## Book and audiobook adaptations

### B01

**Make audiobook identity and upgrade invariants the porting contract**

- Priority / effort: **P0 / M**.
- Current Bookshelf state: existing identity/import regressions; incomplete reusable cross-layer upgrade harness.
- Approach: keep and extend Bookshelf tests; use Sonarr test organization as a pattern.
- Prerequisites: [D01](#d01).
- Pinned v5 source: [src/NzbDrone.Core.Test/TvTests/RefreshSeriesServiceFixture.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core.Test/TvTests/RefreshSeriesServiceFixture.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core.Test/MetadataSource/Audiobook/AudiobookRefreshFixture.cs](../src/NzbDrone.Core.Test/MetadataSource/Audiobook/AudiobookRefreshFixture.cs).

Bookshelf-native work, not a TV model port. Establish fixtures covering work versus recording versus file, full marketplace-plus-ASIN identities, local recording IDs, coauthors, canonical author mappings, publisher packs, US/UK recordings, multiple narrations, fractional positions, chapter parts and unknown dates. Existing tests already cover many individual cases. Add a reusable synthetic legacy-home snapshot and an authenticated API/browser harness checked into the repo so future modernization can reproduce those guarantees without private development-library data.

**Acceptance:** Every port preserves book/edition/file IDs, monitoring, acquisition history, credentials and explicit false/zero values in the applicable fixtures. A file cannot change recording because a title/author is similar; a local recording cannot become an Audible edition during refresh. Upgrades and rollback operate on disposable homes; no production media or private database enters the repository.

### B02

**Provide a supported author reconciliation and correction workflow**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: persistent author corrections exist; duplicate cleanup still requires an operator repair.
- Approach: Bookshelf-native design; study explicit identity lifecycle only.
- Prerequisites: [B01](#b01), [P26](#p26), [U02](#u02).
- Pinned v5 source: [src/NzbDrone.Core/Tv/RefreshSeriesService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Tv/RefreshSeriesService.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MetadataSource/Audiobook/AudiobookMetadataOverrideService.cs](../src/NzbDrone.Core/MetadataSource/Audiobook/AudiobookMetadataOverrideService.cs).

The Sonarr entity lifecycle is a reference for explicit operations, not proof that TV identities map to authors. Build a preview-and-confirm reconciliation operation for a reviewed name-only author and a verified canonical author. Reassign book ownership and author references atomically while preserving all recording ASINs and files; detect conflicting profile settings before applying a declared policy. Save the existing metadata override as part of the supported workflow. Do not merge two explicit, conflicting author IDs by name alone. TurtleMe-style name-only duplicates motivate the workflow, but identity evidence must be recorded per case.

**Acceptance:** Preview lists exact authors, recordings and reference changes. Existing histories, series links, profiles, tags, monitoring and file ownership are preserved according to the reviewed policy. A stale preview or injected DB error leaves both profiles intact. Restart and subsequent metadata lookups reuse the canonical author; similarly named distinct authors remain separate.

### B03

**Represent publisher-pack membership without collapsing recording identities**

- Priority / effort: **P2 / XL**.
- Current Bookshelf state: publisher packs have distinct recordings and range positions; no explicit recording-to-multiple-works membership.
- Approach: adapt multi-item file/grouping concepts; own book schema and UI.
- Prerequisites: [B01](#b01), [P12](#p12), [U07](#u07).
- Pinned v5 source: [src/NzbDrone.Core/MediaFiles/EpisodeFile.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeFile.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Books/Model/Edition.cs](../src/NzbDrone.Core/Books/Model/Edition.cs).

Sonarr's file-to-multiple-episodes relationship and multi-season handling are conceptual references. A publisher pack or omnibus can contain multiple book volumes while retaining one recording ASIN; the standalone recordings, regional editions and alternate casts remain independently owned records. Define a separate work/volume-membership relation and distinguish volume coverage from recording availability. Do not mechanically translate seasons/episodes or infer all memberships from an ambiguous range string.

**Acceptance:** A pack covering volumes 1-2 and two standalone recordings yields correct coverage without duplicate bytes or reassigned file IDs. Fractional volumes, side stories, uncertain ranges and manual membership corrections are supported. Recording monitoring, acquisition and deletion always identify exact recordings, never a display group.

### B04

**Add audio-specific custom-format rules from trustworthy media evidence**

- Priority / effort: **P1 / L**.
- Current Bookshelf state: custom formats and rich audio stream fields exist; codec/profile/channel conditions are missing.
- Approach: adapt specification extension points; implement audiobook rules.
- Prerequisites: [B01](#b01), [D05](#d05), [D13](#d13), [D14](#d14).
- Pinned v5 source: [src/NzbDrone.Core/CustomFormats/Specifications/LanguageSpecification.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/CustomFormats/Specifications/LanguageSpecification.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Parser/Model/MediaInfoModel.cs](../src/NzbDrone.Core/Parser/Model/MediaInfoModel.cs).

Use Sonarr's custom-format specification pattern rather than its TV resolution/source criteria. Bookshelf already stores format, profile, codec ID, additional features, commercial name, channels, bitrate mode, sample rate and all audio streams from bundled MediaInfo. Add conditions for AAC LC, HE-AAC, xHE-AAC/USAC, E-AC-3/Atmos, Opus, lossless formats, narrator/performance attributes and channel configuration where supported by evidence. Separate release-advertised metadata from file-verified metadata; unknown values must not match as confirmed. Acquisition preferences are user-defined, not a universal bitrate or channel ranking.

**Acceptance:** Fixtures distinguish AAC profiles, USAC, Atmos, multistream/default-stream choices, mono/stereo/multichannel, VBR/CBR and unknown data. Search predictions cannot overwrite file measurements. Existing custom-format IDs, scores and profiles survive migration; score simulation explains each match and upgrade outcome before a grab.

### B05

**Version probe metadata and invalidate stale analysis deliberately**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: source/version and stream details exist; no independent analysis schema revision.
- Approach: adapt schema/provenance pattern; keep bundled CLI integration.
- Prerequisites: [B01](#b01).
- Pinned v5 source: [src/NzbDrone.Core/MediaFiles/MediaInfo/MediaInfoModel.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/MediaInfo/MediaInfoModel.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MediaFiles/MediaInfo/MediaInfoProbe.cs](../src/NzbDrone.Core/MediaFiles/MediaInfo/MediaInfoProbe.cs).

Sonarr stores a MediaInfo schema revision and stream data. Add an explicit parsed-analysis revision and well-defined invalidation rules around Bookshelf's file state and bundled tool version. Preserve the existing MediaInfo 26.10 CLI isolation, primary-stream logic and fresh TagLib fallback. Persist only bounded data needed for diagnostics; do not copy whole tool stdout into normal logs or API list responses. Provider-advertised audio information is a separate source from measured file analysis.

**Acceptance:** A tool/parser revision schedules the intended reanalysis once; unchanged current files are not reprobed continuously. Partial/failed probes retain useful stored values, changed files invalidate appropriate fields, and multistream data remains associated with the correct stream. Raw private paths/tags never leak through debug logging or broad list payloads.

### B06

**Complete the local-recording metadata lifecycle**

- Priority / effort: **P2 / L**.
- Current Bookshelf state: create-from-tags and refresh/rescan protection exist; local editing and local-only authors are limited.
- Approach: Bookshelf-native work using v5 forms/validation patterns.
- Prerequisites: [B01](#b01), [P26](#p26), [U04](#u04), [U14](#u14).
- Pinned v5 source: [frontend/src/Settings/useSettings.ts](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Settings/useSettings.ts).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/Books/Services/LocalRecordingService.cs](../src/NzbDrone.Core/Books/Services/LocalRecordingService.cs).

Extend the existing local-recording feature with a supported metadata editor and, if needed, local-only author creation for publishers absent from Audible. Persist provenance for title, narrator, publisher, language, year and chapters while retaining the immutable local recording ID. Any future publisher integration should offer a reviewed association to that same local recording; it must not fabricate an ASIN or silently replace it with a matching Audible title. Model the local identity boundary separately from the UI form infrastructure borrowed from Sonarr.

**Acceptance:** Local metadata edits survive refresh, restart and forced rescan; concurrent edits are detected. Ordered narrator/chapter values, unknown dates and file IDs are preserved. A missing publisher catalog does not block adding or managing a local recording. Associating a provider is explicit, reversible where feasible and does not rewrite audio merely to edit library metadata.

### B07

**Expose metadata-provider health and partial-result provenance**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: cooldowns, caches, sparse-field preservation and catalog omission safeguards exist; diagnostics are limited.
- Approach: adapt refresh/health patterns without copying TV providers.
- Prerequisites: [B01](#b01), [P10](#p10), [D02](#d02).
- Pinned v5 source: [src/NzbDrone.Core/Tv/RefreshSeriesService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Tv/RefreshSeriesService.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MetadataSource/Audiobook/AudiobookInfoProxy.cs](../src/NzbDrone.Core/MetadataSource/Audiobook/AudiobookInfoProxy.cs).

Keep Audnexus/audible-rs and optional publisher sources as book-native adapters. Surface whether a refresh was complete, cached, partial, rate-limited, unavailable or awaiting identity review. Retain last-known good data through provider failures and show per-field/record freshness when useful. A short keyword result is not a complete author bibliography; a missing regional result is not authority to delete a recording. Sonarr's refresh orchestration offers operational patterns, not a replacement book catalog.

**Acceptance:** 429/timeout/5xx, confirmed missing records, repeated pages, incomplete pagination, author-credit changes and sparse metadata have distinct outcomes. No outage deletes books, silently changes owners or erases richer chapters/covers. Recovery clears or updates health state without repeated alerts; cancellation does not masquerade as a successful empty refresh.

### B08

**Explain and resolve unmapped or conflicting recording evidence**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: strict identity checks and manual/local workflows exist; evidence is not unified into a guided review.
- Approach: adapt v5 structured rejection presentation to audiobook identity.
- Prerequisites: [B01](#b01), [D02](#d02), [U01](#u01), [U02](#u02).
- Pinned v5 source: [src/NzbDrone.Core/DecisionEngine/Rejection.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/DecisionEngine/Rejection.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/MediaFiles/AudioRecordingIdentifier.cs](../src/NzbDrone.Core/MediaFiles/AudioRecordingIdentifier.cs).

Show the current recording identity alongside filename, nearest recording-folder, embedded-tag and catalog evidence, including the configured filename preference. Distinguish missing catalog coverage, contradictory identifiers, absent author identity, different performance and insufficient evidence. Link to exact-ASIN lookup, explicit manual selection, author-correction review or Create local recording as appropriate. Keep a bulk review operation bounded to the selected file/recording IDs; do not convert a higher fuzzy score into permission to reassign a recording.

**Acceptance:** A user can understand each rejection and preview the exact resulting association. US/UK editions, numbered volumes, full casts, publisher packs and non-Audible files remain distinct. Review actions revalidate stale file/DB state, preserve provenance, and provide useful errors without revealing credentials or changing audio as a side effect.

### B09

**Add book-native library diagnostics and collection statistics**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: per-author file/book statistics exist; no equivalent global statistics API.
- Approach: adapt aggregation/filtering patterns, define audiobook measures.
- Prerequisites: [B01](#b01), B03 for explicit work coverage, B05 for revision-aware media completeness, U09 for dashboard query ownership.
- Pinned v5 source: [src/Sonarr.Api.V5/Statistics/StatisticsController.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Api.V5/Statistics/StatisticsController.cs).
- Bookshelf counterpart/integration point: [src/NzbDrone.Core/AuthorStats/AuthorStatisticsService.cs](../src/NzbDrone.Core/AuthorStats/AuthorStatisticsService.cs).

Borrow v5 statistics endpoint/service organization and filtering. Show unique recordings, files, known duration and size; useful breakdowns can include codec/profile, language, narrator, local versus Audible source, missing measurements and unresolved identity review. Keep author/book/recording/volume counts distinct and separate actual collection data from diagnosis candidates. Aggregate locally; do not transplant TV season/episode/resolution categories or add telemetry as an implicit feature.

**Acceptance:** Publisher packs and multi-work memberships never double-count bytes or file duration. Unknown metadata remains unknown, filters and counts agree with library lists, and committed imports/deletions/author reconciliation invalidate the appropriate caches. Query cost is measured on large synthetic libraries and list/detail endpoints do not acquire expensive statistics unintentionally.

## Release group follow-up details — 2026-11-15

These entries come from the [focused release group comparison](sonarr-v5-release-group-comparison.md).
The current Bookshelf snapshot for this section is
`4883fcaa724dd15e57c555cd13d39cbcba379a2f`; original numbered entries retain
their original comparison evidence. D01/B01 apply to every slice below.

### RG01

**Adapt release group parsing with audiobook compatibility fixtures**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: basic parsing exists; compound names, accented suffixes,
  trailing bracketed groups, additional repost cleanup and invalid-token checks differ.
- Approach: selectively adapt; preserve audio extension handling and existing cases.
- Pinned v5 source: [ReleaseGroupParser.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Parser/ReleaseGroupParser.cs).
- Bookshelf counterpart: [Parser.ParseReleaseGroup](../src/NzbDrone.Core/Parser/Parser.cs#L630).

The isolated source replay matched all 23 active Bookshelf expectations with our
parser and all 151 donor expectations with its parser. Unmodified donor code
changes 18 Bookshelf expectations, including `2018-ENTiTLED` instead of
`ENTiTLED`. This is not a safe class replacement. Keep TV/anime exclusions and
hardcoded exception names out unless supported by book-release evidence.

**Acceptance:** preserve the 23 existing expectations; add book-shaped compound,
Latin-1, trailing-bracket and repost cases; reject codec/date/part/hash false
positives according to an explicit book policy. Exercise MP3/M4B/FLAC/Opus paths
and release titles. Verify results flow through remote-book and on-disk custom
formats, history and repack decisions without changing recording identity.

### RG02

**Carry manual-import group choices through preview and commit**

- Priority / effort: **P1 / M**.
- Current Bookshelf state: visible editor and preview update field exist; initial
  mapping, override-based score recalculation and final command propagation are incomplete.
- Approach: repair the existing cross-layer data path before adding UI features.
- Prerequisites: D01/B01; coordinate with D05 for import-score enforcement.
- Pinned v5 source: [ManualImportService.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs#L144)
  and [ManualImportFile.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportFile.cs#L19).
- Bookshelf counterparts: [manual service](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportService.cs#L266),
  [command file](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportFile.cs),
  [UI submission](../frontend/src/InteractiveImport/Interactive/InteractiveImportModalContentConnector.js#L155).

Initial item mapping never copies the resolved group. Preview updates keep
nonblank text on the response item without applying that override to the local
decision or recalculating custom formats. Final UI submission and the command
file type omit the field; execution constructs a local book without assigning it.
These are source-traced omissions, not a completed live-import reproduction.

**Acceptance:** detected group appears on first preview; set/change/clear choices
produce matching score previews and reach the committed file, naming and history.
Define omitted versus explicitly cleared values. Cover single and bulk selections,
tracked/untracked downloads, multipart recordings and existing files. Retain
ordinary re-identification provenance; an explicit correction must not be silently
overridden by the previous-file preservation rule. Test the final command, not
only preview responses, and retain staged rollback behavior on failure.

### RG03

**Expose and deliberately correct stored file release groups**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: database/model storage exists; file resource, property
  updates and file editor do not expose or edit the group.
- Approach: additive API field and deliberate file-property correction UI.
- Prerequisites: D01/B01; reuse RG02's missing/clear semantics.
- Pinned v5 source: [v5 file resource](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileResource.cs#L21),
  [single/bulk property updates](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileController.cs#L104)
  and [file card](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Episode/Summary/EpisodeFileCard.tsx#L223).
- Bookshelf counterparts: [file resource](../src/Readarr.Api.V1/BookFiles/BookFileResource.cs),
  [file controller](../src/Readarr.Api.V1/BookFiles/BookFileController.cs#L126)
  and [file editor](../frontend/src/BookFile/Editor/BookFileEditorTableContent.js).

**Acceptance:** show the actual persisted group; support setting and clearing
one or multiple explicit file IDs. Omitted fields preserve current values and
old quality-only clients remain compatible. Update custom-format displays after
success; preserve file/recording identity, acquisition provenance and unrelated
properties. Ordinary rescans must not undo an explicit correction.

### RG04

**Design recording-versus-pack group evidence precedence**

- Priority / effort: **P2 / M**.
- Current Bookshelf state: unconditional download, folder, filename precedence.
- Approach: study/adapt only after defining book pack semantics.
- Prerequisites: B03's pack membership/evidence design; D01/B01.
- Pinned v5 source: [AggregateReleaseGroup.cs](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Aggregation/Aggregators/AggregateReleaseGroup.cs).
- Bookshelf counterpart: [AggregateReleaseGroup.cs](../src/NzbDrone.Core/MediaFiles/BookImport/Aggregation/Aggregators/AggregateReleaseGroup.cs).

Sonarr lets a season-pack label yield to an individual file's group, then falls
back to the pack label. A multipart audiobook is not automatically a pack of
independent recordings. This is a design candidate, not a demonstrated failing
Bookshelf pack import.

**Acceptance:** define recording-level releaser versus pack uploader authority;
exercise single recordings, multipart recordings, mixed-group packs, missing
file labels and conflicting evidence. Retain original source evidence and
explain the resolved group. Do not overwrite explicit manual choices, merge
recordings or change ordinary multipart behavior on a season-pack analogy alone.

### RG05

**Add group-token length formatting and define missing-value behavior**

- Priority / effort: **P2 / S**.
- Current Bookshelf state: group naming token exists; length formatting is ignored
  and the legacy fallback applies only to null.
- Approach: adapt length handling; make a separate saved-template fallback decision.
- Pinned v5 source: [group token](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Organizer/FileNameBuilder.cs#L642)
  and [truncation fixture](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core.Test/OrganizerTests/FileNameBuilderTests/TruncatedReleaseGroupFixture.cs).
- Bookshelf counterpart: [group token](../src/NzbDrone.Core/Organizer/FileNameBuilder.cs#L354).

**Acceptance:** cover positive/negative length formats, long and accented names,
null/empty/whitespace values, optional separators and filename limits. Preserve
the stored full group and existing templates unless a documented compatibility
decision authorizes a naming change. Verify custom-format scoring is not changed
by truncating the display token. Do not silently rename the legacy fallback.

### 2026-11-15 release group implementation record

RG01/RG02/RG03/RG05 are implemented and verified locally in the working tree
based on `4883fcaa724dd15e57c555cd13d39cbcba379a2f`. The source-state paragraphs
above describe the pre-change audit snapshot. No schema migration is needed:
the full group was already stored on each book file. No live home/library was
used or modified.

- **RG01:** selectively extended [group extraction](../src/NzbDrone.Core/Parser/Parser.cs)
  for hyphenated and Latin-1 names, trailing brackets and repost suffixes.
  Added negative cases cover audio/language/part/date/hash metadata in suffixes
  and trailing tags; leading bracketed groups keep their existing behavior.
  Existing parser expectations and audio extension handling remain covered.
  Names without a clear release-metadata boundary can still be ambiguous;
  explicit corrections take precedence over detection.
- **RG02:** [manual import](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportService.cs)
  returns the resolved group and current custom-format matches/score. A separate
  nullable `releaseGroupOverride` in preview responses records a deliberate
  choice; the UI submits only that choice as `releaseGroup` to preview updates
  and the final command. Null/omitted keeps detection or existing provenance;
  an empty value explicitly clears. Choices survive repeated aggregation and
  same-path re-identification without changing file IDs or other acquisition
  provenance. Existing-file previews use the stored group before specifications
  and score calculation, so an untouched preview agrees with the saved result.
  Preview requests also retain the selected edition ID.
- **RG03:** [file resources and updates](../src/Readarr.Api.V1/BookFiles/BookFileController.cs)
  expose group and support single/bulk corrections. Omitted/null fields preserve
  existing values; whitespace is trimmed and empty clears. Group-only requests
  preserve quality. The file editor displays groups and offers a selected-file
  set/clear control, with server errors visible. The OpenAPI schema is updated.
- **RG05:** [naming](../src/NzbDrone.Core/Organizer/FileNameBuilder.cs) supports
  positive/negative group length formats with bounded, combining-character-safe
  truncation. Stored group values stay complete. Existing null-to-`Readarr`
  fallback and empty/whitespace omission remain compatible.

Regression coverage is in [manual-import fixtures](../src/NzbDrone.Core.Test/MediaFiles/ManualImportReleaseGroupFixture.cs),
[decision-maker fixtures](../src/NzbDrone.Core.Test/MediaFiles/TrackImport/ImportDecisionMakerFixture.cs),
[aggregation fixtures](../src/NzbDrone.Core.Test/MediaFiles/TrackImport/Aggregation/AggregateReleaseGroupFixture.cs),
[same-path import fixtures](../src/NzbDrone.Core.Test/MediaFiles/ImportApprovedTracksFixture.cs),
[file API fixtures](../src/NzbDrone.Integration.Test/ApiTests/BookFileQualityEditorFixture.cs)
and [preview API fixtures](../src/NzbDrone.Integration.Test/ApiTests/ManualImportReleaseGroupFixture.cs).
The parser and organizer fixtures cover their respective changes. Local
validation results are recorded in the roadmap's release group implementation
section. The frontend payload harness uses actual Babel-transformed source with
mocked React/Redux/network; it is not a browser-rendering test.

RG04's recording-versus-pack policy remains planned. D05/D13 remain separate
planned work: this batch does not add import-score downgrade rejection or a
minimum upgrade-score delta. Live browser/import smoke testing and release
artifact validation remain separate delivery checks.

**2026-11-16 follow-up:** the fresh linux-x64 candidate was checked against a
copied dev profile, including six real browser set/reload/clear checks, and then
deployed to the existing dev service with stopped-profile/runtime rollback
copies. The dev API's manual-preview check preserved recording/edition identity
without saving group values or importing media. Ten additional parser cases fix
trailing Audible ASIN/ISBN tags being treated as groups; 372 parser cases passed
(18 existing skips). See the [dev deployment record](sonarr-v5-modernization-roadmap.md#dev-deployment-and-real-file-checks--2026-11-16)
for the artifact, upgrade and preservation evidence. Existing null groups are
not automatically backfilled. A committing live import remains untested.

## Deliberate exclusions and decisions

- **No TV model transplant.** Season/episode numbering, anime scene mappings,
  TVDB/TMDB/IMDb ownership, TV air dates, season-search DTOs and video/HDR/subtitle
  quality rules are not Bookshelf contracts. Borrow grouping or rejection patterns
  only after defining their book meaning.
- **No wholesale importer replacement.** D01 preserves Bookshelf's staged-copy,
  atomic association and rollback behavior. The donor is not uniformly safer.
- **No dormant catalog revival.** Keep the current audiobook providers and local
  recordings. Sonarr's TV catalog and Bookshelf's leftover Goodreads/Hardcover
  Docker settings are not replacements for those providers.
- **No provider-count race.** Sonarr has more clients and TV-specific list sources.
  A generic adapter can be selected later when needed; unsupported TV payloads
  should not become book schemas just to match upstream feature counts.
- **No automatic source-ranking claims.** Higher bitrate, more channels or a
  matching title does not prove a better or identical audiobook performance.
- **No automatic external contribution.** This is a local fork roadmap. Any later
  upstream submission is a separate, human-reviewed contribution under that
  project's current contribution requirements.

## Donor caveats to retain during implementation

The exact source is evidence to inspect, not a guarantee that the donor has solved
all neighboring cases. The detailed entries identify these specific traps:

1. D01: the donor still retires old media before transferring replacement media.
2. P14: its PostgreSQL connection-string validation repeats one asymmetric predicate;
   copy the design with exhaustive tests and preserve Bookshelf's third cache DB.
3. P16/P17: review recovery/bootstrap logging for credentials rather than importing
   that policy unchanged.
4. U06: do not silence React deprecation warnings globally as an upgrade strategy.
5. U08/U11: adapt hardcoded `/api/v5` and SignalR version assumptions to Bookshelf.
6. U12/U14: preserve existing preference keys and order-significant arrays; Set
   equality is unsuitable for ordered narrators, chapters or naming tokens.
7. D08/D11: preserve explicit `skipRedownload` through every history branch. The
   analogous donor branch also omits that option, so a wholesale copy is not a fix.
8. D15/D16: TV runtime, pilot/special fallbacks and video quality units must not
   decide whether a long audiobook is a valid download.
9. RG01/RG04: the donor group parser changes 18 existing Bookshelf fixture
   expectations; season-pack provenance also needs book-native semantics. Adapt
   useful behavior with compatibility fixtures rather than replacing the parser
   or treating every multipart recording as a pack.

## Implementation record template

Append this under an entry when its execution state changes; update its index row.

```text
Date / item:
Decision and scope:
Donor commit and file/symbols:
Bookshelf commit:
Schema / API / saved-setting compatibility:
Adaptations and intentionally retained behavior:
Tests and measured outcomes:
Upgrade / backup / rollback evidence, when applicable:
Remaining limitations:
```
