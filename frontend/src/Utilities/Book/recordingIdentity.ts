import {
  AudibleMarketplace,
  audibleMarketplaces,
  BookId,
  BookIdentity,
  RecordingIdentity,
} from 'Book/BookIdentity';

function isAudibleMarketplace(value: string): value is AudibleMarketplace {
  return audibleMarketplaces.some((marketplace) => marketplace === value);
}

export function parseRecordingIdentity(value: unknown): RecordingIdentity | null {
  if (typeof value !== 'string') {
    return null;
  }

  const audible = /^audible:([a-z]{2}):(B[A-Z0-9]{9}|[0-9]{9}[0-9X])$/.exec(value);
  if (audible) {
    const [, marketplace, asin] = audible;
    if (isAudibleMarketplace(marketplace)) {
      return { source: 'audible', id: `audible:${marketplace}:${asin}`, marketplace, asin };
    }
  }

  const local = /^local:recording:([a-f0-9]{32}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})$/.exec(value);
  return local ? { source: 'local', id: `local:recording:${local[1]}` } : null;
}

export function getRecordingIdentity(id: string | null | undefined) {
  const identity = parseRecordingIdentity(id);
  if (identity?.source !== 'audible') {
    return null;
  }

  return { marketplace: identity.marketplace.toUpperCase(), asin: identity.asin };
}

interface SeriesBook extends BookIdentity {
  statistics?: { bookFileCount?: number | null } | null;
}

type SeriesPositions = Readonly<Partial<Record<BookId, string | number | null>>>;

export function summarizeSeriesRecordings(items: readonly SeriesBook[], positionMap: SeriesPositions) {
  if (!items.length) {
    return null;
  }

  const marketplaces = new Set<string>();
  const volumes = new Map<string, boolean>();
  let allPositionsKnown = true;
  let allFileCountsKnown = true;

  for (const item of items) {
    const identity = getRecordingIdentity(item.foreignEditionId || item.foreignBookId);
    if (!identity) {
      return null;
    }
    marketplaces.add(identity.marketplace);

    const position = String(positionMap[item.id] ?? '').trim();
    if (/^\d+(?:\.\d+)?$/.test(position)) {
      const key = String(Number(position));
      const fileCount = item.statistics?.bookFileCount;
      if (fileCount == null || !Number.isInteger(fileCount) || fileCount < 0) {
        allFileCountsKnown = false;
      }

      volumes.set(key, !!volumes.get(key) || (fileCount ?? 0) > 0);
    } else {
      allPositionsKnown = false;
    }
  }

  return {
    recordingCount: items.length,
    volumeCount: allPositionsKnown ? volumes.size : null,
    ownedVolumeCount: allPositionsKnown && allFileCountsKnown ? [...volumes.values()].filter(Boolean).length : null,
    marketplaces: [...marketplaces].sort(),
    hasAlternates: allPositionsKnown && volumes.size < items.length,
  };
}
