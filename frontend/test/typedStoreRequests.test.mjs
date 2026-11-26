import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const dispatched = [];
const requests = [];
const dispatch = (action) => dispatched.push(action);
const [save, schema, bulkEdit, bulkRemove, findCommand, sameCommand, sortedSection, existingAuthor] = loadFrontendModule([
  'Store/Actions/Creators/createSaveHandler',
  'Store/Actions/Creators/createFetchSchemaHandler',
  'Store/Actions/Creators/createBulkEditItemHandler',
  'Store/Actions/Creators/createBulkRemoveItemHandler',
  'Utilities/Command/findCommand',
  'Utilities/Command/isSameCommand',
  'Store/Selectors/createSortedSectionSelector',
  'Store/Selectors/createExistingAuthorSelector'
], {
  'Utilities/createAjaxRequest': (options) => {
    const entry = { options };
    const request = { done(callback) { entry.resolve = callback; return this; }, fail(callback) { entry.reject = callback; return this; } };
    requests.push(entry);
    return { request, abortRequest() {} };
  }
}).map((mod) => mod.default);

test('typed save preserves zero and false, and invalid replies do not clear drafts', () => {
  const perform = save('settings.fixture', '/fixture');
  const getState = () => ({ settings: { fixture: { item: { count: 8, enabled: true }, pendingChanges: { count: 0, enabled: false } } } });
  perform(getState, { name: 'Fixture' }, dispatch);
  assert.deepEqual(JSON.parse(requests.at(-1).options.data), { count: 0, enabled: false, name: 'Fixture' });
  requests.at(-1).resolve(null);
  assert.equal(dispatched.at(-1).payload.saveError.kind, 'invalid-response');
  assert.equal(Object.hasOwn(dispatched.at(-1).payload, 'pendingChanges'), false);
  perform(getState, {}, dispatch);
  requests.at(-1).resolve({ count: 0, enabled: false });
  assert.deepEqual(dispatched.at(-1).payload[0].payload.data, { count: 0, enabled: false });
  assert.deepEqual(dispatched.at(-1).payload[1].payload.pendingChanges, {});
});

test('schema and bulk replies reject invalid shapes without applying partial changes', () => {
  schema('fixture', '/schema')(() => ({}), {}, dispatch);
  requests.at(-1).resolve(null);
  assert.equal(dispatched.at(-1).payload.schemaError.kind, 'invalid-response');
  schema('fixture', '/schema')(() => ({}), {}, dispatch);
  requests.at(-1).resolve([]);
  assert.deepEqual(dispatched.at(-1).payload.schema, []);
  bulkEdit('fixture', '/bulk')(() => ({}), {}, dispatch);
  requests.at(-1).resolve([{ id: 7 }, { name: 'no identity' }]);
  assert.equal(dispatched.at(-1).payload.saveError.kind, 'invalid-response');
  bulkEdit('fixture', '/bulk')(() => ({}), {}, dispatch);
  requests.at(-1).resolve([{ id: 7, section: 'unrelated', enabled: false }]);
  assert.deepEqual(dispatched.at(-1).payload[1].payload, { id: 7, section: 'fixture', enabled: false });
  const count = requests.length;
  bulkRemove('fixture', '/bulk')(() => ({}), { ids: [1, null] }, dispatch);
  assert.equal(requests.length, count);
  assert.equal(dispatched.at(-1).payload.deleteError.kind, 'invalid-response');
});

test('command constraints support array subsets, zero and false without matching malformed commands', () => {
  const body = { name: 'RefreshAuthor', authorIds: [4, 2], force: false, priority: 0 };
  const constraints = { name: 'refreshauthor', authorIds: [2], force: false, priority: 0 };
  assert.equal(sameCommand(body, constraints), true);
  assert.equal(sameCommand(body, { ...constraints, authorIds: [3] }), false);
  assert.equal(sameCommand(null, constraints), false);
  assert.equal(sameCommand({ name: 'RefreshAuthor', authorIds: 2 }, constraints), false);
  const commands = [{ id: 1, body }, { id: 2, body: null }, { id: 3, body }];
  assert.equal(findCommand(commands, constraints), commands[2]);
});

test('typed selectors keep source resources immutable and avoid matching absent author slugs', () => {
  const items = Object.freeze([Object.freeze({ id: 2 }), Object.freeze({ id: 1 })]);
  const state = { fixture: { items }, authors: { items: [{ id: 7, titleSlug: 'author' }, { id: 8 }] } };
  const selector = sortedSection('fixture', (a, b) => a.id - b.id);
  assert.deepEqual(selector(state).items.map((item) => item.id), [1, 2]);
  assert.deepEqual(items.map((item) => item.id), [2, 1]);
  assert.equal(selector({ ...state }), selector(state));
  const author = existingAuthor();
  assert.equal(author(state, {}), false);
  assert.equal(author(state, { titleSlug: '' }), false);
  assert.equal(author(state, { titleSlug: 'author' }), true);
});
