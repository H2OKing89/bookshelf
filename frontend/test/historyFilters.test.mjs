import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const {
  parseHistoryBookId,
  normalizeHistoryEventTypes,
  readHistoryFilterPreference,
  saveHistoryFilterPreference
} = loadFrontendModule('Activity/History/historyFilters');

function storage() {
  const values = new Map();

  return {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value)
  };
}

test('History accepts exact Bookshelf event codes and positive 32-bit book identifiers', () => {
  assert.deepEqual(normalizeHistoryEventTypes(['10', '8', '2', '7', '9', '7', 1, '', null]), ['7', '8', '9', '10']);
  assert.equal(parseHistoryBookId('104'), 104);
  assert.equal(parseHistoryBookId(2_147_483_647), 2_147_483_647);
  for (const value of [0, -1, 1.5, 2_147_483_648, Infinity, '2.1', '1e2', '100,101', null, []]) {
    assert.equal(parseHistoryBookId(value), undefined);
  }
});

test('History saves one filter per API root and preserves deactivated reusable criteria', () => {
  const saved = storage();
  const value = { active: true, filters: { bookId: 104, eventTypes: ['1', '3'] } };
  saveHistoryFilterPreference(saved, '/bookshelf/api/v1', value);
  assert.deepEqual(readHistoryFilterPreference(saved, '/bookshelf/api/v1'), value);
  assert.deepEqual(readHistoryFilterPreference(saved, '/other/api/v1'), { active: false, filters: { eventTypes: [] } });

  saveHistoryFilterPreference(saved, '/bookshelf/api/v1', { ...value, active: false });
  assert.deepEqual(readHistoryFilterPreference(saved, '/bookshelf/api/v1'), { ...value, active: false });
});

test('History safely restores valid parts of a saved filter and falls back from malformed storage', () => {
  const saved = storage();
  const key = 'bookshelf:history-filter:/api/v1';
  saved.setItem(key, '{malformed');
  assert.deepEqual(readHistoryFilterPreference(saved, '/api/v1'), { active: false, filters: { eventTypes: [] } });
  saved.setItem(key, JSON.stringify({ version: 2, active: true, filters: { eventTypes: ['1'] } }));
  assert.deepEqual(readHistoryFilterPreference(saved, '/api/v1'), { active: false, filters: { eventTypes: [] } });
  saved.setItem(key, JSON.stringify({ version: 1, active: true, filters: { bookId: '-1', eventTypes: ['3', '13', '3'] } }));
  assert.deepEqual(readHistoryFilterPreference(saved, '/api/v1'), { active: true, filters: { bookId: undefined, eventTypes: ['3'] } });
  saved.setItem(key, JSON.stringify({ version: 1, active: true, filters: { bookId: 'bad', eventTypes: 9 } }));
  assert.deepEqual(readHistoryFilterPreference(saved, '/api/v1'), { active: false, filters: { bookId: undefined, eventTypes: [] } });
});
