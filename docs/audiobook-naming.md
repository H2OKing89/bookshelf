# Audiobook file naming

Choose a format in **Settings → Media Management → Book Naming**. The token
picker offers an importer-inspired format and a shorter library format. Both
include optional part numbers, which appear only for multipart recordings.
Formats are relative to the author's folder; extensions are preserved.

## Title and recording tokens

| Token | Value |
| --- | --- |
| `{Book Title}` | Published edition title, retaining the existing behavior. |
| `{Book DisplayTitle}` | The shared cleaned display title, including enabled title corrections. For example, `The Primal Hunter #4`. |
| `{Book NamingTitle}` | Primary series, padded explicit position, and distinct work title where they can be separated confidently: `The Wandering Inn vol_20 Memory's Flame`. |
| `{Book WorkTitle}` | The distinct work title, such as `Memory's Flame` or `Aincrad`. Falls back to the display title when there is no distinct work title. |
| `{Book Subtitle}` | The edition's separate subtitle; falls back to splitting a legacy combined title when absent. |
| `{Book NamingSubtitle}` | The cleaned subtitle, empty when recognized promotional or repeated catalog text was removed. |
| `{Book Series}` | Primary series, with the existing series-order fallback for older metadata. |
| `{Book NamingSeries}` | The cleaned primary series name used by both audiobook presets. |
| `{Book SeriesPosition:00}` | Padded explicit position: `01`, `08.5`, `01-03.5`. Unknown strings are retained. Below-one decimal positions retain `0.5`. |
| `{Book Narrators}` | Narrators from the file's edition, including enabled narrator corrections. |
| `{Book Asin}` | The edition's ASIN, omitted when absent or invalid. |
| `{Book Language}` / `{Book Publisher}` | The edition's language code / publisher. |
| `{PartNumber:00}` | File number, such as `01`; omitted for single-file recordings. |

`CleanTitle` retains its original meaning: punctuation cleanup for filenames.
Use the new naming tokens for structural title cleanup. The exact file edition
supplies subtitles and narrator credits, even if another edition is monitored.
Published metadata is retained; naming does not rewrite catalog titles.

## Presets

Importer-inspired:

```text
{Book NamingSeries}/{Book NamingTitle}{ (Edition Year)}{ (Author Name)}{ [Book Asin]}/{Book NamingTitle}{ (Edition Year)}{ (Author Name)}{ [Book Asin]}{ (PartNumber:00)}
```

Example below the author folder:

```text
The Wandering Inn/
  The Wandering Inn vol_20 Memory's Flame (2026) (pirate aba) [B0HGBVHDM2]/
    The Wandering Inn vol_20 Memory's Flame (2026) (pirate aba) [B0HGBVHDM2].m4b
```

Shorter library format:

```text
{Book NamingSeries}/{Book SeriesPosition:00 - }{Book WorkTitle}{ [Book Asin]}/{Author Name} - {Book WorkTitle}{ (PartNumber:00)}
```

```text
The Wandering Inn/
  20 - Memory's Flame [B0HGBVHDM2]/
    pirate aba - Memory's Flame.m4b
```

Multipart filenames end in `(01)`, `(02)`, and so on. Missing optional metadata
removes its surrounding punctuation when grouped in a token, such as
`{ [Book Asin]}`. For an additional edition label, add
`{ (Book Disambiguation)}`. Neither preset changes the saved naming format until
selected and saved. Enabling renaming and applying existing rename commands
remain separate actions.

## Shared title cleanup

**Settings → Metadata → Title cleanup** manages recognized phrases and the
metadata labels they produce. New installations seed these rules disabled.
Use **Preview all rules enabled** to review the library before enabling the
rules you want. Draft previews never save settings or rename files. Individual
rule saves apply to display and naming without refreshing catalog metadata.

Rules match complete subtitles, parenthetical labels, and delimited title or
series segments. Matching ignores case and repeated whitespace. Unknown wording
and words inside meaningful titles remain intact. The initial rules include
`Light Novel`, `A Light Novel`, `A Sci-Fi Light Novel`, `LitRPG`, `A LitRPG`,
`A LitRPG Adventure`, `A Fantasy LitRPG Adventure`, `A Novel`, complete
slice-of-life/harem promotional lines, and two bestseller/movie labels.
An unwrapped series suffix can also be removed when the exact same complete
label is independently evidenced by the recording's title or subtitle. For
example, `Kuma Kuma Kuma Bear Light Novel` is cleaned when that recording's
title contains `(Light Novel)`; a bare series-name suffix alone is insufficient.

Removed light-novel phrases produce the metadata label `Light Novel`.
Explicit LitRPG phrases produce `LitRPG`. Other phrases have no derived label
unless you configure one. These are filterable book metadata, separate from
provider genres and the organizational Tags used by indexers and download
clients. Removal evidence records the source field, text, rule, and labels.

| Published metadata | Cleaned result |
| --- | --- |
| Title `The Primal Hunter 9: A LitRPG Adventure` | Display `The Primal Hunter #9`; label `LitRPG`. |
| Title `Kuma Kuma Kuma Bear (Light Novel) Vol. 13` | Display `Kuma Kuma Kuma Bear #13`; label `Light Novel`. |
| Subtitle `Light Novel, Vol. 5`, matching series position `5` | Empty displayed subtitle; label `Light Novel`; the recording's volume remains in its series position. |
| Subtitle `A LitRPG Adventure (The Primal Hunter)` | The confirmed primary-series repetition and recognized label are removed. |
| Subtitle `A LitRPG Adventure in Space` without a configured matching rule | Preserved. |

Book details, calendar titles, and normalized naming tokens consume the same
projection. Published title, subtitle, series, genres, ASINs, and identities
remain stored unchanged. An empty cleaned subtitle stays empty in the UI.
Existing raw naming tokens remain available. Indexer queries and release
matching retain their existing behavior.

Use **Metadata overrides → Title/subtitle protection** for text that is part of
a real work title. Supply its marketplace, recording ASIN, exact provider title
and/or subtitle, evidence URL, and reason. Protected fields remain intact through
display and naming. Guards apply independently; changed provider text produces a
Needs review status and a warning instead of protecting unrelated new text.
An applicable title correction takes precedence over a title protection for
that same field and is exempt from promotional phrase removal.

## Cleanup boundaries

The first implementation reuses deterministic ideas from the user's
`audiobook_importv2` policy: normalize fields before rendering, use primary series,
preserve useful work subtitles, and separate path sanitation from metadata.
Reference sources are `docs/naming-rules.md`, `docs/naming-matrix.md`,
`audiobook_importer/metadata_normalization.py`, and the series/title helpers in
`scripts/ollama_metadata_probe.py` in that repository.

- A repeated series/volume label is removed only when its complete explicit
  position agrees with the metadata. Conflicting or inseparable titles are
  retained. Decimal fractions and explicit ranges are preserved.
- An exact repeated subtitle is rendered once unless explicitly protected.
  Recognized phrases are editable data; unknown promotional variants require a
  rule and can be reviewed with the draft preview.
- Missing positions remain missing. Unnumbered and standalone titles retain
  their display title; use an explicit subtitle token if desired.
- Protected punctuation and spelling are retained until filesystem sanitation.
  There is no automatic English title casing, translation, contributor-role
  inference, or grammatical rewriting of work titles.
- ASINs, source suffixes such as `-H2OKing`, ranges, and recording parts are
  never invented. Torrent provenance and edition-recovery logic are not ported.

The importer-inspired format is a library naming convention; its `vol_XX` and
year placement do not guarantee parsing by other library applications.

Use the Settings single/multipart examples and the existing rename preview to
review changes. Previews do not currently check an entire batch for duplicate
destinations. Keep part numbering and recording identifiers where applicable.
Byte-length guards for Unraid paths, collision reporting, broader inferred
cleanup, and sidecar handling remain separate work; no remote library rename or
importer execution is part of this change.
