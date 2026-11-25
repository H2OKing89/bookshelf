import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { scrollVirtualizedWindow, scrollVirtualizedWindowToRow } = loadFrontendModule('Utilities/Scroll/scrollVirtualizedWindow');

test('programmatic Grid jumps scroll the WindowScroller rather than its controlled Grid state', () => {
  const requests = [];
  const offsets = [];
  const grid = {
    getOffsetForCell(options) {
      offsets.push(options);
      return { scrollTop: options.rowIndex * 38, scrollLeft: 500 };
    }
  };
  const scroll = (position) => requests.push(position);
  scrollVirtualizedWindowToRow(grid, scroll, 519);
  scrollVirtualizedWindowToRow(grid, scroll, 0);
  scrollVirtualizedWindow(scroll, 800);
  assert.deepEqual(offsets, [
    { rowIndex: 519, columnIndex: 0, alignment: 'start' },
    { rowIndex: 0, columnIndex: 0, alignment: 'start' }
  ]);
  assert.deepEqual(requests, [{ scrollTop: 19722 }, { scrollTop: 0 }, { scrollTop: 800 }]);
});

test('unmounted scrollers and invalid grid rows/offsets cannot trigger navigation', () => {
  const requests = [];
  const scroll = (position) => requests.push(position);
  const grid = { getOffsetForCell: () => ({ scrollTop: NaN }) };
  scrollVirtualizedWindowToRow(grid, null, 1);
  scrollVirtualizedWindowToRow(null, scroll, 1);
  scrollVirtualizedWindowToRow(grid, scroll, -1);
  scrollVirtualizedWindowToRow(grid, scroll, 1.5);
  scrollVirtualizedWindowToRow(grid, scroll, 1);
  for (const value of [-1, Infinity, NaN, undefined, null]) {
    scrollVirtualizedWindow(scroll, value);
  }
  assert.deepEqual(requests, []);
});
