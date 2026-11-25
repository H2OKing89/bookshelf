import assert from 'node:assert/strict';
import test from 'node:test';
import { batchActions } from 'redux-batched-actions';
import { createStore } from 'redux';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const [baseActions, { default: createHandleActions }, stateTypes] = loadFrontendModule([
  'Store/Actions/baseActions',
  'Store/Actions/Creators/createHandleActions',
  'Store/StateTypes'
]);
const {
  set, update, updateItem, removeItem, updateServerSideCollection, clearPendingChanges
} = baseActions;

test('base reducers preserve section isolation, immutable updates, zero, false and unresolved IDs', () => {
  const reducer = createHandleActions({}, { items: [], itemMap: {}, nested: { items: [], pendingChanges: { size: 0 }, saveError: {} } }, 'fixture');
  let state = reducer(undefined, { type: '@@init' });
  assert.equal(reducer(state, update({ section: 'elsewhere', data: [] })), state);
  state = reducer(state, update({ section: 'fixture', data: [{ id: 4, value: false }, { id: -2, value: 0 }] }));
  assert.deepEqual(state.itemMap, { 4: 0, '-2': 1 });
  assert.equal(reducer(state, updateItem({ section: 'fixture', id: 4, value: false })), state);
  assert.equal(reducer(state, updateItem({ section: 'fixture', id: 5, updateOnly: true })), state);

  // A stale map may not update the wrong book/author.
  state = { ...state, itemMap: { 4: 1, '-2': 0 } };
  const changed = reducer(state, updateItem({ section: 'fixture', id: 4, value: 0 }));
  assert.deepEqual(changed.items, [{ id: 4, value: 0 }, { id: -2, value: 0 }]);
  assert.deepEqual(changed.itemMap, { 4: 0, '-2': 1 });
  assert.equal(state.items[0].value, false);

  const appended = reducer(changed, updateItem({ section: 'fixture', id: 'new-item', value: null }));
  assert.equal(appended.itemMap['new-item'], 2);
  const removed = reducer(appended, removeItem({ section: 'fixture', id: 4 }));
  assert.deepEqual(removed.itemMap, { '-2': 0, 'new-item': 1 });

  const nested = reducer(removed, set({ section: 'fixture.nested', isFetching: false, count: 0 }));
  assert.equal(nested.nested.isFetching, false);
  assert.equal(nested.nested.count, 0);
  assert.equal(nested.items, removed.items);
  const cleared = reducer(nested, clearPendingChanges({ section: 'fixture.nested' }));
  assert.deepEqual(cleared.nested.pendingChanges, {});
  assert.equal(cleared.nested.saveError, null);

  const page = reducer(cleared, updateServerSideCollection({
    section: 'fixture.nested', data: { page: 1, pageSize: 20, totalRecords: 0, records: [] }
  }));
  assert.equal(page.nested.totalPages, 1);
  assert.equal(page.nested.totalRecords, 0);
  assert.deepEqual(page.nested.items, []);
  assert.equal(reducer(page, updateServerSideCollection({
    section: 'fixture.nested', data: { pageSize: 0, records: [], totalRecords: 10 }
  })), page);
  assert.deepEqual(stateTypes.createItemMap([{ id: 0 }, {}, { id: 'local' }]), { 0: 0, local: 2 });
});

test('the actual registered Redux store executes thunks and multi-action batches atomically', (t) => {
  const defaults = { items: [], itemMap: {} };
  const registrations = [{ section: 'fixture', defaultState: defaults, reducers: createHandleActions({}, defaults, 'fixture') }];
  const preferences = new Map();
  const windowBefore = Object.getOwnPropertyDescriptor(globalThis, 'window');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    localStorage: { getItem: (key) => preferences.get(key) ?? null, setItem: (key, value) => preferences.set(key, value) }
  } });
  t.after(() => {
    if (windowBefore) Object.defineProperty(globalThis, 'window', windowBefore);
    else delete globalThis.window;
  });

  const [{ default: createAppStore }, { createThunk, handleThunks }] = loadFrontendModule([
    'Store/createAppStore',
    'Store/thunks'
  ], {
    'Store/Actions': registrations,
    'Activity/Queue/queuePreferences': { initializeQueuePreferences: () => {} },
    'Store/Migrators/migrate': () => {}
  });
  const store = createAppStore();
  const received = [];
  const unsubscribe = store.subscribe(() => received.push(store.getState()));
  const fetch = createThunk('fixture/fetch', (payload) => ({ ...payload, normalized: true }));
  handleThunks({
    'fixture/fetch': (getState, payload, dispatch) => {
      assert.equal(getState(), store.getState());
      assert.deepEqual(payload, { value: false, normalized: true });
      return dispatch(batchActions([
        set({ section: 'fixture', isFetching: true }),
        batchActions([
          update({ section: 'fixture', data: [{ id: 4, value: payload.value }] }),
          set({ section: 'fixture', isFetching: false })
        ])
      ], 'fixture/customBatch'));
    }
  });
  assert.equal(store.dispatch(fetch({ value: false })).type, 'fixture/customBatch');
  assert.equal(received.length, 1);
  assert.deepEqual(store.getState().fixture, { items: [{ id: 4, value: false }], itemMap: { 4: 0 }, isFetching: false });
  assert.throws(() => store.dispatch(createThunk('fixture/missing')()), /not been registered/);
  unsubscribe();
});

test('legacy preference hydration restores whitelisted drafts only, preserving column getters and zero/false', (t) => {
  const legacyQueue = { paged: { page: 12, sortKey: 'title' } };
  const customFilter = { id: 7, label: 'Fixture', type: 'authors', filters: [{ key: 'count', value: 0, type: 'equal' }] };
  const old = {
    bookshelf: { customFilters: [customFilter, null, { id: 8, label: 'Old', type: 'authors', filters: [{ key: 'count', type: 'retired' }] }] },
    history: { pageSize: 0, selectedFilterKey: 7, columns: [
      { name: 'title', label: 'Obsolete', isVisible: false }, { name: 'unused', isVisible: true }
    ], items: [{ id: 999 }] },
    settings: { advancedSettings: false, unexpected: 'not persisted' },
    search: { authorDefaults: { qualityProfileId: 0, rootFolderPath: '/fixture', unexpected: 'ignored', tags: [2] } },
    queue: legacyQueue
  };
  const storage = new Map([['readarr', JSON.stringify(old)]]);
  const windowBefore = Object.getOwnPropertyDescriptor(globalThis, 'window');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    localStorage: { getItem: (key) => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) }
  } });
  t.after(() => {
    if (windowBefore) Object.defineProperty(globalThis, 'window', windowBefore);
    else delete globalThis.window;
  });
  const initialized = [];
  const { default: prepare } = loadFrontendModule('Store/Middleware/createPersistState', {
    'Store/Actions': [
      { persistState: ['history.pageSize', 'history.selectedFilterKey', 'history.columns'] },
      { persistState: ['settings.advancedSettings', 'search.authorDefaults', 'bookshelf.customFilters'] }
    ],
    'Activity/Queue/queuePreferences': { initializeQueuePreferences: (value) => initialized.push(value) },
    'Store/Migrators/migrate': () => {}
  });
  let label = 'Current';
  const defaults = {
    history: { pageSize: 20, selectedFilterKey: 'all', columns: [{ name: 'title', isVisible: true, get label() { return label; } }], items: [] },
    settings: { advancedSettings: true },
    bookshelf: {},
    search: { authorDefaults: { qualityProfileId: 5, rootFolderPath: '', tags: [] } },
    queue: { status: {} }
  };
  const prepared = prepare(defaults);
  const store = createStore((state = defaults) => state, prepared.initialState);
  const detach = prepared.attach(store);
  const hydrated = store.getState();
  assert.equal(hydrated.history.pageSize, 0);
  assert.equal(hydrated.history.selectedFilterKey, 7);
  assert.deepEqual(hydrated.history.items, []);
  assert.equal(hydrated.history.columns[0].isVisible, false);
  assert.equal(hydrated.history.columns[0].label, 'Current');
  label = 'New translation';
  assert.equal(hydrated.history.columns[0].label, 'New translation');
  assert.equal(defaults.history.columns[0].isVisible, true);
  assert.equal(hydrated.settings.advancedSettings, false);
  assert.deepEqual(hydrated.bookshelf.customFilters, [customFilter]);
  assert.equal(defaults.bookshelf.customFilters, undefined);
  assert.deepEqual(hydrated.search.authorDefaults, { qualityProfileId: 0, rootFolderPath: '/fixture', tags: [2] });
  assert.deepEqual(hydrated.queue, defaults.queue);
  assert.deepEqual(initialized, [legacyQueue]);
  store.dispatch({ type: 'fixture/persist' });
  const written = JSON.parse(storage.get('readarr'));
  assert.deepEqual(written.queue, legacyQueue);
  assert.deepEqual(written.bookshelf.customFilters, [customFilter]);
  assert.equal(written.history.items, undefined);
  assert.equal(written.settings.unexpected, undefined);
  detach();

  storage.set('readarr', JSON.stringify({ history: { selectedFilterKey: null, columns: {}, pageSize: 'bad' } }));
  const malformed = prepare(defaults).initialState;
  assert.equal(malformed.history.pageSize, 20);
  assert.equal(malformed.history.selectedFilterKey, null);
  assert.equal(malformed.history.columns, defaults.history.columns);
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    get localStorage() { throw new Error('Storage denied'); }
  } });
  assert.deepEqual(prepare(defaults).initialState, defaults);
});

test('request boundary handles successful, rejected, cancelled, and mismatched-resource responses', () => {
  const dispatched = [];
  const options = [];
  let resolve, reject, aborted = false;
  const { default: handler } = loadFrontendModule('Store/Actions/Creators/createFetchHandler', {
    'Utilities/createAjaxRequest': (value) => {
      options.push(value);
      return {
        request: { done: (callback) => { resolve = callback; }, fail: (callback) => { reject = callback; } },
        abortRequest: () => { aborted = true; reject({ aborted: true }); }
      };
    }
  });
  const perform = handler('fixture', '/fixture');
  const dispatch = (action) => dispatched.push(action);
  const cancel = perform(() => ({}), { id: 7, selected: false }, dispatch);
  assert.deepEqual(options.at(-1), { url: '/fixture/7', data: { selected: false }, traditional: true });
  resolve({ id: 7, count: 0 });
  assert.deepEqual(dispatched.at(-1).payload[0].payload, { section: 'fixture', id: 7, count: 0 });
  assert.equal(dispatched.at(-1).payload[1].payload.isPopulated, true);
  perform(() => ({}), { id: 8 }, dispatch);
  resolve({ id: 7 });
  assert.equal(dispatched.at(-1).payload.error.kind, 'invalid-response');
  assert.equal(dispatched.at(-1).payload.isFetching, false);
  perform(() => ({}), {}, dispatch);
  const failure = { status: 500 };
  reject(failure);
  assert.equal(dispatched.at(-1).payload.error, failure);
  cancel();
  assert.equal(aborted, true);
  assert.equal(dispatched.at(-1).payload.error, null);
});

test('typed ID selectors, filter values and scroll state avoid stale matches and falsy-value loss', () => {
  const [
    { default: bookSelector }, { default: authorSelector }, { default: fileSelector },
    { default: findSelectedFilters }, { default: getFilterValue }, { default: predicates }, scroll
  ] = loadFrontendModule([
    'Store/Selectors/createBookSelector',
    'Store/Selectors/createAuthorSelector',
    'Store/Selectors/createBookFileSelector',
    'Utilities/Filter/findSelectedFilters',
    'Utilities/Filter/getFilterValue',
    'Helpers/Props/filterTypePredicates',
    'Store/scrollPositions'
  ]);
  const author = { id: 1, name: 'author' };
  const book = { id: 1, title: 'book' };
  const file = { id: 3, bookId: 0 };
  const state = {
    authors: { items: [author], itemMap: { 1: 9 } },
    books: { items: [book, { id: 2 }], itemMap: { 1: 1 } },
    bookFiles: { items: [file] }
  };
  assert.equal(bookSelector()(state, { bookId: 1 }), book);
  assert.equal(authorSelector()(state, { authorId: 1 }), author);
  assert.equal(fileSelector()(state, { bookFileId: 3 }), file);
  assert.equal(bookSelector()(state, { bookId: 0 }), undefined);
  assert.equal(authorSelector()(state, { authorId: null }), undefined);

  const filters = [{ key: 'preset', filters: [
    { key: 'monitored', value: false }, { key: 'count', value: 0 }, { key: 'optional', value: null }
  ] }];
  assert.equal(findSelectedFilters('preset', filters), filters[0].filters);
  assert.deepEqual(findSelectedFilters(7, filters, [{ id: 7, filters: [{ key: 'custom' }] }]), [{ key: 'custom' }]);
  assert.deepEqual(findSelectedFilters('missing', filters), []);
  assert.deepEqual(findSelectedFilters(7, null, [null, { id: 7, filters: [null] }]), []);
  assert.deepEqual(findSelectedFilters(7, undefined, [{ id: 7, filters: {} }]), []);
  assert.equal(getFilterValue(filters, 'preset', 'monitored', true), false);
  assert.equal(getFilterValue(filters, 'preset', 'count', 12), 0);
  assert.equal(getFilterValue(filters, 'preset', 'optional', 12), null);
  assert.equal(getFilterValue(filters, 'preset', 'missing', 12), 12);
  assert.equal(getFilterValue([null, { key: 'preset', filters: [null] }], 'preset', 'missing', 12), 12);
  assert.equal(predicates.contains('MiXeD Name', 'mixed'), true);
  assert.equal(predicates.contains([0, false], false), true);
  assert.equal(predicates.notContains([0, false], 0), false);
  assert.equal(predicates.notContains(undefined, 'x'), false);
  assert.equal(predicates.greaterThan(4, '3'), true);

  const { default: filterCollection } = loadFrontendModule('Utilities/Array/filterCollection', {
    'Helpers/Props': {
      filterTypePredicates: predicates,
      filterTypes: loadFrontendModule('Helpers/Props/filterTypes')
    }
  });
  const items = [{ title: 'Example' }];
  assert.deepEqual(filterCollection(items, {
    selectedFilterKey: 'old',
    filters: [{ key: 'old', filters: [{ key: 'title', value: 'example', type: 'retired' }] }]
  }), []);
  assert.deepEqual(filterCollection(items, {
    selectedFilterKey: 'old',
    filters: [{ key: 'old', filters: [{ key: 'title', value: 'example', type: 'inNext' }] }]
  }), []);
  scroll.saveScrollPosition('authorIndex', 250);
  scroll.saveScrollPosition('bookIndex', 450);
  scroll.saveScrollPosition('authorIndex', NaN);
  scroll.saveScrollPosition('bookIndex', -1);
  assert.equal(scroll.getScrollPosition('authorIndex'), 250);
  assert.equal(scroll.getScrollPosition('bookIndex'), 450);
  scroll.saveScrollPosition('authorIndex', 0);
  assert.equal(scroll.getScrollPosition('authorIndex'), 0);
});

test('settings draft reducers preserve explicit null, false and zero and provider field names', () => {
  const [
    { default: settingReducer }, { default: fieldReducer }, { default: getProviderState }
  ] = loadFrontendModule([
    'Store/Actions/Creators/Reducers/createSetSettingValueReducer',
    'Store/Actions/Creators/Reducers/createSetProviderFieldValueReducer',
    'Utilities/State/getProviderState'
  ]);
  const current = { item: { enabled: true, limit: 20 }, pendingChanges: {} };
  const reduce = settingReducer('settings.fixture');
  let state = { fixture: current };
  state = reduce(state, { payload: { section: 'settings.fixture', name: 'enabled', value: false } });
  state = reduce(state, { payload: { section: 'settings.fixture', name: 'limit', value: '0' } });
  assert.deepEqual(state.fixture.pendingChanges, { enabled: false, limit: 0 });
  state = reduce(state, { payload: { section: 'settings.fixture', name: 'limit', value: null } });
  assert.equal(state.fixture.pendingChanges.limit, null);
  state = reduce(state, { payload: { section: 'settings.fixture', name: 'limit', value: '20' } });
  assert.equal(Object.hasOwn(state.fixture.pendingChanges, 'limit'), false);
  assert.deepEqual(current.pendingChanges, {});

  const provider = { id: 2, fields: [{ name: 'active', value: true }, { name: 'count', value: 5 }], presets: [] };
  const initial = { fixture: { items: [provider], pendingChanges: {} } };
  const field = fieldReducer('settings.fixture');
  let draft = field(initial, { payload: { section: 'settings.fixture', name: 'active', value: false } });
  draft = field(draft, { payload: { section: 'settings.fixture', name: 'count', value: 0 } });
  assert.deepEqual(getProviderState({ id: 2 }, () => ({ settings: draft }), 'settings.fixture'), {
    id: 2, fields: [{ name: 'active', value: false }, { name: 'count', value: 0 }]
  });
});
