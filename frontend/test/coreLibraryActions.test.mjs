import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function io() {
  const requests = [];
  return { requests, createAjaxRequest(options) {
    const entry = { options };
    requests.push(entry);
    return {
      request: {
        done(callback) { entry.resolve = callback; return this; },
        fail(callback) { entry.reject = callback; return this; }
      },
      abortRequest() { entry.aborted = true; entry.reject?.({ aborted: true }); }
    };
  } };
}

test('library identity boundaries reject duplicate and unrelated canonical IDs', () => {
  const { libraryIds, parseExactRows, bookSection, namedOptions, matchesFilter } = loadFrontendModule('Store/Actions/Creators/libraryActionsState', { 'Utilities/createAjaxRequest': io().createAjaxRequest });
  assert.deepEqual(libraryIds([3, 2, 3]), [3, 2]);
  for (const ids of [[], [0], [1, '2'], [1.2], [null]]) assert.equal(libraryIds(ids), null);
  const parse = value => value;
  assert.deepEqual(parseExactRows([{ id: 3 }, { id: 2 }], [2, 3], parse), [{ id: 3 }, { id: 2 }]);
  assert.throws(() => parseExactRows([{ id: 3 }, { id: 3 }], [2, 3], parse));
  assert.throws(() => parseExactRows([{ id: 7 }], [2], parse));
  assert.equal(bookSection('wanted.missing'), 'wanted.missing');
  assert.equal(bookSection('unrelated'), null);
  assert.deepEqual(namedOptions([{ genres: ['Audio', null, 'Audio'] }], 'genres', true), [{ id: 'Audio', name: 'Audio' }]);
  assert.equal(matchesFilter('unsupported', 5, 5), false);
});

test('bulk library editor validates response identities before patching, preserving false values', () => {
  const transport = io();
  const { createLibraryBulkEditor } = loadFrontendModule('Store/Actions/Creators/libraryActionsState', { 'Utilities/createAjaxRequest': transport.createAjaxRequest });
  const editor = createLibraryBulkEditor({ section: 'bookIndex', target: 'books', url: '/book/editor', idKey: 'bookIds', parse: value => value });
  const actions = [];
  editor.save(() => ({}), { bookIds: [1, 2], monitored: false }, action => actions.push(action));
  assert.deepEqual(JSON.parse(transport.requests[0].options.data), { bookIds: [1, 2], monitored: false });
  transport.requests[0].resolve([{ id: 1, monitored: false }, { id: 99, monitored: false }]);
  assert.equal(actions.at(-1).payload.saveError.kind, 'invalid-response');
  editor.save(() => ({}), { bookIds: [1, 2], monitored: false }, action => actions.push(action));
  transport.requests[1].resolve([{ id: 1, monitored: false }, { id: 2, monitored: false }]);
  assert.deepEqual(actions.at(-1).payload[0].payload, { id: 1, monitored: false, section: 'books', updateOnly: true });
});

test('manual preview edits never replace the wrong last row and absent folder removal is a no-op', () => {
  const mod = loadFrontendModule('Store/Actions/interactiveImportActions', { 'Utilities/createAjaxRequest': io().createAjaxRequest, 'Helpers/Props': { sortDirections: { ASCENDING: 'ascending', DESCENDING: 'descending' } } });
  const rows = [{ id: 1, path: '/owned/a.m4b' }, { id: 2, path: '/owned/b.m4b' }];
  const initial = { ...mod.defaultState, items: rows, recentFolders: [{ folder: '/owned' }] };
  const missing = mod.reducers(initial, mod.updateInteractiveImportItem({ id: 99, replaceExistingFiles: false }));
  assert.equal(missing, initial);
  const updated = mod.reducers(initial, mod.updateInteractiveImportItems({ ids: [2, 99], additionalFile: false }));
  assert.deepEqual(updated.items, [rows[0], { ...rows[1], additionalFile: false }]);
  assert.deepEqual(updated.itemMap, { 1: 0, 2: 1 });
  const folders = mod.reducers(initial, mod.removeRecentFolder({ folder: '/absent' }));
  assert.deepEqual(folders.recentFolders, initial.recentFolders);
});

test('manual preview supports selection key and rejects stale reprocessing replies after a newer fetch', () => {
  const transport = io();
  const [mod, thunks] = loadFrontendModule(['Store/Actions/interactiveImportActions', 'Store/thunks'], { 'Utilities/createAjaxRequest': transport.createAjaxRequest, 'Helpers/Props': { sortDirections: { ASCENDING: 'ascending', DESCENDING: 'descending' } } });
  const rows = [{ id: 1, path: '/owned/a.m4b', author: { id: 7 }, book: { id: 8 }, additionalFile: false, replaceExistingFiles: false, releaseGroupOverride: '' }];
  const getState = () => ({ interactiveImport: { items: rows } });
  const actions = [];
  const dispatch = action => actions.push(action);
  mod.saveInteractiveImportItem({ id: [1] })(dispatch, getState);
  const request = transport.requests[0];
  assert.deepEqual(JSON.parse(request.options.data), [{ id: 1, path: '/owned/a.m4b', authorId: 7, bookId: 8, releaseGroup: '', additionalFile: false, replaceExistingFiles: false }]);
  mod.fetchInteractiveImportItems({ folder: '/owned/new' })(dispatch, getState);
  assert.equal(request.aborted, true);
  const count = actions.length;
  request.resolve(rows);
  assert.equal(actions.length, count);
  mod.clearInteractiveImport()(dispatch, getState);
  assert.equal(transport.requests[1].aborted, true);
  assert.equal(actions.at(-1).type, mod.CLEAR_INTERACTIVE_IMPORT);
  assert.equal(typeof thunks.handleThunks, 'function');
});

test('search add resources are built without mutating lookup records or losing explicit false', () => {
  const [author, book] = loadFrontendModule(['Utilities/Author/getNewAuthor', 'Utilities/Book/getNewBook']);
  const source = Object.freeze({ id: 0, foreignAuthorId: 'owned:author' });
  const sourceBook = Object.freeze({ id: 0, foreignBookId: 'audible:us:owned', author: source });
  const options = { rootFolderPath: '/owned', monitor: 'specificBook', searchForMissingBooks: false, searchForNewBook: false, tags: [] };
  const added = book.default(sourceBook, options);
  assert.equal(added.addOptions.searchForNewBook, false);
  assert.deepEqual(added.author.addOptions, { searchForMissingBooks: false, booksToMonitor: ['audible:us:owned'] });
  assert.equal(Object.hasOwn(source, 'addOptions'), false);
  assert.equal(author.default(source, options).addOptions.searchForMissingBooks, false);
});
