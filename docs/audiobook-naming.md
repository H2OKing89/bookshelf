# Audiobook file naming

Choose a format in **Settings → Media Management → Book Naming**. The token
picker offers an importer-inspired format and a shorter library format. Both
include optional part numbers, which appear only for multipart recordings.
Formats are relative to the author's folder; extensions are preserved.

## Title and recording tokens

| Token | Value |
| --- | --- |
| `{Book Title}` | Published edition title, retaining the existing behavior. |
| `{Book DisplayTitle}` | The existing normalized display title, including enabled title corrections. For example, `The Primal Hunter #4`. |
| `{Book NamingTitle}` | Primary series, padded explicit position, and distinct work title where they can be separated confidently: `The Wandering Inn vol_20 Memory's Flame`. |
| `{Book WorkTitle}` | The distinct work title, such as `Memory's Flame` or `Aincrad`. Falls back to the display title when there is no distinct work title. |
| `{Book Subtitle}` | The edition's separate subtitle; falls back to splitting a legacy combined title when absent. |
| `{Book Series}` | Primary series, with the existing series-order fallback for older metadata. |
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
{Book Series}/{Book NamingTitle}{ (Edition Year)}{ (Author Name)}{ [Book Asin]}/{Book NamingTitle}{ (Edition Year)}{ (Author Name)}{ [Book Asin]}{ (PartNumber:00)}
```

Example below the author folder:

```text
The Wandering Inn/
  The Wandering Inn vol_20 Memory's Flame (2026) (pirate aba) [B0HGBVHDM2]/
    The Wandering Inn vol_20 Memory's Flame (2026) (pirate aba) [B0HGBVHDM2].m4b
```

Shorter library format:

```text
{Book Series}/{Book SeriesPosition:00 - }{Book WorkTitle}{ [Book Asin]}/{Author Name} - {Book WorkTitle}{ (PartNumber:00)}
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
- An exact repeated subtitle is rendered once. The exact generic subtitle
  `A LitRPG Adventure` is omitted from structured naming; broader promotional
  phrase removal is not implemented.
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
Byte-length guards for Unraid paths, collision reporting, broader evidence-based
cleanup, and sidecar handling remain separate work; no remote library rename or
importer execution is part of this change.
