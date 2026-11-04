# Audiobook normalization fixtures

Public US catalog metadata captured on 2026-11-02 from Audnexus book lookups
and Audible author catalog responses through audible-rs. Each ASIN file pairs
an Audnexus object with an audible-rs product-response envelope.

These are field projections, not complete wire captures. Descriptions,
biographies, image URLs, account data, and credentials are omitted. Selected
fields retain their original values and types; absent fields remain absent.
The audible-rs envelope uses the single-product shape around the original
catalog product so tests can exercise the real HTTP response parser offline.

The six recordings cover Yu Omiya volumes 1 and 2 and Primal Hunter volumes 1,
4, 7, and 9: optional subtitles, punctuation and title variants, comma spacing
in publishers, category hierarchies, series positions, ISO timestamps, and
calendar dates. Their ASINs and marketplace remain their identities.

`KumaKumaKumaBear.json` projects two complete Audible author catalog responses
captured on the same date. The English series B0F7Z67917 has 14 recordings
(volumes 1–13 and 11.5). Seven are credited to Kumanano and seven to くまなの;
the author IDs are B0DSLB5CG5 and B074QPMWRL, with five recordings omitting an
author ID. The fixture preserves the original author credits, titles, languages,
and series positions. Both profiles can be checked against the
[public series listing](https://www.audible.com/series/Kuma-Kuma-Kuma-Bear-Light-Novel-Audiobooks/B0F7Z67917).
The `series` catalog query parameter is ignored by Audible and must not be used
as evidence of series membership; these captures use the `author` parameter.

`AudiobookMetadataOverrideService` reads verified identities from the database,
scoped to a marketplace.
Add aliases with evidence and a regression fixture; do not infer identity from
transliteration, matching titles, or participation in the same series. Author
spellings do not change a recording's language or its marketplace/ASIN identity.

`AuthorSpellings.json` pairs projected Audnexus profiles and book-search results
for Mitizou (道造), Miya Kazuki (香月美夜), and Pirateaba (pirate aba). Each pair
shares the same author ASIN but uses different profile and book-credit names.
These spellings can be learned as aliases using that shared identity. Regression
tests also clear the memory cache and reload persisted author metadata to check
that later refreshes retain the aliases and continue finding the books.

`RifujinNaMagonote.json` contains projected Audible catalog responses for
Rifujin na Magonote and 理不尽な孫の手, plus both Audnexus author profiles
(B00LR4UJB8 and B076N4Q46Q). The two name queries return 33 and 3 recordings:
all 26 main-series volumes, Recollections, A Journey of Two Lifetimes, three
Redundant Reincarnation volumes, and five Orc Eroica volumes. The Japanese
credits account for main-series volumes 13, 17, and 18. Some other credits have
no author ASIN. Several volumes put the series subtitle and volume number in
the subtitle field rather than the title. Volume 21 uses a colon before its
volume number, and later Orc Eroica titles end with `(light novel)`.

The original title of B0D5JN2RH2 (volume 18) ends in `Reincarnatio`. Its display
label has a correction scoped to the US ASIN and exact observed title, supported
by the [publisher's volume 18 listing](https://sevenseasentertainment.com/audio_books/mushoku-tensei-jobless-reincarnation-audiobook-vol-18/)
and [Audible's structured series listing](https://www.audible.com/series/Mushoku-Tensei-Jobless-Reincarnation-Audiobooks/B0CJYVSQFB).
Tests retain original titles and recording IDs, require every main-series
position 1–26, and reject title corrections for different recordings,
marketplaces, or changed source titles. A conflicting series position prevents
building a numbered series label; the explicit corrected title is shown instead.

## Persistent overrides

Migration 044 copies the initial curated records from
`src/NzbDrone.Core/MetadataSource/Audiobook/Overrides/defaults.json` into
`AudiobookMetadataOverrides`. Seed data is read only by the migration; disabling
or deleting a record never restores it on startup. The generic mapper and text
normalizer contain no author ASIN lists or individual title corrections.

Manage records in **Settings → Metadata → Metadata overrides**, or through
`GET/POST /api/v1/metadataoverride` and `GET/PUT/DELETE /api/v1/metadataoverride/{id}`.
Each record has an enabled flag, marketplace, ASIN, evidence URL, reason, and UTC
update timestamp. Author mappings additionally hold a canonical name, alternate
ASINs, and name spellings. Overlapping enabled author claims and mapping chains
are rejected. Refresh affected authors after editing an identity mapping.

Title corrections hold an exact provider title and a corrected display title.
Only the display label uses the correction; stored book and edition titles stay
unchanged. A different provider title stops matching and appears as **Needs
review** in the editor, alongside the currently stored provider title. Corrections
for books outside the library are marked **Not in library**. Book lists, search,
calendar, and iCalendar use the same display-label service.

The calendar defaults to **Monitored Only**, which requires both the book and its
author to be monitored. **All** includes unmonitored sample metadata without
changing monitoring or enabling download searches.

## MAM search and matching

MAM requests use JSON POST to `tor/js/loadSearchJSONbasic.php`, `main_cat: [13]`
for audiobooks, a top-level `perpage`, and `tor.startNumber` offsets. RSS uses
`dateDesc`; interactive searches preserve the configured fields, language, and
search type. Each query is bounded to 1,000 results and uses the indexer's rate
limit. Search does not request account-bound download hashes or download files.
The `tid` download request receives the session cookie only for the configured
indexer origin.

The adapter preserves structured author, narrator, title, and series metadata.
Actual `seeders` counts are distinct from `times_completed`. Numeric flags can
arrive as numbers, booleans, or strings; `fl_vip` can be nonzero beyond 1. Encoded
author/narrator/series objects are parsed separately. Inferred `browseflags`
meanings and uploader ownership are not used to decide recording identity.

Native audiobook matching uses the full author's catalog rather than assuming a
search result is the requested book. Titles and series positions must agree;
narrators distinguish recordings of the same volume. Conflicting credits,
ambiguous recordings, and series ranges/packs remain rejected for manual review.
Structured failures cannot fall back to a fuzzy match against a short provider
title such as `Mushoku Tensei`. The existing requested-book check then rejects a
correctly identified companion title returned during a volume-one search.

Migration 045 adds narrator aliases to the existing override editor. Narrator
records have a marketplace, canonical name, aliases, evidence, reason, and
enabled state; they do not require an ASIN. Aliases affect matching only and
preserve both catalog and indexer credits. Overlapping enabled identities are
rejected. The initial Cliff Kirk/Cliff Kurt equivalence is supported by
[the narrator's own explanation](https://www.reddit.com/r/mushokutensei/comments/1ds42cu/i_am_the_narrator_of_the_mushoku_tensei_audiobooks/)
and [the publisher's announcement](https://sevenseasentertainment.com/2023/07/24/seven-seas-launches-audiobook-for-mushoku-tensei-light-novel-series/).
It is seed data, with no narrator-specific matching rule in C#.

`MyAnonaMouseOfflineFixture`, `AudiobookReleaseMatcherFixture`, and
`AudiobookSearchDecisionFixture` use synthetic tracker data. They cover paging,
numeric flags, real seeder counts, metadata retention, different narrations,
conflicting volume numbers, packs, and preventing a companion release from being
approved as volume one. They require no tracker credentials or torrent downloads.
