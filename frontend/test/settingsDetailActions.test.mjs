import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function transport() {
  const requests = [];
  return {
    requests,
    createAjaxRequest(options) {
      const entry = { options, aborted: false };
      requests.push(entry);
      return {
        request: {
          done(callback) { entry.resolve = callback; return this; },
          fail(callback) { entry.reject = callback; return this; }
        },
        abortRequest() { entry.aborted = true; entry.reject?.({ aborted: true }); }
      };
    }
  };
}

test('named collection parser retains Unknown, Any and Original IDs and rejects duplicates', () => {
  const { parseNamedOptions, parseSettingsList } = loadFrontendModule('Store/Actions/Settings/settingsResponses');
  const options = [{ id: 0, name: 'Unknown' }, { id: -1, name: 'Any' }, { id: -2, name: 'Original' }];
  assert.deepEqual(parseNamedOptions(options), options);
  assert.throws(() => parseNamedOptions([...options, { id: 0, name: 'Duplicate' }]));
  assert.throws(() => parseSettingsList([{ id: 0 }]));
  assert.throws(() => parseSettingsList([{ id: 1 }, null]));
});

test('quality definition drafts retain null/zero and save only valid complete response, preserving newer edits', () => {
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/Settings/qualityDefinitions', {
    'Utilities/createAjaxRequest': io.createAjaxRequest
  });
  const section = 'settings.qualityDefinitions';
  const first = { id: 1, quality: { name: 'M4B' }, title: 'Original', weight: 1, minSize: 5, maxSize: 100 };
  const second = { ...first, id: 2 };
  let settings = { qualityDefinitions: { items: [first, second], pendingChanges: {} } };
  const reducer = mod.default.reducers[mod.SET_QUALITY_DEFINITION_VALUE];
  const edit = (id, name, value) => { settings = reducer(settings, { payload: { id, name, value } }); };
  edit(1, 'minSize', 0);
  edit(1, 'maxSize', null);
  assert.deepEqual(settings.qualityDefinitions.pendingChanges['1'], { minSize: 0, maxSize: null });
  const saved = settings;
  edit(99, 'minSize', 2); edit(1, 'id', 4); edit(1, 'maxSize', NaN);
  assert.equal(settings, saved);

  const dispatched = [];
  const save = () => mod.default.actionHandlers[mod.SAVE_QUALITY_DEFINITIONS](() => ({ settings }), {}, a => dispatched.push(a));
  save();
  assert.deepEqual(JSON.parse(io.requests[0].options.data), [{ ...first, minSize: 0, maxSize: null }]);
  io.requests[0].resolve([{ ...first, minSize: 0, maxSize: null }]);
  assert.equal(dispatched.at(-1).payload.saveError.kind, 'invalid-response');
  assert.deepEqual(settings.qualityDefinitions.pendingChanges['1'], { minSize: 0, maxSize: null });
  save();
  edit(1, 'title', 'Newer draft');
  edit(2, 'minSize', 0);
  const canonical = [{ ...first, minSize: 0, maxSize: null }, second];
  io.requests[1].resolve(canonical);
  const actions = dispatched.at(-1).payload;
  assert.deepEqual(actions[0].payload, { section, data: canonical });
  assert.deepEqual(actions[1].payload.pendingChanges, { '1': { title: 'Newer draft' }, '2': { minSize: 0 } });
});

test('naming examples ignore old successes and aborts during rapid draft changes', () => {
  const io = transport();
  const mod = loadFrontendModule('Store/Actions/Settings/namingExamples', {
    'Utilities/createAjaxRequest': io.createAjaxRequest
  });
  let naming = { item: { id: 1, renameBooks: true }, pendingChanges: { renameBooks: false } };
  const getState = () => ({ settings: { naming } });
  const dispatched = [];
  const fetch = () => mod.default.actionHandlers[mod.FETCH_NAMING_EXAMPLES](getState, {}, a => dispatched.push(a));
  fetch();
  assert.equal(io.requests[0].options.data.renameBooks, false);
  naming = { ...naming, pendingChanges: { renameBooks: true, colonReplacementFormat: 0 } };
  fetch();
  assert.equal(io.requests[0].aborted, true);
  const count = dispatched.length;
  io.requests[0].resolve({ singleBookExample: 'Old' });
  assert.equal(dispatched.length, count);
  io.requests[1].resolve({ singleBookExample: 'New', multiPartBookExample: null });
  assert.equal(dispatched.at(-1).payload[0].payload.data.singleBookExample, 'New');
});

test('detail history ignores superseded reads and mutation completions after clearing selection', () => {
  const io = transport();
  let refreshes = 0;
  const { createDetailHistoryRequests, parseDetailHistory } = loadFrontendModule('Store/Actions/Creators/detailHistoryRequests', {
    'Utilities/createAjaxRequest': io.createAjaxRequest,
    'Activity/History/historyPageQuery': {
      parseHistoryRecord(record) { if (!record.id) throw new TypeError('invalid'); return { id: record.id }; },
      scheduleHistoryRefresh() { ++refreshes; }
    }
  });
  const handlers = createDetailHistoryRequests({
    section: 'authorHistory', path: '/history/author',
    query: ({ authorId }) => authorId ? { authorId } : null,
    parse: parseDetailHistory,
    refetch: payload => ({ type: 'refetch', payload }),
    clear: () => ({ type: 'clear' })
  });
  const getState = () => ({});
  const dispatched = [];
  const dispatch = a => dispatched.push(a);
  handlers.fetch(getState, { authorId: 1 }, dispatch);
  handlers.fetch(getState, { authorId: 2 }, dispatch);
  assert.equal(io.requests[0].aborted, true);
  const count = dispatched.length;
  io.requests[0].resolve([{ id: 7 }]);
  assert.equal(dispatched.length, count);
  io.requests[1].resolve([{ id: 8, book: { title: 'Retained' } }]);
  assert.deepEqual(dispatched.at(-1).payload[0].payload.data, [{ id: 8, book: { title: 'Retained' } }]);
  handlers.markFailed(getState, { historyId: 8, authorId: 2 }, dispatch);
  handlers.clear(getState, {}, dispatch);
  io.requests[2].resolve({});
  assert.equal(refreshes, 1);
  assert.equal(dispatched.at(-1).type, 'clear');
});
