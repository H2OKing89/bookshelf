import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { emptyWantedSelection, reconcileWantedSelection, selectAllWantedBooks, toggleWantedBook } =
  loadFrontendModule('Wanted/wantedSelection');

const books = (...ids) => ids.map((id) => ({ id }));

test('Wanted range selection includes only eligible books in the current row order', () => {
  const initial = reconcileWantedSelection(emptyWantedSelection, 'page 1', books(14, 10, 12, 2));
  const anchored = toggleWantedBook(initial, 14, true, false);
  const selected = toggleWantedBook(anchored, 12, true, true);

  assert.deepEqual([...selected.selectedIds], [14, 10, 12]);
  assert.deepEqual([...anchored.selectedIds], [14]);
  assert.equal(toggleWantedBook(selected, 11, true, true), selected);
  assert.equal(toggleWantedBook(selected, 12, null, false), selected);
  assert.equal(toggleWantedBook(selected, 10, true, false), selected);
  assert.equal(toggleWantedBook(selected, 14, false, true).selectedIds.size, 0);
});

test('Wanted preserves surviving IDs across confirmed sort and filter changes without preserving an old anchor', () => {
  const initial = reconcileWantedSelection(emptyWantedSelection, 'first sort', books(14, 10, 12, 2));
  const selected = toggleWantedBook(toggleWantedBook(initial, 10, true, false), 14, true, false);

  const sorted = reconcileWantedSelection(selected, 'second sort', books(1, 2, 10, 12));
  assert.deepEqual([...sorted.selectedIds], [10]);
  assert.equal(sorted.anchor, null);
  assert.deepEqual([...toggleWantedBook(sorted, 1, true, true).selectedIds], [10, 1]);

  const filtered = reconcileWantedSelection(sorted, 'filtered', books(12, 1));
  assert.equal(filtered.selectedIds.size, 0);
  assert.equal(reconcileWantedSelection(filtered, 'second sort', books(1, 2, 10, 12)).selectedIds.size, 0);
});

test('Wanted ignores placeholder rows and keeps selection on a refresh with unchanged eligible IDs', () => {
  const initial = reconcileWantedSelection(emptyWantedSelection, 'page 1', books(14, 10, 12));
  const selected = toggleWantedBook(initial, 10, true, false);

  assert.equal(reconcileWantedSelection(selected, null, books(31, 33)), selected);
  assert.equal(reconcileWantedSelection(selected, 'page 1', books(14, 10, 12)), selected);
  const changed = reconcileWantedSelection(selected, 'page 1', books(14, 8, 12));
  assert.equal(changed.anchor, null);
  assert.deepEqual([...toggleWantedBook(changed, 12, true, true).selectedIds], [12]);
});

test('Wanted Select All applies to the confirmed page only and resets the Shift anchor', () => {
  const initial = reconcileWantedSelection(emptyWantedSelection, 'page 1', books(14, 10, 12));
  const selected = selectAllWantedBooks(toggleWantedBook(initial, 14, true, false), true);
  assert.deepEqual([...selected.selectedIds], [14, 10, 12]);
  assert.equal(selected.anchor, null);

  const next = reconcileWantedSelection(selected, 'page 2', books(5, 6));
  assert.equal(next.selectedIds.size, 0);
  assert.deepEqual([...selectAllWantedBooks(next, true).selectedIds], [5, 6]);
  assert.equal(selectAllWantedBooks(selected, false).selectedIds.size, 0);
});
