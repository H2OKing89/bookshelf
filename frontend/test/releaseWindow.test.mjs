import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const {
  releaseIdentity,
  releaseIndexAt,
  releaseOffsets,
  releaseWindowEntries,
  releaseWindowIndices
} = loadFrontendModule('InteractiveSearch/releaseWindow.ts');

test('result window uses indexer, guid and search token for measurements and actions', () => {
  const release = { guid: 'same-guid', indexerId: 12, searchResultId: 'token-1' };
  assert.notEqual(releaseIdentity(release), releaseIdentity({ ...release, indexerId: 13 }));
  assert.notEqual(releaseIdentity(release), releaseIdentity({ ...release, searchResultId: 'token-2' }));
  assert.equal(releaseIdentity(release), releaseIdentity({ ...release, isGrabbed: true }));
});

test('measured height changes and reordering preserve exact visible row boundaries', () => {
  const keys = ['a', 'b', 'c'];
  const heights = new Map([['b', 180]]);
  assert.deepEqual(releaseOffsets(keys, heights, 60), [0, 60, 240, 300]);
  const offsets = releaseOffsets(['b', 'c', 'a'], heights, 60);
  assert.deepEqual(offsets, [0, 180, 240, 300]);
  assert.equal(releaseIndexAt(offsets, 179), 0);
  assert.equal(releaseIndexAt(offsets, 180), 1);

  heights.set('b', 360);
  const expanded = releaseOffsets(['b', 'c', 'a'], heights, 60);
  assert.deepEqual(expanded, [0, 360, 420, 480]);
  assert.equal(releaseIndexAt(expanded, 240), 0);
});

test('a large result window stays bounded across the list and preserves a distant focused release', () => {
  const keys = Array.from({ length: 1000 }, (_, index) => `release-${index}`);
  const heights = new Map(keys.map((key, index) => [key, index % 3 === 0 ? 260 : 50]));
  const offsets = releaseOffsets(keys, heights, 80);
  const total = offsets.at(-1);

  for (const top of [0, 320, Math.round(total / 2), total - 720]) {
    const indices = releaseWindowIndices(offsets, top, 720, 450, [999]);
    assert.ok(indices.length < 40);
    assert.ok(indices.includes(999));
    for (let index = 0; index < keys.length; index++) {
      if (offsets[index + 1] > top && offsets[index] < top + 720) {
        assert.ok(indices.includes(index), `missing visible row ${index} at ${top}`);
      }
    }

    const entries = releaseWindowEntries(offsets, indices);
    const reconstructedHeight = entries.reduce((height, entry) => height + (
      'index' in entry ? offsets[entry.index + 1] - offsets[entry.index] : entry.gap
    ), 0);
    assert.equal(reconstructedHeight, total);
  }
  assert.deepEqual(releaseWindowIndices([0], 0, 720, 450), []);
  assert.deepEqual(releaseWindowEntries([0], []), []);
});
