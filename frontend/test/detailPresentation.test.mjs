import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { bookHeaderData, detailStatistics } = loadFrontendModule('Book/Details/detailPresentation.ts');
const { detailBookRows, monitorRange, sortSeriesBooks } = loadFrontendModule('Author/Details/detailRowsState.ts');
const { parseReconciliationPreview } = loadFrontendModule('Author/Details/reconciliationState.ts');

test('detail header and rows preserve explicit zero, false and absent metadata', () => {
  const data = bookHeaderData({ monitored: false, pageCount: 0, runtimeLengthMinutes: null, ratings: { value: 0 } });
  assert.equal(data.monitored, false);
  assert.equal(data.pageCount, 0);
  assert.equal(data.runtimeLengthMinutes, null);
  assert.equal(data.ratings.value, 0);
  assert.equal(detailStatistics({ bookFileCount: 0 }).bookFileCount, 0);
  assert.equal(detailStatistics({}).bookFileCount, undefined);
  const rows = detailBookRows([{ id: 1, authorId: 3, monitored: false, pageCount: 0 }, { id: '1' }], 3);
  assert.equal(rows.length, 1);
  assert.equal(rows[0].pageCount, 0);
  assert.equal(rows[0].authorId, 3);
  assert.equal(rows[0].monitored, false);
});

test('series positions sort numerically without mutating rows and stale monitoring anchors are ignored', () => {
  const rows = detailBookRows([{id: 1}, {id: 2}, {id: 3}], 4);
  assert.deepEqual(sortSeriesBooks(rows, {1: '10', 2: '1', 3: '2'}, 'position', 'ascending').map((row) => row.id), [2, 3, 1]);
  assert.deepEqual(rows.map((row) => row.id), [1, 2, 3]);
  assert.deepEqual(monitorRange(rows, 3, 9, true), [3]);
  assert.deepEqual(monitorRange(rows, 3, 1, true).sort(), [1, 2, 3]);
});

test('author reconciliation parses reviewed identities and preserves false and zero profile values', () => {
  const source = {id: 1, name: 'Source', foreignAuthorId: 'name:source', titleSlug: 'source',
    qualityProfileId: 0, metadataProfileId: 0, monitored: false, monitorNewItems: 'none', tags: []};
  const target = {...source, id: 2, titleSlug: 'target'};
  const preview = {source, target, previewToken: 'reviewed', canApply: false,
    recordings: [], files: [], references: [{ table: 'books', count: 0 }], blockers: [], settingsDifferences: []};
  const parsed = parseReconciliationPreview(preview);
  assert.equal(parsed.source.monitored, false);
  assert.equal(parsed.target.qualityProfileId, 0);
  assert.equal(parsed.references[0].count, 0);
  assert.equal(parsed.canApply, false);
  assert.throws(() => parseReconciliationPreview({...preview, previewToken: ''}), /identity/);
  assert.throws(() => parseReconciliationPreview({...preview, target: source}), /identity/);
  assert.throws(() => parseReconciliationPreview({...preview, files: null}), /files/);
});

test('book detail connector handles missing route snapshots and keeps actions scoped to its displayed recording', () => {
  const calls = [];
  const action = (name) => (...args) => { calls.push([name, ...args]); };
  const { default: connected } = loadFrontendModule('Book/Details/BookDetailsConnector.tsx', {
    'react-redux': { connect: (selector, actions) => (Component) => ({ selector: selector(), actions, Component }) },
    'Commands/commandNames': {
      REFRESH_BOOK: 'RefreshBook', REFRESH_MEDIA_INFO: 'RefreshMediaInfo',
      BOOK_SEARCH: 'BookSearch', RENAME_FILES: 'RenameFiles', RENAME_AUTHOR: 'RenameAuthor'
    },
    'InteractiveSearch/releaseSearchQuery': { invalidateReleaseSearches: action('invalidate') },
    'Store/Actions/baseActions': { updateItem: action('updateItem') },
    'Store/Actions/bookFileActions': { fetchBookFiles: action('fetchBookFiles'), clearBookFiles: action('clearBookFiles') },
    'Store/Actions/commandActions': { executeCommand: action('executeCommand') },
    'Store/Actions/editionActions': { fetchEditions: action('fetchEditions'), clearEditions: action('clearEditions') },
    'Utilities/Command': loadFrontendModule('Utilities/Command/index.ts'),
    'Utilities/pagePopulator': { registerPagePopulator() {}, unregisterPagePopulator() {} },
    './BookDetails': () => null
  });
  const state = {
    authors: { items: [{ id: 3, titleSlug: 'author', authorName: 'Author' }] },
    books: { items: [
      { id: 1, authorId: 3, title: 'First', titleSlug: 'first', releaseDate: '2020-01-01' },
      { id: 2, authorId: 3, title: 'Second', titleSlug: 'second', releaseDate: '2021-01-01' },
      { id: 9, authorId: 4, title: 'Other', titleSlug: 'other', releaseDate: '2022-01-01' }
    ] },
    bookFiles: { items: [], isPopulated: true },
    editions: { items: [], isPopulated: true },
    commands: { items: [] }
  };
  assert.deepEqual(connected.selector(state, { titleSlug: 'missing' }), { detail: null });
  assert.deepEqual(connected.selector(state, { titleSlug: 'other' }), { detail: null });
  const detail = connected.selector(state, { titleSlug: 'first' }).detail;
  assert.equal(detail.id, 1);
  assert.equal(detail.previousBook.id, 2);
  assert.equal(detail.nextBook.id, 2);
  const component = new connected.Component({ detail, ...connected.actions });
  component.populate();
  component.onLocalMetadataSaved({ id: 2, title: 'Stale update' });
  assert.deepEqual(calls, [['fetchBookFiles', { bookId: 1 }], ['fetchEditions', { bookId: 1 }]]);
  component.onLocalMetadataSaved({ id: 1, title: 'Updated title' });
  assert.deepEqual(calls.slice(2), [
    ['updateItem', { id: 1, title: 'Updated title', section: 'books' }],
    ['fetchEditions', { bookId: 1 }], ['invalidate', { bookId: 1 }]
  ]);
});
