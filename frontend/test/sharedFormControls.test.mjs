import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { toggleSelectedKey, isOptionDisabled, optionLabel } = loadFrontendModule('Components/Form/enhancedSelectTypes.ts');
const { resolveTag } = loadFrontendModule('Components/Form/tagRenderTypes.ts');
const { providerSelectOptions, editionOptions, numericChoice, selectEdition } = loadFrontendModule('Components/Form/providerSelectOptions.ts');
const { nextEnabledOptionIndex, selectedOptionIndex } = loadFrontendModule('Components/Form/selectInputSelection.ts');

test('enhanced selections preserve exact key types and missing values across option refreshes', () => {
  const values = [{ key: 0, value: 'Zero' }, { key: '0', value: 'String' }, { key: 1, value: 'One' }];
  assert.deepEqual(toggleSelectedKey(['missing', 0], values, '0'), [0, '0', 'missing']);
  assert.deepEqual(toggleSelectedKey([0, '0', 'missing'], values, 0), ['0', 'missing']);
  assert.equal(selectedOptionIndex(0, values), 0);
  assert.equal(selectedOptionIndex('0', values), 1);
  assert.equal(selectedOptionIndex(null, values), -1);
  assert.equal(optionLabel(() => 'Translated'), 'Translated');
});

test('disabled, hidden and parent-selected options cannot be selected', () => {
  const options = [
    { key: 0, value: 'Parent' },
    { key: 1, value: 'Child', parentKey: 0 },
    { key: 2, value: 'Disabled', isDisabled: true },
    { key: 3, value: 'Hidden', isHidden: true },
    { key: 4, value: 'Other' }
  ];
  const selected = [0];
  assert.equal(isOptionDisabled(options[1], selected), true);
  assert.equal(isOptionDisabled(options[1], []), false);
  const effective = options.map((option) => ({ ...option, isDisabled: isOptionDisabled(option, selected) }));
  assert.equal(nextEnabledOptionIndex(0, 1, effective), 4);
  assert.equal(nextEnabledOptionIndex(4, -1, effective), 0);
});

test('tags retain false and zero identities when selecting highlighted suggestions', () => {
  const zero = { id: 0, name: 'Zero' };
  const disabled = { id: false, name: 'False' };
  assert.equal(resolveTag('', zero, [zero, disabled], false), zero);
  assert.equal(resolveTag('False', null, [zero, disabled], false), disabled);
  assert.equal(resolveTag('new', null, [zero], false), undefined);
  assert.deepEqual(resolveTag('new', null, [zero], true), { name: 'new' });
});

test('provider select options preserve Any zero and do not mutate store ordering', () => {
  const section = { items: [{ id: 4, name: 'Zed', protocol: 'torrent' }, { id: 2, name: 'Alpha', protocol: 'torrent' }, { id: 3, name: 'News', protocol: 'usenet' }] };
  assert.deepEqual(providerSelectOptions(section, true, 'torrent').map((item) => item.key), [0, 2, 4]);
  assert.deepEqual(section.items.map((item) => item.id), [4, 2, 3]);
  assert.equal(numericChoice(0), 0);
  assert.equal(numericChoice('0'), 0);
  assert.equal(numericChoice(null), null);
  assert.equal(numericChoice('noChange'), undefined);
  assert.equal(numericChoice('1x'), undefined);
});

test('edition selection tolerates missing monitored edition while preserving raw metadata', () => {
  const a = { foreignEditionId: 'us', title: 'Title', monitored: false, asin: 'synthetic', extra: { flag: false, count: 0 } };
  const b = { foreignEditionId: 'uk', title: 'Title UK', monitored: false };
  const draft = { value: [a, b] };
  assert.equal(editionOptions(draft).value, undefined);
  assert.equal(editionOptions(draft).values[0].value, 'Title');
  assert.equal(selectEdition(draft, 'absent'), undefined);
  assert.deepEqual(selectEdition(draft, 'us'), [{ ...a, monitored: true }, b]);
  assert.equal(a.monitored, false);
});

const mock = () => null;
const { default: AutoSuggestInput } = loadFrontendModule('Components/Form/AutoSuggestInput.tsx', {
  'Components/Portal': mock,
  'Components/Positioning/AnchoredPopup': { default: mock, popupMaxHeight: '' },
  'Utilities/getUniqueElementId': () => 'suggestion-id'
});
const { filterAutoComplete } = loadFrontendModule('Components/Form/AutoCompleteInput.tsx', { './AutoSuggestInput': mock });

test('autocomplete compares accent-insensitively and Tab completes an actual string without trapping Shift+Tab', () => {
  assert.deepEqual(filterAutoComplete(['Café', 'Books'], 'CAFE'), ['Café']);
  const calls = [];
  const component = new AutoSuggestInput({
    name: 'path',
    value: '/boo',
    suggestions: [{ path: '/books' }],
    getSuggestionValue: (suggestion) => suggestion.path,
    onChange: (change) => calls.push(change)
  });
  const event = (shiftKey) => ({ key: 'Tab', shiftKey, preventDefault: () => calls.push('prevent') });
  component.onInputKeyDown(event(true));
  assert.deepEqual(calls, []);
  component.onInputKeyDown(event(false));
  assert.deepEqual(calls, ['prevent', { name: 'path', value: '/books' }]);
});

const { updateIndexerFlags } = loadFrontendModule('Components/Form/IndexerFlagsSelectInput.tsx', {
  './EnhancedSelectInput': mock,
  'App/State/AppState': {},
  'react-redux': { useSelector: () => null }
});
test('editing known indexer flags keeps unknown bits unchanged rather than resetting them', () => {
  assert.equal(updateIndexerFlags(0, [1, 2, 4], [1]), 1);
  assert.equal(updateIndexerFlags(128 | 1, [1, 2, 4], [2]), 130);
  assert.equal(updateIndexerFlags(128 | 1, [1, 2, 4], []), 128);
});
