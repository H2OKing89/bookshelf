export function getRecordingIdentity(id) {
  const match = /^audible:([a-z]{2}):(B[A-Z0-9]{9}|[0-9]{9}[0-9X])$/.exec(id || '');

  return match ? { marketplace: match[1].toUpperCase(), asin: match[2] } : null;
}

export function summarizeSeriesRecordings(items, positionMap) {
  const identities = items.map((item) => getRecordingIdentity(item.foreignEditionId || item.foreignBookId));

  if (!items.length || identities.some((identity) => !identity)) {
    return null;
  }

  const volumes = new Map();
  let allPositionsKnown = true;
  let allFileCountsKnown = true;

  items.forEach((item) => {
    const position = String(positionMap[item.id] ?? '').trim();
    if (!/^\d+(?:\.\d+)?$/.test(position)) {
      allPositionsKnown = false;
      return;
    }

    const key = String(Number(position));
    const fileCount = item.statistics?.bookFileCount;
    if (!Number.isInteger(fileCount) || fileCount < 0) {
      allFileCountsKnown = false;
    }

    volumes.set(key, (volumes.get(key) || false) || fileCount > 0);
  });

  return {
    recordingCount: items.length,
    volumeCount: allPositionsKnown ? volumes.size : null,
    ownedVolumeCount: allPositionsKnown && allFileCountsKnown ? [...volumes.values()].filter(Boolean).length : null,
    marketplaces: [...new Set(identities.map((identity) => identity.marketplace))].sort(),
    hasAlternates: allPositionsKnown && volumes.size < items.length
  };
}
