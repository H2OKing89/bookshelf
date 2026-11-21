import assert from 'node:assert/strict';
import test from 'node:test';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server.js';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const h = React.createElement;
const form = 'Components/Form/';
const [inputTypes, kinds, sizes] = loadFrontendModule(['Helpers/Props/inputTypes', 'Helpers/Props/kinds', 'Helpers/Props/sizes']);
const suppress = () => null;
const styles = new Proxy({}, { get: (_, key) => key === '__esModule' ? undefined : key });
const styledComponents = [
  'AutoSuggestInput', 'CheckInput', 'EnhancedSelectInput', 'EnhancedSelectInputSelectedValue',
  'FormGroup', 'FormInputGroup', 'FormInputHelpText', 'HintedSelectInputSelectedValue',
  'TagInput', 'TagInputInput', 'TextArea', 'TextInput'
];
const connectors = [
  'BookEditionSelectInputConnector',
  'BookshelfInputConnector',
  'CaptchaInputConnector',
  'DeviceInputConnector',
  'DownloadClientSelectInputConnector',
  'EnhancedSelectInputConnector',
  'IndexerFlagsSelectInput',
  'IndexerSelectInputConnector',
  'MetadataProfileSelectInputConnector',
  'OAuthInputConnector',
  'PathInputConnector',
  'QualityProfileSelectInputConnector',
  'RootFolderSelectInputConnector',
  'TagInputConnector',
  'TagSelectInputConnector',
  'TextTagInputConnector'
];

const boundaries = {
  ...Object.fromEntries(styledComponents.map((name) => [`./${name}.css`, styles])),
  ...Object.fromEntries(connectors.map((name) => [`./${name}`, suppress])),
  'Helpers/Props': { inputTypes, kinds, sizes, icons: {}, scrollDirections: {} },
  'Helpers/Props/Shapes/tagShape': {},
  'Helpers/elementChildren': { map: React.Children.map },
  'Utilities/Author/monitorOptions': [{ key: 'all', value: 'All' }],
  'Utilities/Author/monitorNewItemsOptions': [],
  'Utilities/String/translate': (key) => key,
  'Utilities/browser': { isMobile: () => false },
  'Components/FileBrowser/FileBrowserModal': suppress,
  'Components/Icon': suppress,
  'Components/Label': suppress,
  'Components/Link/Button': suppress,
  'Components/Link/IconButton': suppress,
  'Components/Link/SpinnerButton': suppress,
  'Components/Link/Link': ({ children, className, id, ...props }) => {
    const aria = Object.fromEntries(Object.entries(props).filter(([key]) => key.startsWith('aria-')));
    return h('button', { className, id, ...aria }, children);
  },
  'Components/Loading/LoadingIndicator': suppress,
  'Components/Measure': ({ children }) => children,
  'Components/Modal/Modal': suppress,
  'Components/Modal/ModalBody': suppress,
  'Components/Portal': suppress,
  'Components/Scroller/Scroller': suppress,
  'react-popper': { Manager: ({ children }) => children, Reference: ({ children }) => children({ ref: () => {} }), Popper: suppress }
};

const [FormGroup, FormLabel, FormInputGroup, TagInput, PathInput] = loadFrontendModule(
  ['FormGroup', 'FormLabel', 'FormInputGroup', 'TagInput', 'PathInput'].map((name) => form + name),
  boundaries
).map((module) => module.default);

function row(label, props, labelProps) {
  return h(FormGroup, null,
    h(FormLabel, labelProps, label),
    h(FormInputGroup, { onChange: () => {}, ...props }));
}

function tags(html, tagName) {
  return Array.from(html.matchAll(new RegExp(`<${tagName}\\b[^>]*>`, 'g')), ([tag]) => tag);
}

function attribute(tag, key) {
  return new RegExp(`\\b${key}="([^"]*)"`).exec(tag)?.[1];
}

test('duplicate field names get unique controls and bind only the visible messages', () => {
  const html = renderToStaticMarkup(h(React.Fragment, null,
    h('p', { id: 'additional' }, 'Additional guidance'),
    row('First name', {
      name: 'name',
      value: '',
      helpText: 'Primary guidance',
      helpTexts: ['Additional local guidance'],
      errors: [{ message: 'Enter a name.' }],
      warnings: [{ message: 'A warning.' }],
      'aria-describedby': 'additional additional'
    }),
    row('Second name', { name: 'name', value: '', id: 'custom-name' }, { htmlFor: 'custom-name' }),
    h(FormGroup, { isAdvanced: true },
      h(FormLabel, null, 'Hidden'),
      h(FormInputGroup, { name: 'hidden', value: '', onChange: () => {} }))));
  const [first, second] = tags(html, 'input');
  const labels = tags(html, 'label');
  const id = attribute(first, 'id');
  assert.ok(id);
  assert.equal(attribute(labels[0], 'for'), id);
  assert.equal(attribute(labels[1], 'for'), 'custom-name');
  assert.equal(attribute(second, 'id'), 'custom-name');
  assert.equal(attribute(first, 'aria-invalid'), 'true');
  assert.equal(attribute(first, 'aria-describedby'), `additional ${id}-help ${id}-help-0 ${id}-error-0 ${id}-warning-0`);
  assert.equal(attribute(second, 'aria-invalid'), undefined);
  for (const message of attribute(first, 'aria-describedby').split(' ')) {
    assert.ok(html.includes(`id="${message}"`));
  }
  assert.doesNotMatch(html, /Hidden/);
});

test('the native password, checkbox, textarea, select and tag controls keep their field semantics', () => {
  const html = renderToStaticMarkup(h(React.Fragment, null,
    row('Password', { name: 'password', value: '', type: inputTypes.PASSWORD, helpText: 'Optional credentials.' }),
    row('Use catalog', { name: 'enabled', value: false, type: inputTypes.CHECK, helpText: 'Use the metadata catalog.' }),
    row('Narrators', { name: 'narrators', value: '', type: inputTypes.TEXT_AREA, helpText: 'One narrator per line.' }),
    row('Monitor', { name: 'monitor', value: 'all', type: inputTypes.MONITOR_BOOKS_SELECT, helpText: 'Which recordings to monitor.' }),
    h(TagInput, {
      id: 'tags', name: 'tags', tags: [], tagList: [], 'aria-label': 'Tags',
      'aria-describedby': 'tag-help', 'aria-invalid': true,
      onTagAdd: () => {}, onTagDelete: () => {}
    })));
  const controls = [...tags(html, 'input'), ...tags(html, 'textarea'), ...tags(html, 'select')];
  for (const field of ['password', 'enabled', 'narrators', 'monitor', 'tags']) {
    const control = controls.find((candidate) => attribute(candidate, 'name') === field);
    const id = attribute(control, 'id');
    assert.ok(id);
    assert.equal(attribute(control, 'aria-describedby'), field === 'tags' ? 'tag-help' : `${id}-help`);
    if (field !== 'tags') {
      assert.ok(tags(html, 'label').some((label) => attribute(label, 'for') === id));
    }
  }
  assert.ok(tags(html, 'input').some((control) => attribute(control, 'type') === 'password'));
  assert.ok(tags(html, 'input').some((control) => attribute(control, 'id') === 'tags' && attribute(control, 'aria-invalid') === 'true'));
  assert.doesNotMatch(controls.find((control) => attribute(control, 'name') === 'enabled'), /checked=/);
});

test('an enhanced select labels its activator and includes the selected value', () => {
  const html = renderToStaticMarkup(row('Delivery method', {
    name: 'method',
    type: inputTypes.SELECT,
    value: 0,
    values: [{ key: 0, value: 'Default' }, { key: 1, value: 'Alternate' }],
    helpText: 'Choose a delivery method.'
  }));
  const [activator] = tags(html, 'button');
  const [label] = tags(html, 'label');
  const id = attribute(activator, 'id');
  assert.equal(attribute(label, 'for'), id);
  assert.equal(attribute(activator, 'aria-describedby'), `${id}-help`);
  const [fieldLabel, valueLabel] = attribute(activator, 'aria-labelledby').split(' ');
  assert.equal(fieldLabel, attribute(label, 'id'));
  assert.match(html, new RegExp(`id="${valueLabel}"[^>]*><div[^>]*>Default</div>`));
});

test('a path editor can leave with Tab when completion has nothing to change', () => {
  const events = [];
  const existingDocument = globalThis.document;
  globalThis.document = { getElementById: () => null };
  try {
    const field = new PathInput({
      name: 'path', value: '/library', paths: [],
      onChange: (value) => events.push(value),
      onFetchPaths: (path) => events.push(path)
    });
    const event = { key: 'Tab', preventDefault() { events.push('prevented'); } };
    field.onInputKeyDown(event);
    field.props = { ...field.props, paths: [{ path: '/library', type: 'folder' }] };
    field.onInputKeyDown(event);
    field.props = { ...field.props, paths: [{ path: '/library/audiobooks', type: 'folder' }] };
    field.onInputKeyDown({ ...event, shiftKey: true });
    assert.deepEqual(events, []);
    field.onInputKeyDown(event);
    assert.deepEqual(events, ['prevented', { name: 'path', value: '/library/audiobooks' }, '/library/audiobooks']);
  } finally {
    if (existingDocument === undefined) {
      delete globalThis.document;
    } else {
      globalThis.document = existingDocument;
    }
  }
});
