import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { numericTagProjection, textTags, removeTag } = loadFrontendModule('Components/Form/tagInputState.ts');

test('tags keep zero and not-yet-loaded IDs without changing the draft', () => {
  const saved = [5, 0, 2];
  const projected = numericTagProjection(saved, [{ id: 0, label: 'Zero' }, { id: 2, label: 'Two' }], 'store');
  assert.deepEqual(projected.value, saved);
  assert.deepEqual(projected.tags, [{ id: 5, name: 'Unknown (5)' }, { id: 0, name: 'Zero' }, { id: 2, name: 'Two' }]);
  assert.deepEqual(removeTag(projected.value, { id: 0, index: 1 }), [5, 2]);
  assert.deepEqual(saved, [5, 0, 2]);
  assert.deepEqual(numericTagProjection([0], [{ key: 0, value: 'Any' }], 'option').tags, [{ id: 0, name: 'Any' }]);
});

test('text tags support comma-separated values, URLs and null without index drift', () => {
  assert.deepEqual(textTags(null), []);
  assert.deepEqual(textTags('first,second'), ['first', 'second']);
  assert.deepEqual(textTags(['first', null, '/path', '']), ['first', '/path']);
  assert.deepEqual(removeTag(['first', '/path'], { index: 1, id: '/path' }), ['first']);
});
