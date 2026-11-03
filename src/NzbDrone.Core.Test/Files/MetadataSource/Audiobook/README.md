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

`AudiobookAuthorIdentity` maps only verified identities within a marketplace.
Add aliases with evidence and a regression fixture; do not infer identity from
transliteration, matching titles, or participation in the same series. Author
spellings do not change a recording's language or its marketplace/ASIN identity.

`AuthorSpellings.json` pairs projected Audnexus profiles and book-search results
for Mitizou (道造), Miya Kazuki (香月美夜), and Pirateaba (pirate aba). Each pair
shares the same author ASIN but uses different profile and book-credit names.
These spellings can be learned as aliases using that shared identity. Regression
tests also clear the memory cache and reload persisted author metadata to check
that later refreshes retain the aliases and continue finding the books.
