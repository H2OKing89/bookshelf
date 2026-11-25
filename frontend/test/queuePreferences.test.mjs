import assert from 'node:assert/strict';
import test from 'node:test';
import { createStore } from 'redux';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const moduleStubs = {
  'Components/Icon': () => null,
  'Helpers/Props': { icons: { SCORE: {} } },
  'Utilities/String/translate': (key) => `current:${key}`
};
const getStorage = () => {
  const values = new Map();
  return {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value)
  };
};
const apiRoot = '/books/api/v1';
const key = `bookshelf:queue-preferences:${apiRoot}`;
const { createQueuePreferencesStore } = loadFrontendModule('Activity/Queue/queuePreferences', {
  ...moduleStubs,
  'Store/Middleware/mergeColumnPreferences': loadFrontendModule('Store/Middleware/mergeColumnPreferences').default,
  'Store/Middleware/preferenceStorage': loadFrontendModule('Store/Middleware/preferenceStorage')
});
const legacy = {
  options: { includeUnknownAuthorItems: false },
  paged: {
    page: 8,
    pageSize: 30,
    sortKey: 'authors.sortName',
    sortDirection: 'descending',
    columns: [
      { name: 'status', isVisible: false },
      { name: 'progress', isVisible: false },
      { name: 'protocol', isVisible: true, label: 'Stale Label' },
      { name: 'unused', isVisible: true },
      { name: 'progress', isVisible: true },
      { name: 'authorMetadata.sortName', isVisible: true },
      null
    ]
  }
};

function assertLegacyPreferences(store) {
  const preferences = store.getSnapshot();
  const { columns, ...options } = preferences;
  assert.deepEqual(options, {
    page: 1,
    pageSize: 30,
    sortKey: 'authorMetadata.sortName',
    sortDirection: 'descending',
    includeUnknownAuthorItems: false,
    filters: { status: '', protocol: '' }
  });
  const names = columns.map(({ name }) => name);
  assert.ok(names.indexOf('progress') < names.indexOf('protocol'));
  assert.ok(names.indexOf('protocol') < names.indexOf('authorMetadata.sortName'));
  assert.equal(names.filter((name) => name === 'progress').length, 1);
  assert.equal(names.includes('unused'), false);
  assert.equal(columns.find(({ name }) => name === 'status').isVisible, true);
  assert.equal(columns.find(({ name }) => name === 'progress').isVisible, false);
  const protocol = columns.find(({ name }) => name === 'protocol');
  assert.equal(protocol.isVisible, true);
  assert.equal(protocol.label(), 'current:Protocol');
  assert.equal(columns.find(({ name }) => name === 'books.releaseDate').isVisible, false);
}

test('migrates Queue options, column order, and explicit false once per deployment', () => {
  const storage = getStorage();
  const before = structuredClone(legacy);
  const store = createQueuePreferencesStore(storage, apiRoot, legacy);
  assertLegacyPreferences(store);
  assert.deepEqual(legacy, before);
  const stored = JSON.parse(storage.getItem(key));
  assert.equal(stored.version, 1);
  assert.equal(stored.page, undefined);
  assert.equal(stored.columns.find(({ name }) => name === 'protocol').label, undefined);
  assertLegacyPreferences(createQueuePreferencesStore(storage, apiRoot, {}));
  assert.equal(createQueuePreferencesStore(storage, '/other/api/v1', {}).getSnapshot().pageSize, 20);
});

test('resets only the active page when page size or unknown-author preference changes', () => {
  const storage = getStorage();
  const store = createQueuePreferencesStore(storage, apiRoot, null);
  const changes = [];
  const detach = store.subscribe(() => changes.push(store.getSnapshot()));

  store.setPage(3);
  store.setSort('size');
  assert.equal(store.getSnapshot().sortDirection, 'ascending');
  store.setSort('size');
  assert.equal(store.getSnapshot().sortDirection, 'descending');
  store.setSort('progress');
  assert.equal(store.getSnapshot().sortDirection, 'descending');
  assert.equal(store.getSnapshot().page, 3);
  store.setTableOptions({ pageSize: 0 });
  assert.equal(store.getSnapshot().pageSize, 20);
  assert.equal(store.getSnapshot().page, 3);
  store.setIncludeUnknownAuthorItems(false);
  const filtered = changes.at(-1);
  assert.equal(filtered.includeUnknownAuthorItems, false);
  assert.equal(filtered.page, 1);
  store.setPage(2);
  store.setTableOptions({ pageSize: 40 });
  assert.equal(store.getSnapshot().page, 1);
  const beforeDetach = changes.length;
  detach();
  store.setPage(2);
  assert.equal(changes.length, beforeDetach);
  const restored = createQueuePreferencesStore(storage, apiRoot, {}).getSnapshot();
  assert.equal(restored.pageSize, 40);
  assert.equal(restored.includeUnknownAuthorItems, false);
  assert.equal(restored.sortKey, 'progress');
  assert.equal(restored.page, 1);
});

test('corrupt and partial preferences recover without erasing a future version', () => {
  const storage = getStorage();
  storage.setItem(key, '{malformed');
  assertLegacyPreferences(createQueuePreferencesStore(storage, apiRoot, legacy));

  storage.setItem(key, JSON.stringify({ version: 1, sortKey: 'unknown', pageSize: -3, columns: [null], includeUnknownAuthorItems: false }));
  const partial = createQueuePreferencesStore(storage, apiRoot, null).getSnapshot();
  assert.equal(partial.sortKey, 'timeleft');
  assert.equal(partial.pageSize, 20);
  assert.equal(partial.includeUnknownAuthorItems, false);
  assert.ok(partial.columns.some(({ name }) => name === 'protocol'));

  const future = JSON.stringify({ version: 2, bucket: 'preserve' });
  storage.setItem(key, future);
  const store = createQueuePreferencesStore(storage, apiRoot, legacy);
  assertLegacyPreferences(store);
  store.setIncludeUnknownAuthorItems(true);
  assert.equal(store.getIncludeUnknownAuthorItems(), true);
  assert.equal(storage.getItem(key), future);
});

test('the current tab still works when the browser rejects preference storage', () => {
  const blocked = () => { throw new Error('Access denied'); };
  const store = createQueuePreferencesStore({ getItem: blocked, setItem: blocked }, apiRoot, legacy);
  assertLegacyPreferences(store);
  store.setIncludeUnknownAuthorItems(true);
  assert.equal(store.getIncludeUnknownAuthorItems(), true);
});

test('saved Queue filters reset paging and reject obsolete values without losing other preferences', () => {
  const storage = getStorage();
  const store = createQueuePreferencesStore(storage, apiRoot, legacy);
  store.setPage(5);
  store.setFilters({ status: 'downloading', protocol: 'torrent' });
  assert.equal(store.getSnapshot().page, 1);

  const reloaded = createQueuePreferencesStore(storage, apiRoot, null);
  assert.deepEqual(reloaded.getSnapshot().filters, { status: 'downloading', protocol: 'torrent' });
  assert.equal(reloaded.getIncludeUnknownAuthorItems(), false);
  reloaded.setPage(2);
  reloaded.setFilters({ status: 'downloading', protocol: 'torrent' });
  assert.equal(reloaded.getSnapshot().page, 2);

  const saved = JSON.parse(storage.getItem(key));
  storage.setItem(key, JSON.stringify({ ...saved, filters: { status: 'obsolete', protocol: 'usenet' } }));
  assert.deepEqual(createQueuePreferencesStore(storage, apiRoot, null).getSnapshot().filters,
    { status: '', protocol: 'usenet' });
});

test('Redux preference startup retains legacy rollback data and avoids restoring stale Queue state', (t) => {
  const storage = getStorage();
  const before = { queue: legacy, history: { pageSize: 50 } };
  storage.setItem('readarr', JSON.stringify(before));
  const windowStub = { localStorage: storage };
  t.mock.method(storage, 'getItem');
  t.mock.method(storage, 'setItem');
  const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
  const previousStorage = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: windowStub });
  Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: storage });
  t.after(() => {
    if (previousWindow) Object.defineProperty(globalThis, 'window', previousWindow);
    else delete globalThis.window;
    if (previousStorage) Object.defineProperty(globalThis, 'localStorage', previousStorage);
    else delete globalThis.localStorage;
  });

  const initialized = [];
  const { default: createPersistState } = loadFrontendModule('Store/Middleware/createPersistState', {
    'Activity/Queue/queuePreferences': { initializeQueuePreferences: (value) => initialized.push(value) },
    'Store/Actions': [{ persistState: ['history.pageSize'] }],
    'Store/Migrators/migrate': () => {},
    'Store/StateTypes': loadFrontendModule('Store/StateTypes')
  });
  const initialState = { queue: { status: {} }, history: { pageSize: 20 } };
  const persistence = createPersistState(initialState);
  const store = createStore((state) => state, persistence.initialState);
  persistence.attach(store);
  store.dispatch({ type: 'fixture/dispatch' });
  assert.deepEqual(initialized, [legacy]);
  assert.deepEqual(store.getState(), { ...initialState, history: { pageSize: 50 } });
  assert.deepEqual(JSON.parse(storage.getItem('readarr')), before);

  storage.setItem('readarr', '{malformed');
  assert.deepEqual(createPersistState(initialState).initialState, initialState);
});
