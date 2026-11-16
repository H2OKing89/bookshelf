# Release group gaps against Sonarr v5

Reviewed **2026-11-15, America/Chicago**. Bookshelf source:
`4883fcaa724dd15e57c555cd13d39cbcba379a2f` (clean working tree before this
documentation update). Donor: **Sonarr v5.0.0.2941**,
[`c018578e4b79ee048575c02d7a8aa2b929e4b837`](https://github.com/Sonarr/Sonarr/tree/c018578e4b79ee048575c02d7a8aa2b929e4b837),
the same pinned release as the modernization roadmap. This is a comparison with
that release, not a claim about the latest Sonarr build or when a feature debuted.

Here, release group means the uploader/releaser label used for parsing,
preferences and file provenance. Quality groups and catalog edition grouping are
different features.

**Bookshelf has the basic matching feature, but lacks important parts of the
surrounding workflow.** The largest gaps are parser coverage, manual-import
round trips, editing stored groups and import-time score enforcement. The earlier
answer overstated manual-import support by treating the visible input as proof
that its value reaches the saved file.

**Implementation follow-up:** RG01/RG02/RG03/RG05 have since been implemented in
the local working tree; see the [implementation record](sonarr-v5-porting-backlog.md#2026-11-15-release-group-implementation-record).
The capability table, source tracing and parser replay below describe the pinned
pre-change audit snapshot. RG04 and D05/D13 remain planned.

## Capability comparison

| Area | Inspected Sonarr v5 | Current Bookshelf | Assessment |
| --- | --- | --- | --- |
| Group custom-format condition | Case-insensitive regex against parsed/stored group; required and negate behavior | Same condition and regex behavior, with the book model substituted | Existing capability; retain IDs, expressions and scores |
| Group parsing | Dedicated parser; supports two-component hyphenated names, Latin-1 letters, trailing bracketed groups, exception names, additional repost cleanup and invalid-token checks | Older ASCII suffix regex, leading bracketed groups and shorter repost cleanup | Substantial gap; selective adaptation required |
| Manual-import preview | Maps the resolved group, reapplies user choices during reprocessing and recalculates custom formats | Initial `MapItem` omits the group; updates retain nonblank text on the preview item but do not apply that override to the decision object or recalculate its custom formats | Preview can be incomplete or inconsistent with the chosen group |
| Manual-import commit | Command includes `ReleaseGroup`; execution copies it into the local item and reapplies it after aggregation | Frontend command omits the group, `ManualImportFile` has no group property and execution does not assign it | Concrete missing data path; the visible editor does not provide end-to-end support |
| Stored-file API and UI | v5 file resource exposes group; individual and bulk property updates accept it; episode file card displays it | Database model stores group, but `BookFileResource` omits it and file updates/editors only support quality changes | Users cannot inspect/correct the stored value through the corresponding file workflow |
| Automatic import provenance | Download/folder before filename, except season-pack labels yield to individual-file evidence | Always download, then folder, then filename | Ordinary precedence exists; pack-aware policy needs a book-native design |
| Same-quality replacement | Import specification rejects a lower custom-format score and distinguishes score loss after renaming | Import specification only compares quality/revision, despite receiving the format service | Existing D05 gap also affects group preferences |
| Minimum upgrade improvement | Profile `MinUpgradeFormatScore` rejects score gains below the configured delta | Any higher score qualifies after the quality/revision comparison | Existing D13 gap also affects group preferences |
| Naming | Group token supports length formatting and treats null/empty/whitespace as missing | Group token exists, ignores length formatting and only falls back for null | Small, separate naming gap |
| History, webhooks, scripts and re-identification | Group participates in provenance and custom-format calculation | History, webhook and script fields already exist; re-identification explicitly retains stored group and scene name | Preserve these paths; do not classify storage or notifications as missing |

## Parser behavior reproduced

A disposable .NET 10 harness replayed the exact group-parser methods/regexes
from both source snapshots. It compiled Sonarr's `ParserCommon`, `RegexReplace`
and extension-removal code; dependency substitutes supplied the source extension
lists and trivial string/quality helpers. Bookshelf's parser methods and regex
fields were extracted from the current source with its audio extension list.
This isolates **group extraction**, not full title identification or an import.

There were **186 inputs**: 23 active Bookshelf fixture cases, 151 Sonarr fixture
cases and 12 synthetic audiobook-shaped examples. Each parser reproduced all of
its own fixture expectations. Sonarr matched only **5 of Bookshelf's 23**
expectations; Bookshelf matched **58 of Sonarr's 151**. These are different domain
fixtures, not production accuracy percentages. The ignored Bookshelf path test
was excluded; Sonarr's separate `ParsePath` test was not replayed.

Selected outputs (`unknown` means a null result):

| Input | Bookshelf | Sonarr v5 | Implication |
| --- | --- | --- | --- |
| `Author.Book.M4B-Team-One` | `One` | `Team-One` | Ours loses part of a hyphenated name |
| `Author.Book.M4B-Báleygr` | unknown | `Báleygr` | Ours misses this accented name; donor coverage is Latin-1, not arbitrary Unicode |
| `Author.Book.M4B [AudioCrew]` | unknown | `AudioCrew` | Ours misses a trailing bracketed group |
| `Author.Book.M4B-Crew-RePACKPOST` | `RePACKPOST` | `Crew` | Ours mistakes newer repost noise for the group |
| `Author.Book.M4B-Crew-[N-Z-B]-xpost` | `B` | `Crew` | Ours can extract part of an indexer marker |
| `Author.Book.M4B-deadbeef` | `deadbeef` | unknown | Donor rejects an eight-character hexadecimal token |
| `Author.Book.M4B-S01` | `S01` | unknown | Donor rejects season/episode-shaped labels; decide book applicability explicitly |
| `Author.Book.M4B-Crew.m4b` | `Crew` | `Crew` | Ordinary suffixed groups already work |
| `[AudioCrew] Author - Book.m4b` | `AudioCrew` | `AudioCrew` | Leading bracketed groups already work |
| `Olafur.Arnalds-Remember-WEB-2018-ENTiTLED` | `ENTiTLED` | `2018-ENTiTLED` | Unchanged donor parser breaks an existing Bookshelf expectation |
| `7s-atlantis-128.mp3` | unknown | `atlantis-128` | Unchanged donor parser introduces another existing-fixture regression |
| `Author.Book.HE-AAC` | `AAC` | `AAC` | Neither parser proves audiobook codec tokens are safe from false group matches |

The audio examples illustrate syntax, not observed tracker releases or claims
that these are audiobook release groups. Sonarr's TV quality/language exclusions,
anime substitutions and hardcoded exception names are donor-specific. Its media
extension list also targets video; retain Bookshelf's audio extension handling.
Do not copy the donor regex or its entire exception-name list unchanged.

Sources: [Bookshelf parser](../src/NzbDrone.Core/Parser/Parser.cs#L630),
[Bookshelf fixture](../src/NzbDrone.Core.Test/ParserTests/ReleaseGroupParserFixture.cs),
[Sonarr parser](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Parser/ReleaseGroupParser.cs),
[Sonarr fixture](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core.Test/ParserTests/ReleaseGroupParserFixture.cs).

## Manual import and stored-file correction

The current manual-import path has several separate omissions:

1. [Initial mapping](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportService.cs#L266)
   calculates custom formats from the decision but never assigns its group to the
   returned item.
2. [Preview updates](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportService.cs#L248)
   preserve a nonblank user-entered group on the item. They do not set that value
   on the reprocessed `LocalBook` or recalculate its custom formats. Blank input
   is replaced by the detected group, so deliberate clearing has no defined path.
3. [Final UI submission](../frontend/src/InteractiveImport/Interactive/InteractiveImportModalContentConnector.js#L155)
   omits the group. The
   [command file type](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportFile.cs)
   also omits it, and
   [execution](../src/NzbDrone.Core/MediaFiles/BookImport/Manual/ManualImportService.cs#L337)
   constructs a local item without assigning a group.
4. The importer saves the local item's group to the file record. Existing-file
   [re-identification](../src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs#L239)
   intentionally restores the previous record's group. A future explicit
   correction must distinguish itself from an ordinary re-identification;
   weakening that preservation rule globally would lose provenance.

Sonarr's [manual command](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportFile.cs#L19)
carries the group, and its
[manual service](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs#L144)
reapplies chosen values during both preview and execution. These are source-traced
findings; no browser or live-library manual import was performed for this audit.

For already imported files, Bookshelf's
[resource](../src/Readarr.Api.V1/BookFiles/BookFileResource.cs) does not expose
`ReleaseGroup`; its [controller](../src/Readarr.Api.V1/BookFiles/BookFileController.cs#L126)
supports quality updates only. Sonarr exposes it in the
[v5 resource](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileResource.cs#L21),
accepts single/bulk corrections in the
[v5 controller](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileController.cs#L104)
and shows it on the
[file card](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/frontend/src/Episode/Summary/EpisodeFileCard.tsx#L223).
Bookshelf needs an additive API field and an explicit correction workflow that
preserves file IDs, recording ownership and acquisition history.

## Group preferences, import decisions and naming

The regex matcher itself is effectively at parity:
[Bookshelf condition](../src/NzbDrone.Core/CustomFormats/Specifications/ReleaseGroupSpecification.cs),
[Sonarr condition](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/CustomFormats/Specifications/ReleaseGroupSpecification.cs).
Both regex bases use case-insensitive matching. Both history format calculators
reparse the source title rather than using the stored history group directly;
that is a shared limitation, not a uniquely missing Bookshelf feature.

The enforcement gaps are already tracked as
[D05](sonarr-v5-porting-backlog.md#d05) and
[D13](sonarr-v5-porting-backlog.md#d13). For example, at the same quality,
Bookshelf's import upgrade specification does not reject a file solely because
its group-based custom-format score is lower than the existing recording's.
Sonarr's [import specification](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/UpgradeSpecification.cs#L61)
does that comparison, including before/after-rename evidence. Its
[acquisition comparison](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs#L105)
also applies the profile's minimum score improvement. These checks concern total
custom-format score, not an unconditional preference for one group regardless of
quality, revision, narrator or recording identity.

Bookshelf's [aggregation](../src/NzbDrone.Core/MediaFiles/BookImport/Aggregation/Aggregators/AggregateReleaseGroup.cs)
always prefers download, folder, then filename. Sonarr's
[aggregation](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/MediaFiles/EpisodeImport/Aggregation/Aggregators/AggregateReleaseGroup.cs)
lets a season-pack label yield to individual-file evidence, with pack fallback.
For books, distinguish a multipart recording from a publisher/multi-recording
pack before changing precedence. A pack uploader and an individual recording's
releaser can differ; this is a design candidate, not a demonstrated failing pack
import in Bookshelf.

For naming, [Bookshelf](../src/NzbDrone.Core/Organizer/FileNameBuilder.cs#L354)
returns the group directly or uses the legacy `Readarr` fallback for null.
[Sonarr](https://github.com/Sonarr/Sonarr/blob/c018578e4b79ee048575c02d7a8aa2b929e4b837/src/NzbDrone.Core/Organizer/FileNameBuilder.cs#L642)
also handles empty/whitespace groups and truncation such as `{ReleaseGroup:12}`
or `{ReleaseGroup:-17}`. Saved naming templates require an explicit compatibility
decision before changing fallback behavior.

## Roadmap recommendation

The [release group follow-up index](sonarr-v5-porting-backlog.md#release-group-follow-up-index)
tracks five additional slices without renumbering the original 87-item backlog:

1. **RG02 — manual-import round trip (P1):** repair the existing input's data
   path, preview score calculation and explicit-clear behavior first.
2. **RG01 — selective parser improvements (P1):** add audiobook fixtures for
   the useful donor syntax and negative cases; keep every current expectation
   unless a separately reviewed behavior change justifies replacing it.
3. **D05/D13 — enforcement (P1):** make group preferences hold through import,
   rename and score-based upgrades while preserving recording identity.
4. **RG03 — stored-file visibility/correction (P2):** expose the actual saved
   group and support deliberate corrections after import.
5. **RG05 — naming details (P2):** optional truncation and defined missing-value
   behavior, with saved-template compatibility.
6. **RG04 — pack evidence policy (P2):** design with B03; preserve ordinary
   multipart-recording precedence until evidence supports changing it.

No application code, database schema or live library changed in this comparison.
Validation consists of source tracing and isolated parser replay; full donor
builds, HTTP/UI workflows, import transactions and naming behavior were not run.
