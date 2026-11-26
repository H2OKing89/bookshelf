import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { generalDrafts, mediaManagementDrafts, uiDrafts } = loadFrontendModule('Settings/settingDrafts.ts');
const inputTypes = loadFrontendModule('Helpers/Props/inputTypes.ts');
const { default: ProviderFieldFormGroup, getSelectValues } = loadFrontendModule('Components/Form/ProviderFieldFormGroup.tsx', {
  'Components/Form/FormGroup': () => null,
  'Components/Form/FormInputGroup': () => null,
  'Components/Form/FormLabel': () => null,
  'Helpers/Props': { inputTypes }
});

function providerField(type, value, additional = {}) {
  return ProviderFieldFormGroup({
    type,
    name: 'testField',
    label: 'Test field',
    value,
    onChange: () => {},
    ...additional
  });
}

function input(type, value, additional) {
  return providerField(type, value, additional).props.children[1].props;
}

test('settings projections preserve false, zero, null, drafts and validation messages', () => {
  const errors = [{ message: 'Synthetic validation failure' }];
  const settings = {
    port: { value: 0, previousValue: 8787, pending: true, errors, warnings: [] },
    enableSsl: { value: false },
    sslCertPassword: { value: null, previousValue: '********' },
    proxyEnabled: { value: 'noChange' },
    backupFolder: { value: '' },
    accounts: { value: [{ identifier: 'abc', username: 'sample', hasPassword: false }] }
  };
  const general = generalDrafts(settings);
  assert.equal(general.port.value, 0);
  assert.equal(general.port.previousValue, 8787);
  assert.equal(general.port.pending, true);
  assert.equal(general.port.errors, errors);
  assert.equal(general.enableSsl.value, false);
  assert.equal(general.sslCertPassword.value, null);
  assert.equal(general.proxyEnabled.value, 'noChange');
  assert.equal(general.backupFolder.value, '');
  assert.deepEqual(general.accounts.value, settings.accounts.value);
  assert.equal(uiDrafts({ firstDayOfWeek: { value: 0 } }).firstDayOfWeek.value, 0);
  assert.equal(mediaManagementDrafts({ recycleBinCleanupDays: { value: 0 } }).recycleBinCleanupDays.value, 0);
  assert.equal(settings.port.value, 0);
});

test('malformed settings do not populate editable controls with object representations', () => {
  const projected = generalDrafts({
    port: { value: { unknown: true } },
    backupFolder: { value: ['invalid'] },
    accounts: { value: [null, { identifier: 'missing-username' }] }
  });
  assert.equal(projected.port.value, null);
  assert.equal(projected.backupFolder.value, null);
  assert.deepEqual(projected.accounts.value, []);
});

test('provider fields preserve explicit values, validation messages and dynamic metadata', () => {
  const errors = [{ message: 'Synthetic validation error' }];
  assert.equal(input('number', 0, { errors, pending: true }).value, 0);
  assert.equal(input('number', 0, { errors }).errors, errors);
  assert.equal(input('checkbox', false).value, false);
  assert.equal(input('checkbox', 'noChange').value, 'noChange');
  assert.equal(input('checkbox', null).value, null);
  assert.equal(input('password', '').value, '');
  assert.deepEqual(input('select', [0, 'alpha']).value, [0, 'alpha']);
  assert.equal(input('select', 0).value, 0);
  assert.equal(input('select', null).value, null);
  const data = { fields: [] };
  const dynamic = input('select', ['id'], { selectOptionsProviderAction: 'getItems', providerData: data });
  assert.equal(dynamic.type, inputTypes.DYNAMIC_SELECT);
  assert.equal(dynamic.providerData, data);
  assert.deepEqual(dynamic.value, ['id']);
  assert.equal(input('filePath', '/test/file').includeFiles, true);
  assert.equal(input('path', '/test/dir').includeFiles, undefined);
});

test('hidden-if-not-set distinguishes empty values from explicit numeric and boolean settings', () => {
  for (const value of [undefined, null, '']) {
    assert.equal(providerField('textbox', value, { hidden: 'hiddenIfNotSet' }), null);
  }
  assert.ok(providerField('number', 0, { hidden: 'hiddenIfNotSet' }));
  assert.ok(providerField('checkbox', false, { hidden: 'hiddenIfNotSet' }));
  assert.equal(providerField('number', 0, { hidden: 'hidden' }), null);
});

test('provider options preserve zero and string keys and ignore malformed entries', () => {
  const values = getSelectValues([
    { name: 'Zero', value: 0, hint: 'Numeric' },
    { name: 'String', value: '0' },
    { name: 'Malformed', value: {} },
    null
  ]);
  assert.deepEqual(values, [
    { key: 0, value: 'Zero', hint: 'Numeric' },
    { key: '0', value: 'String', hint: undefined }
  ]);
});
