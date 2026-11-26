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

const noop = () => {};
const translate = (key) => key;
const { providerFields, providerItems, providerKeys } = loadFrontendModule('Components/Form/providerControlTypes.ts');
const { getProfileDefault, parseProfileValue, profileOptions, profiles } = loadFrontendModule('Components/Form/profileSelectOptions.ts', {
  'Utilities/String/translate': translate
});
const { getSelectOptions } = loadFrontendModule('Components/Form/EnhancedSelectInputConnector.tsx', {
  './EnhancedSelectInput': noop,
  'Store/Actions/providerOptionActions': { clearOptions: noop, defaultState: {}, fetchOptions: noop }
});
const { filterPaths } = loadFrontendModule('Components/Form/PathInputConnector.tsx', {
  './PathInput': noop,
  'Store/Actions/pathActions': { clearPaths: noop, fetchPaths: noop }
});
const { getRootFolderOptions } = loadFrontendModule('Components/Form/RootFolderSelectInputConnector.tsx', {
  './RootFolderSelectInput': noop,
  'Utilities/String/translate': translate
});

test('provider API boundaries retain key types, zero and hierarchical options', () => {
  assert.deepEqual(providerKeys([0, '0', false, null, {}]), [0, '0']);
  assert.deepEqual(providerItems([{ id: 0, name: 'Device' }, { id: false, name: 'Invalid' }, null]), [{ id: 0, name: 'Device' }]);
  assert.deepEqual(providerFields(undefined), []);
  assert.deepEqual(providerFields({ fields: [null, { name: 'test', value: false }] }), [{ name: 'test', value: false }]);
  assert.deepEqual(getSelectOptions([
    { value: 0, name: 'Root' },
    { value: 'child', name: 'Child', parentValue: 0 },
    { value: {}, name: 'Invalid' }
  ]), [
    { key: 0, value: 'Root', hint: undefined, parentKey: undefined },
    { key: 'child', value: 'Child', hint: undefined, parentKey: 0 }
  ]);
});

test('profile selectors distinguish mixed and noChange from numeric profile IDs', () => {
  const values = profileOptions(profiles({ items: [{ id: 1, name: 'Z' }, { id: 0, name: 'A' }, null] }), true, false, true);
  assert.deepEqual(values.map(({ key }) => key), ['mixed', 'noChange', 0, 1]);
  assert.equal(values[1].isDisabled, false);
  assert.equal(getProfileDefault(0, values), undefined);
  assert.equal(getProfileDefault('mixed', values), undefined);
  assert.equal(getProfileDefault('noChange', values), undefined);
  assert.equal(getProfileDefault(null, values), 0);
  assert.equal(getProfileDefault(null, []), undefined);
  assert.equal(parseProfileValue('0'), 0);
  assert.equal(parseProfileValue('noChange'), 'noChange');
  assert.equal(parseProfileValue('mixed'), undefined);
  assert.equal(parseProfileValue(''), undefined);
});

test('path suggestions and root folders ignore missing rows without losing a selected path or zero space', () => {
  assert.deepEqual(filterPaths({
    currentPath: '/root/books',
    directories: [{ path: '/root/books/a', type: 'directory' }, { path: '/other' }, null],
    files: [{ path: '/root/books/f.m4b', type: 'file' }]
  }), [
    { path: '/root/books/a', type: 'directory' },
    { path: '/root/books/f.m4b', type: 'file' }
  ]);
  const options = getRootFolderOptions({ items: [{ path: '/root/books', freeSpace: 0 }] }, '/missing', true, true, false);
  assert.equal(options[0].key, 'noChange');
  assert.equal(options[0].isDisabled, false);
  assert.equal(options[1].freeSpace, 0);
  assert.deepEqual(options[2], { key: '/missing', value: '/missing', isMissing: true, isDisabled: true });
  assert.equal(options.at(-1).key, 'addNew');
  assert.deepEqual(getRootFolderOptions({ items: [] }, null, true).map(({ key }) => key), ['', 'addNew']);
});
