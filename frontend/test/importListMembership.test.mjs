import assert from 'node:assert/strict';
import test from 'node:test';

import React from 'react';

import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function ImportListMembership() {
  return null;
}
const boundaries = Object.fromEntries([
  'AddAuthor/AuthorMonitorNewItemsOptionsPopoverContent', 'Components/Alert', 'Components/DescriptionList/DescriptionList',
  'Components/DescriptionList/DescriptionListItem', 'Components/FieldSet', 'Components/Form/Form',
  'Components/Form/FormGroup', 'Components/Form/FormInputGroup', 'Components/Form/FormLabel',
  'Components/Form/ProviderFieldFormGroup', 'Components/Icon', 'Components/Link/Button', 'Components/Link/SpinnerErrorButton',
  'Components/Loading/LoadingIndicator', 'Components/Modal/ModalBody', 'Components/Modal/ModalContent',
  'Components/Modal/ModalFooter', 'Components/Modal/ModalHeader', 'Components/Tooltip/Popover', 'Settings/AdvancedSettingsButton'
].map((name) => [name, () => null]));

const [{ default: renderModal }, { default: selectSettings }] = loadFrontendModule([
  'Settings/ImportLists/ImportLists/EditImportListModalContent.tsx', 'Store/Selectors/selectSettings.ts'
], {
  ...boundaries,
  react: { ...React, useState: (value) => [value, () => {}] },
  'Helpers/Props': { icons: {}, inputTypes: {}, kinds: {}, tooltipPositions: {} },
  'Utilities/Date/formatShortTimeSpan': (value) => value,
  'Utilities/requestAction': () => {},
  'Utilities/String/translate': (key) => key,
  './ImportListMembership': ImportListMembership
});

function membershipPanels(element) {
  if (!React.isValidElement(element)) {
    return [];
  }
  return element.type === ImportListMembership
    ? [element]
    : React.Children.toArray(element.props.children).flatMap(membershipPanels);
}

function render(id, itemId) {
  const item = selectSettings({ id: itemId, name: 'Synthetic list', minRefreshInterval: '01:00:00' }, {}, null).settings;
  const noop = () => {};
  return renderModal({
    id, item, isFetching: false, advancedSettings: false, isSaving: false, isTesting: false, showMetadataProfile: false,
    onInputChange: noop, onFieldChange: noop, onModalClose: noop, onSavePress: noop, onTestPress: noop,
    onAdvancedSettingsPress: noop
  });
}

test('passes the saved numeric provider id rather than the wrapped form setting to membership', () => {
  const panels = membershipPanels(render(301, 301));
  assert.equal(panels.length, 1);
  assert.equal(panels[0].props.id, 301);
});

test('does not show persisted membership on an unsaved provider schema', () => {
  assert.deepEqual(membershipPanels(render(undefined, 0)), []);
});
