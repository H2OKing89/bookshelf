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
