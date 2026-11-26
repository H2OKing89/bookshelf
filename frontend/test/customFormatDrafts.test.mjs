import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const helper = 'Settings/CustomFormats/customFormatState';
const [models, selectSettings] = loadFrontendModule([helper, 'Store/Selectors/selectSettings'], {
  'Settings/settingDrafts': { getDraft: (item, key) => item[key] },
  // Project just the same raw values here; actual Store selector separately
  // supplies the normalized draft to the export boundary.
});

const template = Object.freeze({ id: 0, name: 'Default', implementation: 'Fixture', implementationName: 'Fixture condition', required: false, negate: true,
  fields: Object.freeze([{ name: 'count', value: 4, type: 'number' }, { name: 'enabled', value: true }, { name: 'choice', value: 'x' }, { name: 'list', value: [] }]), presets: [] });

test('custom format import validates all conditions before returning an independent draft with explicit falsy fields', () => {
  const input = { id: 99, name: 'Owned', includeCustomFormatWhenRenaming: false, arbitraryGuideData: 7,
    specifications: [{ id: 7, name: 'First', implementation: 'Fixture', negate: false, required: true, fields: { count: 0, enabled: false, choice: null, list: ['a', 0] } },
      { id: 7, name: 'Second', implementation: 'Fixture', fields: {} }] };
  const draft = models.parseCustomFormatImport(JSON.stringify(input), [template]);
  assert.deepEqual(draft.pendingChanges, { name: 'Owned', includeCustomFormatWhenRenaming: false });
  assert.deepEqual(draft.specifications.map((spec) => spec.id), [2, 3]);
  assert.equal(draft.specifications[0].negate, false);
  assert.equal(draft.specifications[0].required, true);
  assert.deepEqual(draft.specifications[0].fields.map((field) => field.value), [0, false, null, ['a', 0]]);
  assert.equal(draft.specifications[0].fields[0].type, 'number');
  assert.equal(template.fields[0].value, 4);
  assert.notEqual(draft.specifications[0].fields, template.fields);
  const invalid = { ...input, specifications: [...input.specifications, { implementation: 'Fixture', fields: { invented: 1 } }] };
  assert.throws(() => models.parseCustomFormatImport(JSON.stringify(invalid), [template]), /Unknown option/);
  for (const bad of ['null', '[]', '{"specifications":null}', '{"specifications":[null]}', '{"specifications":[{"implementation":"Fixture","fields":null}]}']) {
    assert.throws(() => models.parseCustomFormatImport(bad, [template]));
  }
});

test('custom format export round-trips exact field values without provider metadata or temporary identities', () => {
  const input = { name: 'Owned', includeCustomFormatWhenRenaming: false,
    specifications: [{ name: 'Condition', implementation: 'Fixture', required: false, negate: false, fields: { count: 0, enabled: false, choice: null, list: ['a'] } }] };
  const draft = models.parseCustomFormatImport(JSON.stringify(input), [template]);
  const saved = { id: 1, ...draft.pendingChanges, specifications: draft.specifications };
  const item = selectSettings.default(saved, {}, null).settings;
  const output = JSON.parse(models.exportCustomFormat(item));
  assert.deepEqual(output, input);
  assert.equal(models.formatCards([null, {}, { id: 1, name: 'Owned', specifications: [null, { name: 'Label', required: false, negate: true }] }]).length, 1);
  assert.deepEqual(models.specificationCards([{ id: 2, implementation: 'Fixture', name: 'Condition', negate: false }]), [{ id: 2, implementation: 'Fixture', implementationName: 'Fixture', name: 'Condition', negate: false, required: false }]);
});

test('actual custom format actions allocate independent local IDs, keep item maps correct and never mutate previous drafts', () => {
  const requests = [];
  const [formats, specifications, { default: createHandleActions }] = loadFrontendModule([
    'Store/Actions/Settings/customFormats', 'Store/Actions/Settings/customFormatSpecifications', 'Store/Actions/Creators/createHandleActions'
  ], {
    'Utilities/createAjaxRequest': (options) => {
      const entry = { options };
      requests.push(entry);
      return { request: { done(fn) { entry.resolve = fn; }, fail(fn) { entry.reject = fn; } }, abortRequest() {} };
    },
    'Components/ActionStatus': { announceSettingsSaved() {} },
    'Utilities/serializeLegacyQuery': () => ''
  });
  const saved = { id: 3, name: 'Saved', specifications: [{ ...template, id: 0 }, { ...template, id: 0, name: 'Other' }] };
  const oldDraft = Object.freeze({ name: 'New', includeCustomFormatWhenRenaming: false });
  const defaults = { customFormats: { ...formats.default.defaultState, items: [saved], itemMap: { 3: 90 }, pendingChanges: oldDraft },
    customFormatSpecifications: { ...specifications.default.defaultState, schema: [template] } };
  const reduce = createHandleActions({ ...formats.default.reducers, ...specifications.default.reducers }, defaults, 'settings');
  let state = defaults;
  const getState = () => ({ settings: state });
  function dispatch(action) {
    if (action.meta?.batch) action.payload.forEach(dispatch);
    else state = reduce(state, action);
    return action;
  }
  const action = (module, type, payload = {}) => module.default.actionHandlers[type](getState, payload, dispatch);
  action(specifications, specifications.FETCH_CUSTOM_FORMAT_SPECIFICATIONS, { id: 3 });
  assert.deepEqual(state.customFormatSpecifications.items.map((spec) => spec.id), [1, 2]);
  dispatch(specifications.cloneCustomFormatSpecification({ id: 1 }));
  assert.deepEqual(state.customFormatSpecifications.items.map((spec) => spec.id), [1, 2, 3]);
  assert.deepEqual(state.customFormatSpecifications.itemMap, { 1: 0, 2: 1, 3: 2 });
  assert.equal(saved.specifications[0].id, 0);
  assert.notEqual(state.customFormatSpecifications.items[2].fields, saved.specifications[0].fields);
  action(specifications, specifications.DELETE_CUSTOM_FORMAT_SPECIFICATION, { id: 2 });
  assert.deepEqual(state.customFormatSpecifications.itemMap, { 1: 0, 3: 1 });
  action(formats, formats.SAVE_CUSTOM_FORMAT, { id: 3 });
  assert.equal(requests.length, 1);
  assert.equal(Object.hasOwn(oldDraft, 'specifications'), false);
  assert.deepEqual(JSON.parse(requests[0].options.data).specifications.map((spec) => spec.id), [1, 3]);
  requests[0].reject({ status: 400 });
  assert.equal(state.customFormats.pendingChanges.includeCustomFormatWhenRenaming, false);
  action(specifications, specifications.DELETE_ALL_CUSTOM_FORMAT_SPECIFICATION);
  assert.deepEqual(state.customFormatSpecifications.itemMap, {});
  action(specifications, specifications.FETCH_CUSTOM_FORMAT_SPECIFICATIONS, { id: 999 });
  assert.equal(state.customFormatSpecifications.isPopulated, false);
  assert.deepEqual(state.customFormatSpecifications.items, []);
  assert.equal(state.customFormatSpecifications.error.kind, 'invalid-response');
});

test('provider card projections preserve zero priorities and explicit booleans while sorting without touching credentials', () => {
  const { indexerCards, downloadClientCards, importListCards, tagSummaries } = loadFrontendModule('Settings/providerCardState');
  const source = Object.freeze([
    Object.freeze({ id: 8, name: 'Zulu', priority: 0, enableRss: false, supportsRss: true, tags: [3, null, '4'], fields: [{ name: 'password', value: 'fixture-only' }] }),
    Object.freeze({ id: 2, name: 'Alpha', enableRss: true, supportsRss: false }), null
  ]);
  const cards = indexerCards(source);
  assert.deepEqual(cards.map((item) => item.id), [2, 8]);
  assert.equal(cards[1].priority, 0);
  assert.equal(cards[1].enableRss, false);
  assert.deepEqual(cards[1].tags, [3]);
  assert.equal(Object.hasOwn(cards[1], 'fields'), false);
  assert.equal(source[0].id, 8);
  assert.deepEqual(downloadClientCards([{ id: 2, name: 'Client', enable: false, priority: 0, tags: [] }]), [{ id: 2, name: 'Client', enable: false, priority: 0, tags: [] }]);
  assert.deepEqual(importListCards([{ id: 2, name: 'List', enableAutomaticAdd: false, shouldSearch: true, minRefreshInterval: '00:30:00' }]), [{ id: 2, name: 'List', enableAutomaticAdd: false, shouldSearch: true, minRefreshInterval: '00:30:00' }]);
  assert.deepEqual(tagSummaries([{ id: 3, label: 'Tag' }, { id: 9 }, null]), [{ id: 3, label: 'Tag' }]);
});
