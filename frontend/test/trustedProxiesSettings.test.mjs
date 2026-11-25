import assert from 'node:assert/strict';
import test from 'node:test';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const props = {
  inputTypes: { TEXT: 'text', NUMBER: 'number', CHECK: 'check' },
  kinds: { DANGER: 'danger' },
  sizes: { SMALL: 'small', MEDIUM: 'medium', all: ['small', 'medium'] }
};
const translate = (key) => key;
let renderedInput;
function FormInputGroup(props) {
  renderedInput = props;
  return null;
}
const FormLabel = () => null;
const { default: FormGroup } = loadFrontendModule('Components/Form/FormGroup.tsx', {
  'Helpers/Props': props,
  'Helpers/elementChildren': { map: React.Children.map },
  './FormGroup.css': { group: 'group' },
  './FormInputGroup': FormInputGroup,
  './FormLabel': FormLabel
});
const { default: HostSettings } = loadFrontendModule('Settings/General/HostSettings.js', {
  'Components/FieldSet': () => null,
  'Components/Form/FormGroup': FormGroup,
  'Components/Form/FormInputGroup': FormInputGroup,
  'Components/Form/FormLabel': FormLabel,
  'Helpers/Props': props,
  'Utilities/String/translate': translate
});

function trustedProxyGroup(advancedSettings, trustedNetworks, onInputChange) {
  const view = HostSettings({
    advancedSettings,
    settings: { enableSsl: { value: false }, trustedNetworks },
    isWindows: false,
    mode: 'console',
    onInputChange
  });
  return React.Children.toArray(view.props.children).find((group) =>
    group.type === FormGroup && React.Children.toArray(group.props.children).some((input) =>
      input.type === FormInputGroup && input.props.name === 'trustedNetworks'
    )
  );
}

test('trusted proxy input is advanced and preserves values and API validation errors', () => {
  const validationErrors = [{ errorMessage: 'Enter an IP address or canonical CIDR network.' }];
  const setting = { value: '192.0.2.10,2001:db8::10', errors: validationErrors };
  const hidden = trustedProxyGroup(false, setting, () => {});
  assert.ok(hidden);
  assert.equal(renderToStaticMarkup(hidden), '');

  renderToStaticMarkup(trustedProxyGroup(true, setting, () => {}));
  assert.equal(renderedInput.name, 'trustedNetworks');
  assert.equal(renderedInput.value, setting.value);
  assert.equal(renderedInput.errors, validationErrors);
  assert.equal(renderedInput.helpTextWarning, 'RestartRequiredHelpTextWarning');
});

test('trusted proxy input sends an explicit empty string when cleared', () => {
  const changes = [];
  const group = trustedProxyGroup(true, { value: '192.0.2.10' }, (change) => changes.push(change));
  const input = React.Children.toArray(group.props.children).find((child) => child.type === FormInputGroup);

  input.props.onChange({ name: input.props.name, value: '' });

  assert.deepEqual(changes, [{ name: 'trustedNetworks', value: '' }]);
});

const generalBoundaries = Object.fromEntries([
  'Components/Alert', 'Components/Form/Form', 'Components/Loading/LoadingIndicator',
  'Components/Modal/ConfirmModal', 'Components/Page/PageContent', 'Components/Page/PageContentBody',
  'Settings/SettingsToolbarConnector', './BackupSettings', './HostSettings', './LoggingSettings',
  './ProxySettings', './SecuritySettings', './UpdateSettings'
].map((name) => [name, () => null]));
const { default: GeneralSettings } = loadFrontendModule('Settings/General/GeneralSettings.js', {
  ...generalBoundaries,
  'Helpers/Props': props,
  'Utilities/String/translate': translate
});

function finishSave(before, after, overrides = {}) {
  const current = {
    isSaving: false,
    isResettingApiKey: false,
    saveError: null,
    settings: { trustedNetworks: { value: after } },
    ...overrides
  };
  const component = new GeneralSettings(current);
  component.setState = (update) => { component.state = { ...component.state, ...update }; };
  component.componentDidUpdate({
    ...current,
    isSaving: true,
    settings: { trustedNetworks: { previousValue: before, value: after } }
  });
  return component;
}

test('saving changed or cleared trusted proxies prompts for a restart', () => {
  assert.equal(finishSave('', '192.0.2.10').state.isRestartRequiredModalOpen, true);
  assert.equal(finishSave('192.0.2.10', '').state.isRestartRequiredModalOpen, true);
});

test('unchanged, rejected and unfinished proxy saves do not prompt for a restart', () => {
  assert.equal(finishSave('192.0.2.10', '192.0.2.10').state.isRestartRequiredModalOpen, false);
  assert.equal(finishSave('', 'invalid', { saveError: { status: 400 } }).state.isRestartRequiredModalOpen, false);
  assert.equal(finishSave('', '192.0.2.10', { isSaving: true }).state.isRestartRequiredModalOpen, false);
});

function finishHostSave(key, before, entered, saved) {
  const current = { isSaving: false, isResettingApiKey: false, saveError: null, settings: { [key]: { value: saved } } };
  const component = new GeneralSettings(current);
  component.setState = (update) => { component.state = { ...component.state, ...update }; };
  component.componentDidUpdate({ ...current, isSaving: true, settings: { [key]: { previousValue: before, value: entered, pending: true } } });
  return component.state.isRestartRequiredModalOpen;
}

test('replacing a TLS password prompts to restart even though saved passwords are always masked', () => {
  assert.equal(finishHostSave('sslCertPassword', '********', 'synthetic-new-password', '********'), true);
  assert.equal(finishHostSave('sslCertPassword', '********', '********', '********'), false);
  assert.equal(finishHostSave('sslCertPassword', '********', '', ''), true);
  assert.equal(finishHostSave('sslKeyPath', '', '/synthetic/key.pem', '/synthetic/key.pem'), true);
  assert.equal(finishHostSave('performanceProfilingEnabled', false, true, true), true);
  assert.equal(finishHostSave('performanceProfilingEnabled', true, false, false), true);
});
