import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

// The frontend's .js modules are bundled as ESM; package.json remains CommonJS
// for webpack. Load this dependency-free migration as ESM without a test runtime.
const source = await readFile(new URL('../src/Store/Migrators/migrateAudiobookColumns.js', import.meta.url), 'utf8');
const { default: migrate } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function legacyState() {
  return {
    books: {
      columns: [
        { name: 'select', isVisible: true },
        { name: 'monitored', isVisible: true },
        { name: 'title', isVisible: true },
        { name: 'series', isVisible: false },
        { name: 'releaseDate', isVisible: true },
        { name: 'pageCount', isVisible: true },
        { name: 'rating', isVisible: true },
        { name: 'indexerFlags', isVisible: false },
        { name: 'status', isVisible: true },
        { name: 'actions', isVisible: true }
      ]
    },
    authorDetails: { sortKey: 'releaseDate', sortDirection: 'descending' },
    bookIndex: { columns: [{ name: 'title', isVisible: true }] }
  };
}

test('upgrades the untouched legacy table without changing other saved preferences', () => {
  const state = legacyState();
  const before = structuredClone(state);
  migrate(state);
  before.books.columns.find((column) => column.name === 'pageCount').isVisible = false;
  assert.deepEqual(state, before);
  migrate(state);
  assert.deepEqual(state, before);
});

test('preserves customized visibility including Pages for ebook collections', () => {
  const state = legacyState();
  state.books.columns.find((column) => column.name === 'series').isVisible = true;
  const before = structuredClone(state);
  migrate(state);
  assert.deepEqual(state, before);
});

test('preserves custom column ordering and explicit hidden Pages', () => {
  for (const customize of [
    (columns) => columns.reverse(),
    (columns) => { columns.find((column) => column.name === 'pageCount').isVisible = false; },
    (columns) => { columns.push({ name: 'runtimeLengthMinutes', isVisible: false }); }
  ]) {
    const state = legacyState();
    customize(state.books.columns);
    const before = structuredClone(state);
    migrate(state);
    assert.deepEqual(state, before);
  }
});

test('accepts fresh, partial, and absent persisted state', () => {
  for (const state of [null, undefined, {}, { books: {} }, { books: { columns: [] } }]) {
    const before = structuredClone(state);
    migrate(state);
    assert.deepEqual(state, before);
  }
});
