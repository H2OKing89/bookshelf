import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const creator = 'Store/Actions/Creators/';
const names = [
  'createSaveProviderHandler', 'createTestProviderHandler', 'createFetchServerSideCollectionHandler',
  'createServerSideCollectionHandlers', 'createBatchToggleBookMonitoredHandler', 'createHandleActions'
];

function fixture(initial) {
  const requests = [];
  const actions = [];
  let announcements = 0;
  let state = initial;
  const modules = loadFrontendModule(names.map((name) => creator + name), {
    'Utilities/createAjaxRequest': (options) => {
      const entry = { options, aborted: false };
      const request = {
        done(callback) { entry.resolve = callback; return this; },
        fail(callback) { entry.reject = callback; return this; }
      };
      requests.push(entry);
      return { request, abortRequest() { entry.aborted = true; entry.reject({ aborted: true }); } };
    },
    'Utilities/serializeLegacyQuery': (params) => new URLSearchParams(params).toString(),
    'Components/ActionStatus': { announceSettingsSaved: () => announcements++ }
  });
  const reducers = Object.fromEntries(Object.entries(initial).map(([section, value]) => [
    section, modules[5].default({}, value, section)
  ]));
  const getState = () => state;
  function dispatch(action) {
    if (typeof action === 'function') return action(dispatch, getState);
    actions.push(action);
    if (action.meta?.batch) {
      action.payload.forEach(dispatch);
    } else {
      state = Object.fromEntries(Object.entries(reducers).map(([section, reducer]) => [
        section, reducer(state[section], action)
      ]));
    }
    return action;
  }
  const params = (index = -1) => new URL(requests.at(index).options.url, 'http://fixture.invalid').searchParams;
  return { requests, actions, getState, dispatch, params, get announcements() { return announcements; },
    save: modules[0].default, cancelSave: modules[0].createCancelSaveProviderHandler,
    testProvider: modules[1].default, cancelTest: modules[1].createCancelTestProviderHandler,
    fetch: modules[2].default, collectionHandlers: modules[3].default, monitor: modules[4].default };
}

function providerState() {
  return { items: [{ id: 7, name: 'Fixture', enabled: true, priority: 3, fields: [
    { name: 'token', value: 'owned-fixture-token' }, { name: 'size', value: 2 }, { name: 'use', value: true }, { name: 'empty', value: 'old' }
  ] }], pendingChanges: { enabled: false, priority: 0, fields: { size: 0, use: false, empty: null } }, saveError: null };
}

function patch(f, section, payload) {
  f.dispatch({ type: 'base/set', payload: { section, ...payload } });
}

test('provider save sends the existing moveFiles query and explicit zero/false/null, preserving concurrent draft edits', () => {
  const f = fixture({ settings: { a: providerState() } });
  const save = f.save('settings.a', '/author');
  save(f.getState, { id: 7, queryParams: { moveFiles: true } }, f.dispatch);
  assert.match(f.requests[0].options.url, /^\/author\/7\?/);
  assert.equal(f.requests[0].options.method, 'PUT');
  assert.equal(f.params().get('moveFiles'), 'true');
  assert.equal(f.params().has('moveFolder'), false);
  const data = JSON.parse(f.requests[0].options.data);
  assert.equal(data.enabled, false);
  assert.equal(data.priority, 0);
  assert.deepEqual(data.fields, [{ name: 'token', value: 'owned-fixture-token' }, { name: 'size', value: 0 }, { name: 'use', value: false }, { name: 'empty', value: null }]);
  patch(f, 'settings.a', { pendingChanges: { enabled: false, priority: 9, name: 'New draft', fields: { size: 0, use: true, empty: null, extra: 'later' } } });
  f.requests[0].resolve({ ...data, section: 'wrong-section' });
  const state = f.getState().settings.a;
  assert.equal(state.items[0].priority, 0);
  assert.deepEqual(state.pendingChanges, { priority: 9, name: 'New draft', fields: { use: true, extra: 'later' } });
  assert.equal(state.isSaving, false);
  assert.equal(state.saveError, null);
  assert.equal(f.announcements, 1);
});

test('provider force-save retry is scoped by operation and section; cancel clears retry and stale completions', () => {
  const f = fixture({ settings: { a: providerState(), b: providerState() } });
  const a = f.save('settings.a', '/provider'), b = f.save('settings.b', '/provider');
  const payload = { id: 7 };
  a(f.getState, payload, f.dispatch);
  const failure = { status: 400, responseJSON: [{ errorMessage: 'Fixture rejected this save' }] };
  f.requests[0].reject(failure);
  assert.equal(f.getState().settings.a.saveError, failure);
  b(f.getState, payload, f.dispatch);
  assert.equal(f.params().has('forceSave'), false);
  a(f.getState, payload, f.dispatch);
  assert.equal(f.params().get('forceSave'), 'true');
  f.cancelSave('settings.a')(f.getState, {}, f.dispatch);
  assert.equal(f.requests[2].aborted, true);
  assert.equal(f.getState().settings.a.isSaving, false);
  assert.equal(f.getState().settings.a.saveError, null);
  assert.equal(f.getState().settings.b.isSaving, true);
  f.requests[2].resolve({ id: 7, name: 'Stale' });
  assert.equal(f.getState().settings.a.items[0].name, 'Fixture');
  a(f.getState, payload, f.dispatch);
  assert.equal(f.params().has('forceSave'), false);
  f.requests[3].resolve({ id: 7, name: 'Current' });
  f.requests[1].resolve({ id: 7, name: 'Other' });
  assert.equal(f.getState().settings.a.items[0].name, 'Current');
  assert.equal(f.getState().settings.b.items[0].name, 'Other');
  assert.equal(f.announcements, 2);
});

test('provider save rejects malformed or mismatched resource replies without discarding drafts', () => {
  const f = fixture({ settings: { a: providerState() } });
  const save = f.save('settings.a', '/provider');
  const original = f.getState().settings.a.pendingChanges;
  for (const invalid of [null, [], { name: 'missing' }, { id: 9 }, [{ id: 7 }, { id: 7 }]]) {
    save(f.getState, { id: 7 }, f.dispatch);
    f.requests.at(-1).resolve(invalid);
    const state = f.getState().settings.a;
    assert.equal(state.saveError.kind, 'invalid-response');
    assert.equal(state.isSaving, false);
    assert.deepEqual(state.pendingChanges, original);
    assert.equal(state.items.length, 1);
  }
  save(f.getState, { id: 7 }, f.dispatch);
  const obsolete = f.requests.at(-1);
  save(f.getState, { id: 7 }, f.dispatch);
  assert.equal(obsolete.aborted, true);
  obsolete.resolve({ id: 7, name: 'Obsolete' });
  assert.equal(f.getState().settings.a.isSaving, true);
  f.requests.at(-1).resolve({ id: 7, name: 'Current' });
  assert.equal(f.getState().settings.a.items[0].name, 'Current');
  assert.equal(f.announcements, 1);
});

test('provider save accepts current creation and explicit stale-record bulk response without updating unrelated IDs', () => {
  const state = { selectedSchema: { id: 0, name: 'New', fields: [] }, items: [{ id: 7 }, { id: 8 }, { id: 9 }], pendingChanges: {} };
  const f = fixture({ settings: { a: state } });
  f.save('settings.a', '/provider')(f.getState, { id: 0 }, f.dispatch);
  assert.equal(f.requests[0].options.method, 'POST');
  assert.match(f.requests[0].options.url, /^\/provider\?/);
  f.requests[0].resolve({ id: 10, name: 'New' });
  const save = f.save('settings.a', '/provider', true);
  save(f.getState, { id: [7, 8] }, f.dispatch);
  assert.match(f.requests[1].options.url, /^\/provider\/7,8\?/);
  f.requests[1].resolve([{ id: 7, name: 'Remaining' }, { id: 11 }]);
  assert.equal(f.getState().settings.a.saveError.kind, 'invalid-response');
  assert.deepEqual(f.getState().settings.a.items.map((item) => item.id), [7, 8, 9, 10]);
  save(f.getState, { id: [7, 8] }, f.dispatch);
  f.requests[2].resolve([{ id: 7, name: 'Remaining' }]);
  assert.deepEqual(f.getState().settings.a.items.map((item) => item.id), [7, 9, 10]);
  for (const invalid of [[7, 7], [], [false], { id: 1 }]) {
    save(f.getState, { id: invalid }, f.dispatch);
    assert.equal(f.getState().settings.a.saveError.kind, 'invalid-request');
  }
  assert.equal(f.requests.length, 3);
});

test('provider test accepts empty success, isolates same-payload retry and ignores cancelled/obsolete errors', () => {
  const f = fixture({ settings: { a: providerState(), b: providerState() } });
  const a = f.testProvider('settings.a', '/provider'), b = f.testProvider('settings.b', '/provider');
  const payload = { id: 7 };
  a(f.getState, payload, f.dispatch);
  f.requests[0].reject({ status: 400 });
  b(f.getState, payload, f.dispatch);
  assert.equal(f.params().has('forceTest'), false);
  a(f.getState, payload, f.dispatch);
  assert.equal(f.params().get('forceTest'), 'true');
  const old = f.requests[2];
  a(f.getState, payload, f.dispatch);
  assert.equal(old.aborted, true);
  assert.equal(f.params().has('forceTest'), false);
  old.reject({ status: 500 });
  assert.equal(f.getState().settings.a.isTesting, true);
  f.cancelTest('settings.a')(f.getState, {}, f.dispatch);
  assert.equal(f.getState().settings.a.isTesting, false);
  assert.equal(f.getState().settings.a.saveError, null);
  assert.equal(f.getState().settings.b.isTesting, true);
  f.requests[1].resolve();
  assert.equal(f.getState().settings.b.isTesting, false);
  assert.equal(f.getState().settings.b.saveError, null);
  assert.deepEqual(f.getState().settings.b.pendingChanges, providerState().pendingChanges);
});

test('server page fetch preserves false/zero filters, rejects malformed pages and ignores late pages', () => {
  const f = fixture({ collection: { page: 1, pageSize: 10, sortKey: 'title', sortDirection: 'ascending', items: [], selectedFilterKey: 0,
    customFilters: [{ id: 0, filters: [{ key: 'enabled', value: false }, { key: 'count', value: 0 }, { key: 'page', value: 99 }] }] } });
  const fetchPage = f.fetch('collection', '/fixture');
  fetchPage(f.getState, { page: 2 }, f.dispatch);
  assert.deepEqual(f.requests[0].options.data, { page: 2, pageSize: 10, sortKey: 'title', sortDirection: 'ascending', enabled: false, count: 0 });
  fetchPage(f.getState, { page: 3 }, f.dispatch);
  assert.equal(f.requests[0].aborted, true);
  f.requests[0].resolve({ page: 2, pageSize: 10, totalRecords: 21, records: [{ id: 2 }] });
  assert.equal(f.getState().collection.isFetching, true);
  f.requests[1].resolve({ page: 3, pageSize: 10, totalRecords: 21, records: [{ id: 3 }], selectedFilterKey: 'injected' });
  assert.equal(f.getState().collection.page, 3);
  assert.equal(f.getState().collection.selectedFilterKey, 0);
  assert.deepEqual(f.getState().collection.items, [{ id: 3 }]);
  assert.equal(f.getState().collection.isPopulated, true);
  for (const invalid of [{ page: 0, pageSize: 10, totalRecords: 0, records: [] }, { page: 1, pageSize: 0, totalRecords: 1, records: [] }, { page: 1, pageSize: 10, totalRecords: 2, records: [{ id: 1 }, { id: 1 }] }]) {
    fetchPage(f.getState, { page: 1 }, f.dispatch);
    f.requests.at(-1).resolve(invalid);
    assert.equal(f.getState().collection.error.kind, 'invalid-response');
    assert.equal(f.getState().collection.isPopulated, false);
    assert.deepEqual(f.getState().collection.items, [{ id: 3 }]);
  }
});

test('server page navigation clamps known bounds and filters reset page while sorting keeps direction semantics', () => {
  const f = fixture({ collection: { page: 1, totalPages: 5, sortKey: 'title', sortDirection: 'ascending' } });
  const calls = [];
  const handlers = f.collectionHandlers('collection', '/fixture', (payload) => () => calls.push(payload), {
    fetch: 'fetch', firstPage: 'first', previousPage: 'previous', nextPage: 'next', lastPage: 'last', exactPage: 'exact', sort: 'sort', filter: 'filter'
  });
  handlers.previous(f.getState, {}, f.dispatch);
  handlers.last(f.getState, {}, f.dispatch);
  handlers.exact(f.getState, { page: 100 }, f.dispatch);
  handlers.exact(f.getState, { page: 0 }, f.dispatch);
  patch(f, 'collection', { page: 5 });
  handlers.next(f.getState, {}, f.dispatch);
  handlers.sort(f.getState, { sortKey: 'title' }, f.dispatch);
  assert.equal(f.getState().collection.sortDirection, 'descending');
  handlers.sort(f.getState, { sortKey: 'name' }, f.dispatch);
  assert.equal(f.getState().collection.sortDirection, 'descending');
  handlers.filter(f.getState, { selectedFilterKey: 0, section: 'unrelated' }, f.dispatch);
  assert.equal(f.getState().collection.selectedFilterKey, 0);
  assert.deepEqual(calls, [{ page: 1 }, { page: 5 }, { page: 5 }, { page: 1 }, { page: 5 }, undefined, undefined, { page: 1 }]);
});

test('batch monitoring updates live records only and overlapping requests cannot restore stale values', () => {
  const f = fixture({ books: { items: [{ id: 1, monitored: true }, { id: 2, monitored: true }, { id: 3, monitored: false }] } });
  let refreshes = 0;
  const monitor = f.monitor('books', () => () => refreshes++);
  monitor(f.getState, { bookIds: [1, 2, 1], monitored: false }, f.dispatch);
  assert.deepEqual(JSON.parse(f.requests[0].options.data), { bookIds: [1, 2], monitored: false });
  monitor(f.getState, { bookIds: [2], monitored: true }, f.dispatch);
  f.dispatch({ type: 'base/removeItem', payload: { section: 'books', id: 1 } });
  f.dispatch({ type: 'base/updateItem', payload: { section: 'books', id: 3, other: 'independent' } });
  f.requests[0].resolve();
  assert.equal(refreshes, 0);
  assert.equal(f.getState().books.items[0].isSaving, true);
  f.requests[1].resolve();
  assert.equal(refreshes, 1);
  assert.deepEqual(f.getState().books.items, [{ id: 2, monitored: true, isSaving: false }, { id: 3, monitored: false, other: 'independent' }]);
  monitor(f.getState, { bookIds: [2], monitored: false }, f.dispatch);
  f.requests[2].reject({ status: 500 });
  assert.equal(f.getState().books.items[0].isSaving, false);
  assert.equal(f.getState().books.items[0].monitored, true);
  assert.equal(refreshes, 1);
});

test('actual provider and profile settings actions validate schema selection and clone only safe independent drafts', () => {
  const [indexers, quality, naming, { default: createHandleActions }] = loadFrontendModule([
    'Store/Actions/Settings/indexers', 'Store/Actions/Settings/qualityProfiles', 'Store/Actions/Settings/naming', creator + 'createHandleActions'
  ], {
    'Utilities/createAjaxRequest': () => assert.fail('unexpected transport'),
    'Components/ActionStatus': { announceSettingsSaved() {} },
    'Helpers/Props': { sortDirections: { ASCENDING: 'ascending', DESCENDING: 'descending' } },
    'Utilities/serializeLegacyQuery': () => ''
  });
  const schema = { implementation: 'Fixture', name: 'Fixture', fields: [{ name: 'enabled', value: false }], supportsRss: false, supportsSearch: true,
    presets: [{ name: 'Preset', supportsRss: false, supportsSearch: true, fields: [{ name: 'zero', value: 0 }] }] };
  const provider = { id: 7, name: 'Provider', fields: [{ name: 'token', privacy: 'apiKey', value: 'fixture-only-secret' },
    { name: 'password', privacy: 'password', value: 'fixture-only-password' }, { name: 'public', value: [0, false] }] };
  const profile = { id: 2, name: 'Quality', items: [{ quality: { id: 4 }, allowed: false }] };
  const defaults = { indexers: { schema: [schema], items: [provider], selectedSchema: {}, pendingChanges: {} },
    qualityProfiles: { items: [profile], pendingChanges: {} }, naming: { item: { enabled: true, count: 12 }, pendingChanges: {} } };
  const reduce = createHandleActions({ ...indexers.default.reducers, ...quality.default.reducers, ...naming.default.reducers }, defaults, 'settings');
  let state = reduce(defaults, indexers.selectIndexerSchema({ implementation: 'missing' }));
  assert.equal(state, defaults);
  state = reduce(state, indexers.selectIndexerSchema({ implementation: 'Fixture', presetName: 'missing' }));
  assert.equal(state, defaults);
  state = reduce(state, indexers.selectIndexerSchema({ implementation: 'Fixture', presetName: 'Preset' }));
  assert.equal(state.indexers.selectedSchema.enableRss, false);
  assert.equal(state.indexers.selectedSchema.enableInteractiveSearch, true);
  assert.equal(schema.presets[0].enableRss, undefined);
  assert.notEqual(state.indexers.selectedSchema.fields[0], schema.presets[0].fields[0]);
  assert.equal(reduce(state, indexers.cloneIndexer({ id: 999 })), state);
  state = reduce(state, indexers.cloneIndexer({ id: 7 }));
  assert.equal(Object.hasOwn(state.indexers.selectedSchema, 'id'), false);
  assert.equal(Object.hasOwn(state.indexers.selectedSchema, 'name'), false);
  assert.deepEqual(state.indexers.selectedSchema.fields.map((field) => field.value), ['', '', [0, false]]);
  assert.notEqual(state.indexers.selectedSchema.fields[2].value, provider.fields[2].value);
  assert.equal(provider.fields[0].value, 'fixture-only-secret');
  assert.equal(state.indexers.pendingChanges.name, 'Provider - Copy');
  state = reduce(state, indexers.setIndexerFieldValue({ name: 'public', value: false }));
  assert.deepEqual(state.indexers.pendingChanges.fields, { public: false });
  state = reduce(state, quality.cloneQualityProfile({ id: 2 }));
  assert.deepEqual(state.qualityProfiles.pendingChanges, { name: 'Quality - Copy', items: [{ quality: { id: 4 }, allowed: false }] });
  assert.notEqual(state.qualityProfiles.pendingChanges.items[0].quality, profile.items[0].quality);
  const value = naming.setNamingSettingsValue({ name: 'count', value: 0, section: 'forged' });
  assert.deepEqual(value.payload, { section: 'settings.naming', name: 'count', value: 0 });
  state = reduce(state, value);
  state = reduce(state, naming.setNamingSettingsValue({ name: 'enabled', value: false }));
  assert.deepEqual(state.naming.pendingChanges, { count: 0, enabled: false });
});
