import assert from 'node:assert/strict';
import test from 'node:test';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const styles = new Proxy({}, { get: (_, key) => key === '__esModule' ? undefined : key });
const [kinds] = loadFrontendModule(['Helpers/Props/kinds']);
const noop = () => null;
const boundaries = {
  'Helpers/Props': { icons: {}, kinds },
  'Components/Icon': noop,
  'Components/Link/Link': noop,
  'Components/Link/IconButton': noop,
  'Components/Label': ({ children }) => React.createElement('span', null, children),
  'Utilities/String/translate': (key) => key,
  ...Object.fromEntries([
    'TextInput', 'TextArea', 'CheckInput', 'SelectInput', 'KeyValueListInput',
    'KeyValueListInputItem', 'HintedSelectInputSelectedValue', 'EnhancedSelectInputSelectedValue',
    'FormInputHelpText'
  ].map((name) => [`./${name}.css`, styles]))
};
const form = 'Components/Form/';
const [
  { default: NumberInput, parseValue },
  { default: TextInput },
  { default: CheckInput },
  { default: SelectInput },
  { default: KeyValueListInput },
  { default: HintedSelectInputSelectedValue },
  { default: formControlProps }
] = loadFrontendModule(['NumberInput', 'TextInput', 'CheckInput', 'SelectInput', 'KeyValueListInput',
  'HintedSelectInputSelectedValue', 'formControlProps'].map((name) => form + name), boundaries);
const [
  { default: selectSettings },
  { default: createSettingsSectionSelector },
  { default: createProviderSettingsSelector }
] = loadFrontendModule(['selectSettings', 'createSettingsSectionSelector', 'createProviderSettingsSelector'].map((name) => 'Store/Selectors/' + name));

function createField(Component, props) {
  const instance = new Component(props);
  // Run class event handlers without mounting a browser. Preserve the normal
  // shallow React state merge so draft values can be asserted deterministically.
  instance.setState = (update) => {
    const patch = typeof update === 'function' ? update(instance.state, instance.props) : update;
    instance.state = { ...instance.state, ...patch };
  };
  return instance;
}

test('typed number input keeps zero, empty and parsed numeric payloads distinct', () => {
  assert.equal(parseValue({}, '0'), 0);
  assert.equal(parseValue({}, ''), null);
  assert.equal(parseValue({}, null), null);
  assert.equal(parseValue({}, 'not a number'), null);
  assert.equal(parseValue({ isFloat: true }, '1.25'), 1.25);
  assert.equal(parseValue({ min: 0, max: 5 }, '-7'), 0);
  assert.equal(parseValue({ min: 0, max: 5 }, '8'), 5);

  const events = [];
  const input = createField(NumberInput, { name: 'priority', value: 0, onChange: (event) => events.push(event) });
  assert.equal(input.state.value, '0');
  input.onFocus();
  input.onChange({ name: 'priority', value: '' });
  input.onBlur();
  input.onChange({ name: 'priority', value: '0' });
  assert.deepEqual(events, [
    { name: 'priority', value: null },
    { name: 'priority', value: null },
    { name: 'priority', value: 0 }
  ]);
});

test('text and native select changes retain DOM strings and optional file payload', () => {
  const events = [];
  const input = createField(TextInput, { name: 'filename', type: 'file', onChange: (event) => events.push(event) });
  const files = { length: 1 };
  input.onChange({ target: { value: '', files } });
  const select = createField(SelectInput, {
    name: 'profile', value: 0, values: [{ key: 0, value: 'Default' }],
    onChange: (event) => events.push(event)
  });
  select.onChange({ target: { value: '0' } });
  assert.deepEqual(events, [
    { name: 'filename', value: '', files },
    { name: 'profile', value: '0' }
  ]);
  const markup = renderToStaticMarkup(React.createElement(SelectInput, select.props));
  assert.match(markup, /value="0" selected="">Default<\/option>/);
});

test('checkbox distinguishes indeterminate from false and preserves Shift selection', () => {
  const events = [];
  const input = createField(CheckInput, { name: 'selected', value: 'mixed', onChange: (event) => events.push(event) });
  const checkbox = { checked: false, indeterminate: false };
  input.setRef(checkbox);
  input.setIndeterminate();
  assert.equal(checkbox.indeterminate, true);
  input.onChange({ target: { checked: true }, nativeEvent: { shiftKey: true } });
  input.props = { ...input.props, value: false };
  input.setIndeterminate();
  assert.equal(checkbox.indeterminate, false);
  input.onChange({ target: { checked: false }, nativeEvent: {} });
  assert.deepEqual(events, [{ name: 'selected', value: true, shiftKey: true }]);
});

test('key/value cleanup retains explicit zero instead of treating it as blank', () => {
  const events = [];
  const list = createField(KeyValueListInput, {
    name: 'values',
    value: [{ key: '', value: 0 }, { key: '', value: '' }, { key: 'empty', value: '' }],
    onChange: (event) => events.push(event)
  });
  list.onBlur();
  assert.deepEqual(events, [{
    name: 'values', value: [{ key: '', value: 0 }, { key: 'empty', value: '' }]
  }]);
});

test('enhanced multiselect labels do not merge numeric and textual keys', () => {
  const html = renderToStaticMarkup(React.createElement(HintedSelectInputSelectedValue, {
    value: [0, '0', 'missing'], isMultiSelect: true,
    values: [{ key: 0, value: 'Numeric zero' }, { key: '0', value: 'Text zero' }]
  }));
  assert.match(html, /<span>Numeric zero<\/span><span>Text zero<\/span><span>missing<\/span>/);
});

test('form control props forward only the accessible identity attributes', () => {
  assert.deepEqual(formControlProps({
    id: 'visible', 'aria-label': 'Field', 'aria-labelledby': 'label',
    'aria-describedby': 'guidance', 'aria-invalid': false, credential: 'unrelated'
  }), {
    id: 'visible', 'aria-label': 'Field', 'aria-labelledby': 'label',
    'aria-describedby': 'guidance', 'aria-invalid': false
  });
});

test('settings projection preserves pending false, zero, null and schema fields without mutating resource/error inputs', () => {
  const item = {
    implementationName: 'Provider', enable: true, limit: 1,
    fields: [{ name: 'Threshold', value: 8, type: 'number', selectOptions: [{ value: 0, name: 'Zero' }] }]
  };
  const pendingChanges = { enable: false, limit: 0, empty: null, fields: { Threshold: 0 } };
  const responseJSON = [
    { propertyName: 'Enable', errorMessage: 'Check enable', isWarning: false },
    { propertyName: 'threshold', errorMessage: 'Check threshold', isWarning: true, infoLink: '/info' },
    { propertyName: null, errorMessage: 'General failure' }, null, { errorMessage: 12 }
  ];
  const before = structuredClone({ item, pendingChanges, responseJSON });
  const result = selectSettings(item, pendingChanges, { status: 400, responseJSON });
  assert.equal(result.hasPendingChanges, true);
  assert.equal(result.settings.implementationName, 'Provider');
  assert.equal(result.settings.enable.value, false);
  assert.equal(result.settings.enable.previousValue, true);
  assert.deepEqual(result.settings.enable.errors, [{ message: 'Check enable', link: undefined, detailedMessage: undefined }]);
  assert.equal(result.settings.limit.value, 0);
  assert.equal(result.settings.empty.value, null);
  const [field] = result.settings.fields;
  assert.equal(field.name, 'Threshold');
  assert.equal(field.pending, true);
  assert.equal(field.value, 0);
  assert.equal(field.previousValue, 8);
  assert.equal(field.warnings[0].link, '/info');
  assert.deepEqual(field.selectOptions, item.fields[0].selectOptions);
  assert.equal(result.validationErrors[0].errorMessage, 'General failure');
  assert.deepEqual(result.validationWarnings, []);
  assert.deepEqual({ item, pendingChanges, responseJSON }, before);
});

test('missing settings and malformed validation bodies cannot crash draft rendering', () => {
  const missing = selectSettings(undefined, { limit: 0, enable: false }, { status: 400, responseJSON: { error: 'invalid' } });
  assert.equal(missing.settings.limit.value, 0);
  assert.equal(missing.settings.enable.value, false);
  assert.deepEqual(missing.validationErrors, []);
  const selectSection = createSettingsSectionSelector('general');
  assert.equal(selectSection({ settings: {} }).hasSettings, false);

  const provider = createProviderSettingsSelector('providers');
  const state = {
    settings: {
      providers: {
        schema: [{ implementationName: 'Available' }],
        selectedSchema: { implementationName: 'Chosen', fields: [{ name: 'size', value: 0 }] },
        isSchemaPopulated: true,
        items: [{ id: 4, name: 'Saved' }],
        pendingChanges: {}
      }
    }
  };
  assert.equal(provider(state, { id: 0 }).item.implementationName, 'Chosen');
  assert.equal(provider(state, { id: 0 }).item.fields[0].value, 0);
  assert.equal(provider(state, { id: 4 }).item.name.value, 'Saved');
  assert.equal(provider(state, { id: 9 }).hasSettings, false);
});
