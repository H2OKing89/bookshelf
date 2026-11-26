import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function transport() {
  const entries = [];
  function issue(options) {
    const entry = { options, aborted: false };
    entries.push(entry);
    const request = {
      done(callback) { entry.resolve = callback; return this; },
      fail(callback) { entry.reject = callback; return this; }
    };
    return { request, abortRequest() { entry.aborted = true; entry.reject?.({ aborted: true }); } };
  }
  return { entries, issue };
}

function providerStub() {
  const io = transport();
  return { ...io, providerRequest(payload) { return io.issue(payload).request; } };
}

function withWindow(t) {
  const previous = globalThis.window;
  globalThis.window = {
    innerWidth: 1280,
    innerHeight: 800,
    Readarr: { version: 'fixture', apiRoot: '/owned/api/v1', urlBase: '' },
    location: { origin: 'http://127.0.0.1' }
  };
  t.after(() => {
    if (previous === undefined) delete globalThis.window;
    else globalThis.window = previous;
  });
}

const props = {
  messageTypes: { INFO: 'info', SUCCESS: 'success', ERROR: 'error' },
  filterTypes: { EQUAL: 'equal' },
  sortDirections: { ASCENDING: 'ascending', DESCENDING: 'descending' }
};

function entry(path, name = path) {
  return { path, name, size: 0 };
}

test('filesystem navigation validates response and discards stale or cleared requests', () => {
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/pathActions', {
    'Utilities/createAjaxRequest': io.issue,
    'Utilities/requestAction': () => { throw Error('unused'); }
  });
  const getState = () => ({ paths: mod.defaultState });
  const actions = [];
  const dispatch = a => actions.push(a);
  const fetch = path => mod.fetchPaths({ path, includeFiles: false, allowFoldersWithoutTrailingSlashes: true })(dispatch, getState);
  fetch('/first');
  fetch('/second');
  assert.equal(io.entries[0].aborted, true);
  assert.deepEqual(io.entries[1].options.data, { path: '/second', includeFiles: false, allowFoldersWithoutTrailingSlashes: true });
  const count = actions.length;
  io.entries[0].resolve({ parent: '/', directories: [entry('/wrong')], files: [] });
  assert.equal(actions.length, count);
  io.entries[1].resolve({ parent: '/', directories: [entry('/second/child')], files: [] });
  const update = actions.findLast(a => a.type === mod.UPDATE_PATHS);
  const state = mod.reducers(mod.defaultState, update);
  assert.equal(state.currentPath, '/second');
  assert.equal(state.directories[0].path, '/second/child');
  assert.throws(() => mod.parsePaths('/bad', { directories: [null], files: [] }));
  fetch('/third');
  mod.clearPaths()(dispatch, getState);
  assert.equal(io.entries[2].aborted, true);
  const cleared = mod.reducers(state, actions.at(-1));
  assert.equal(cleared.currentPath, '');
  assert.equal(cleared.isFetching, false);
  assert.deepEqual(cleared.directories, []);
});

test('provider options never replace a newer response or a cleared field', () => {
  const io = providerStub();
  const mod = loadFrontendModule('Store/Actions/providerOptionActions', {
    'Utilities/requestAction': io.providerRequest,
    'Utilities/createAjaxRequest': () => { throw Error('unused'); }
  });
  let state = {};
  const getState = () => ({ providerOptions: state });
  const dispatch = a => { state = mod.reducers(state, a); };
  const fetch = (n) => mod.fetchOptions({ section: 'category', provider: 'downloadclient', action: 'getOptions', providerData: { value: { value: n } } })(dispatch, getState);
  fetch(1);
  fetch(1);
  assert.equal(io.entries.length, 1);
  fetch(2);
  io.entries[0].resolve({ options: [{ value: 1, name: 'old' }] });
  assert.equal(state.category.isFetching, true);
  io.entries[1].resolve({ options: [{ value: 0, name: 'new' }] });
  assert.deepEqual(state.category.items, [{ value: 0, name: 'new' }]);
  fetch(3);
  dispatch(mod.clearOptions({ section: 'category' }));
  io.entries[2].resolve({ options: [{ value: 3, name: 'cleared' }] });
  assert.deepEqual(state.category.items, []);
  assert.equal(state.category.isFetching, false);
});

test('command execution returns the actual validated response and calls completion once', async (t) => {
  withWindow(t);
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/commandActions', {
    'Utilities/createAjaxRequest': io.issue,
    'Utilities/requestAction': () => { throw Error('unused'); },
    'Utilities/String/translate': { fetchTranslations: async () => true },
    'Helpers/Props': props,
    'Utilities/Command': { isSameCommand: () => false }
  });
  let completed = 0;
  const dispatched = [];
  const getState = () => ({ commands: { handlers: {} } });
  const dispatch = a => typeof a === 'function' ? a(dispatch, getState) : dispatched.push(a);
  const pending = mod.executeCommandHelper({ name: 'BookSearch', bookIds: [1], commandFinished: () => ++completed }, dispatch);
  assert.deepEqual(JSON.parse(io.entries[0].options.data), { name: 'BookSearch', bookIds: [1] });
  io.entries[0].resolve({ id: 42, name: 'BookSearch', status: 'queued' });
  assert.equal((await pending).id, 42);
  assert.equal(dispatched.at(-1).payload.id, 42);
  const finished = { id: 42, name: 'BookSearch', status: 'completed' };
  mod.finishCommand(finished)(dispatch, getState);
  mod.finishCommand(finished)(dispatch, getState);
  assert.equal(completed, 1);
  const bad = mod.executeCommandHelper({ name: 'BookSearch' }, dispatch);
  io.entries[1].resolve({ id: 0, name: 'BookSearch', status: 'queued' });
  await assert.rejects(bad);
});

test('backup restore refuses an empty request and clears only the logs subsection', (t) => {
  withWindow(t);
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/systemActions', {
    'Utilities/createAjaxRequest': io.issue,
    'Utilities/requestAction': () => { throw Error('unused'); },
    'Utilities/String/translate': Object.assign((key) => key, { fetchTranslations: async () => true }),
    'Helpers/Props': props
  });
  const dispatched = [];
  const dispatch = a => dispatched.push(a);
  mod.restoreBackup({})(dispatch, () => ({ system: {} }));
  assert.equal(io.entries.length, 0);
  assert.equal(dispatched.at(-1).payload.isRestoring, false);
  assert.equal(dispatched.at(-1).payload.restoreError, 'Error restoring backup');
  const initial = { ...mod.defaultState, logs: { ...mod.defaultState.logs, items: [{ id: 1 }], totalPages: 1 }, health: { items: [{ id: 2 }] } };
  const cleared = mod.reducers(initial, mod.clearLogsTable());
  assert.deepEqual(cleared.logs.items, []);
  assert.deepEqual(cleared.health.items, [{ id: 2 }]);
});

test('calendar range requests are isolated and clearing prevents obsolete results', (t) => {
  withWindow(t);
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/calendarActions', {
    'Utilities/createAjaxRequest': io.issue,
    'Utilities/requestAction': () => { throw Error('unused'); },
    'Helpers/Props': props,
    'Calendar/calendarViews': { DAY: 'day', WEEK: 'week', MONTH: 'month', FORECAST: 'forecast', AGENDA: 'agenda' },
    'Commands/commandNames': { BOOK_SEARCH: 'BookSearch' },
    './commandActions': { executeCommandHelper: async () => ({ id: 19 }) }
  });
  const getState = () => ({ calendar: mod.defaultState, settings: { ui: { item: { firstDayOfWeek: 1 } } } });
  const dispatched = [];
  const dispatch = a => dispatched.push(a);
  const fetch = view => mod.fetchCalendar({ time: '2026-11-25T12:00:00', view })(dispatch, getState);
  fetch('week');
  fetch('agenda');
  assert.equal(io.entries[0].aborted, true);
  const count = dispatched.length;
  io.entries[0].resolve([{ id: 1 }]);
  assert.equal(dispatched.length, count);
  io.entries[1].resolve([{ id: 2 }]);
  assert.equal(dispatched.at(-1).payload[0].payload.data[0].id, 2);
  fetch('day');
  mod.clearCalendar()(dispatch, getState);
  assert.equal(io.entries[2].aborted, true);
  const cleared = mod.reducers({ ...mod.defaultState, items: [{ id: 1 }] }, dispatched.at(-1));
  assert.deepEqual(cleared.items, []);
});

test('app reconnect timer follows the actual app slice and treats an unauthorized listener as online', (t) => {
  withWindow(t);
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/appActions', {
    'Utilities/createAjaxRequest': io.issue,
    'Utilities/String/translate': { fetchTranslations: async () => true }
  });
  let app = { ...mod.defaultState, isConnected: true, isRestarting: false };
  const getState = () => ({ app });
  const dispatch = a => { app = mod.reducers(app, a); };
  mod.pingServer()(dispatch, getState);
  t.mock.timers.tick(5000);
  assert.equal(io.entries.length, 0);
  app = { ...app, isRestarting: true };
  mod.pingServer()(dispatch, getState);
  t.mock.timers.tick(5000);
  assert.equal(io.entries.length, 1);
  io.entries[0].reject({ status: 401 });
  assert.equal(app.isRestarting, false);
});
