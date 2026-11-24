export function releaseIdentity(release: { indexerId: number; guid: string; searchResultId?: string }) {
  return JSON.stringify([release.indexerId, release.guid, release.searchResultId ?? null]);
}

export function releaseOffsets(keys: readonly string[], heights: ReadonlyMap<string, number>, estimate: number) {
  const offsets = [0];

  for (const key of keys) {
    offsets.push(offsets[offsets.length - 1] + (heights.get(key) ?? estimate));
  }

  return offsets;
}

// Offsets describe row boundaries, including the end of the final row.
export function releaseIndexAt(offsets: readonly number[], position: number) {
  let low = 0;
  let high = Math.max(0, offsets.length - 2);

  while (low < high) {
    const middle = Math.floor((low + high + 1) / 2);

    if (offsets[middle] <= position) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }

  return low;
}

export function releaseWindowIndices(
  offsets: readonly number[],
  top: number,
  height: number,
  overscan: number,
  pinnedIndices: readonly number[] = []
) {
  const count = offsets.length - 1;
  if (!count) {
    return [];
  }

  const first = releaseIndexAt(offsets, Math.max(0, top - overscan));
  const last = releaseIndexAt(offsets, Math.max(0, top + height + overscan));
  const indices = new Set(pinnedIndices.filter((index) => index >= 0 && index < count));

  for (let index = first; index <= last; index++) {
    indices.add(index);
  }

  return Array.from(indices).sort((left, right) => left - right);
}

export type ReleaseWindowEntry = { index: number } | { gap: number; start: number };

export function releaseWindowEntries(offsets: readonly number[], indices: readonly number[]): ReleaseWindowEntry[] {
  const entries: ReleaseWindowEntry[] = [];
  let next = 0;

  for (const index of indices) {
    if (index > next) {
      entries.push({ gap: offsets[index] - offsets[next], start: next });
    }

    entries.push({ index });
    next = index + 1;
  }

  const remaining = offsets[offsets.length - 1] - offsets[next];
  if (remaining > 0) {
    entries.push({ gap: remaining, start: next });
  }

  return entries;
}
