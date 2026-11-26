import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const [{ providerEditDrafts, providerFields, providerHasChanges }, { default: selectSettings }] = loadFrontendModule([
  'Settings/providerSettingsState.ts',
  'Store/Selectors/selectSettings.ts'
]);

test('provider drafts retain exact false, zero, null, and schema metadata without changing the provider projection', () => {
  const { settings } = selectSettings({
    id: 0,
    name: 'Synthetic provider',
    enable: true,
    priority: 1,
    fields: [
      { name: 'port', label: 'Port', type: 'number', value: 123 },
      { name: 'list', label: 'List', type: 'select', value: [1], selectOptionsProviderAction: 'getLists', schemaMetadata: { custom: true } },
      { name: 'unset', type: 'textbox', value: null }
    ]
  }, { enable: false, priority: 0, fields: { port: 0, list: [] } }, null);
  const drafts = providerEditDrafts(settings);

  assert.equal(drafts.id, 0);
  assert.equal(drafts.enable.value, false);
  assert.equal(drafts.priority.value, 0);
  assert.equal(drafts.fields[0].value, 0);
  assert.equal(drafts.fields[0].pending, true);
  assert.deepEqual(drafts.fields[1].value, []);
  assert.equal(drafts.fields[1].selectOptionsProviderAction, 'getLists');
  assert.deepEqual(drafts.fields[1].schemaMetadata, { custom: true });
  assert.equal(drafts.fields[2].value, null);
  assert.equal(drafts.fields[2].label, 'unset');
  assert.equal(settings.fields[2].label, undefined);
});

test('provider editors safely render absent fields and malformed provider messages', () => {
  assert.deepEqual(providerFields({}), []);
  assert.equal(providerEditDrafts({ message: { value: { type: 'unknown', message: 'Text' } } }).message, null);
  assert.deepEqual(providerEditDrafts({ message: { value: { type: 'warning', message: 'Text' } } }).message, {
    type: 'warning', message: 'Text'
  });
});

test('dirty provider detection includes empty, zero, false and cleared dynamic values', () => {
  const section = {
    items: [{ id: 1, name: 'Provider', enable: false, fields: [{ name: 'port', value: 0 }, { name: 'unset', value: null }] }],
    selectedSchema: { enable: true, fields: [{ name: 'port', value: 0 }] }
  };
  const dirty = (pendingChanges, id = 1) => providerHasChanges({ ...section, pendingChanges }, id);
  assert.equal(dirty({ enable: false, fields: { port: 0, unset: null } }), false);
  assert.equal(dirty({ enable: true }), true);
  assert.equal(dirty({ fields: { unset: '' } }), true);
  assert.equal(dirty({ fields: { port: null } }), true);
  assert.equal(dirty({ fields: { port: 0 }, enable: true, name: '' }, 0), false);
});
