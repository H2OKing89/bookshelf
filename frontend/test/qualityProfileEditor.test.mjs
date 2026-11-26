import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { parseQualityItems, parseFormatItems, safeCutoff, moveQualityItem, updateSizeRule, nextGroupId, qualityProfileDrafts } =
  loadFrontendModule('Settings/Profiles/Quality/qualityProfileSettingsState.ts');

const leaf = (id, allowed = true) => ({
  quality: { id, name: 'Quality ' + id, source: 'synthetic' },
  allowed,
  minSize: 0,
  preferredSize: null,
  sizeUnit: 'MiB/h',
  futureField: 'preserved'
});
const group = (id, ...items) => ({ id, name: 'Group ' + id, quality: null, allowed: true, items });

test('quality profile drafts keep zero cutoff, score and size rules and reject partial malformed collections', () => {
  const profile = {
    id: { value: 0 },
    cutoff: { value: 0 },
    items: { value: [leaf(0), group(1001, leaf(2))] },
    formatItems: { value: [{ format: 3, name: 'Score', score: 0 }] }
  };
  const drafts = qualityProfileDrafts(profile);
  assert.equal(drafts.valid, true);
  assert.equal(drafts.cutoff.value, 0);
  assert.equal(drafts.items.value[0].minSize, 0);
  assert.equal(drafts.items.value[0].preferredSize, null);
  assert.equal(drafts.items.value[0].futureField, 'preserved');
  assert.equal(drafts.formatItems.value[0].score, 0);
  assert.equal(parseQualityItems([leaf(1), { quality: {} }]), null);
  assert.equal(parseFormatItems([{ format: 3, name: 'Score', score: null }])[0].score, null);
  assert.equal(parseFormatItems([{ format: 3, name: 'Score', score: {} }]), null);
  assert.equal(qualityProfileDrafts({ ...profile, items: { value: [leaf(1), {}] } }).valid, false);
});

test('quality profile cutoff remains zero when valid and follows enabled items after removal', () => {
  const items = parseQualityItems([leaf(0), group(1001, leaf(2))]);
  assert.equal(safeCutoff(items, 0), 0);
  items[0].allowed = false;
  assert.equal(safeCutoff(items, 0), 1001);
  items[1].allowed = false;
  assert.equal(safeCutoff(items, 0), null);
  assert.equal(nextGroupId(items), 1002);
});

test('quality group reorder validates stale and malformed drag indices without a partial mutation', () => {
  const items = parseQualityItems([leaf(1), leaf(2), group(1001, leaf(3))]);
  const snapshot = structuredClone(items);
  assert.equal(moveQualityItem(items, { dragQualityIndex: '4', dropQualityIndex: '2', dropPosition: 'above' }), false);
  assert.equal(moveQualityItem(items, { dragQualityIndex: '1', dropQualityIndex: 'bad', dropPosition: 'above' }), false);
  assert.deepEqual(items, snapshot);
  assert.equal(moveQualityItem(items, { dragQualityIndex: '1', dropQualityIndex: '3.1', dropPosition: 'above' }), true);
  assert.deepEqual(items.map((item) => item.quality?.id ?? item.id), [2, 1001]);
  assert.deepEqual(items[1].items.map((item) => item.quality.id), [3, 1]);
});

test('quality size rule changes preserve other rows and explicit zero', () => {
  const items = parseQualityItems([leaf(1), group(1001, leaf(2))]);
  const updated = updateSizeRule(items, 2, 'maxSize', 0);
  assert.equal(updated[0], items[0]);
  assert.equal(updated[1].items[0].maxSize, 0);
  assert.equal(updated[1].items[0].futureField, 'preserved');
  assert.equal(items[1].items[0].maxSize, undefined);
});
