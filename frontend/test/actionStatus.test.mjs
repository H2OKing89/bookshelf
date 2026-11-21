import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { renderToStaticMarkup } = createRequire(import.meta.url)('react-dom/server');
const translate = (key) => key;
const { default: ActionStatus } = loadFrontendModule('Components/ActionStatus', {
  'Utilities/String/translate': translate
});

const SpinnerButton = () => null;
const FormInputButton = () => null;
const boundaries = {
  'Components/ActionStatus': ActionStatus,
  'Components/Form/FormInputButton': FormInputButton,
  'Components/Icon': () => null,
  'Components/Link/SpinnerButton': SpinnerButton,
  'Helpers/Props': { icons: {}, kinds: { all: ['default'] } },
  'Utilities/String/translate': translate
};
const [
  { default: SpinnerErrorButton },
  { default: ClipboardButton }
] = loadFrontendModule([
  'Components/Link/SpinnerErrorButton',
  'Components/Link/ClipboardButton'
], boundaries);

function mount(Component, props) {
  const component = new Component({ ...Component.defaultProps, ...props });
  component.setState = (next) => Object.assign(component.state, next);
  component.componentDidMount?.();
  return component;
}

function updateTest(button, isSpinning, error) {
  const previous = button.props;
  button.props = { ...previous, isSpinning, error };
  button.componentDidUpdate(previous);
}

function renderStatus(button) {
  return renderToStaticMarkup(button.render().props.children[1]);
}

function assertAnnouncement(button, role, message) {
  const html = renderStatus(button);
  assert.ok(html.includes('role="' + role + '" aria-atomic="true"><span>' + message + '</span>'));
  const otherRole = role === 'status' ? 'alert' : 'status';
  assert.ok(html.includes('role="' + otherRole + '" aria-atomic="true"></span>'));
  assert.equal(html.split(message).length, 2);
}

function assertEmpty(button) {
  assert.equal(renderStatus(button), '<span role="status" aria-atomic="true"></span>' +
    '<span role="alert" aria-atomic="true"></span>');
}

function mockFeedbackClock(context) {
  let now = 0;
  const pending = new Map();
  context.mock.method(globalThis, 'setTimeout', (callback, delay) => {
    const handle = Symbol();
    pending.set(handle, { callback, due: now + delay });
    return handle;
  });
  context.mock.method(globalThis, 'clearTimeout', (handle) => pending.delete(handle));

  return (elapsed) => {
    now += elapsed;
    for (const [handle, { callback, due }] of pending) {
      if (due <= now) {
        pending.delete(handle);
        callback();
      }
    }
  };
}

function mockNavigator(context, value) {
  const previous = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value });
  context.after(() => {
    if (previous) {
      Object.defineProperty(globalThis, 'navigator', previous);
    } else {
      delete globalThis.navigator;
    }
  });
}

test('test progress and results use one live region and replace expired feedback', (context) => {
  const tick = mockFeedbackClock(context);
  const button = mount(SpinnerErrorButton, { action: 'test', isSpinning: false, children: 'Test' });
  assertEmpty(button);
  updateTest(button, true);
  const control = button.render().props.children[0];
  assert.equal(control.type, SpinnerButton);
  assert.equal(control.props['aria-label'], 'Test');
  assert.equal(control.props['aria-busy'], true);
  assertAnnouncement(button, 'status', 'TestingConnection');
  updateTest(button, false);
  assertAnnouncement(button, 'status', 'TestSucceeded');

  tick(2000);
  updateTest(button, true);
  updateTest(button, false, { status: 400, responseJSON: [{ isWarning: true }] });
  tick(1000);
  assertAnnouncement(button, 'status', 'TestCompletedWithWarnings');
  tick(2000);
  assertEmpty(button);

  updateTest(button, true);
  updateTest(button, false, { status: 400, responseJSON: [{ isWarning: true }, { isWarning: false }] });
  assertAnnouncement(button, 'alert', 'TestFailed');
  button.componentWillUnmount();
  button.setState = () => assert.fail('unmounted button received a timer update');
  tick(3000);
});

test('aborted actions stay silent and save buttons leave the successful announcement to the app', (context) => {
  mockFeedbackClock(context);
  const button = mount(SpinnerErrorButton, { action: 'save', isSpinning: false, children: 'Save' });
  updateTest(button, true);
  assertAnnouncement(button, 'status', 'SavingSettings');
  updateTest(button, false, { aborted: true });
  assertEmpty(button);
  updateTest(button, true);
  updateTest(button, false, { status: 500 });
  assertAnnouncement(button, 'alert', 'SaveFailed');
  updateTest(button, true);
  updateTest(button, false);
  assertEmpty(button);
  button.componentWillUnmount();
});

test('clipboard reports resolved writes and permission failures without exposing the copied value', async (context) => {
  const tick = mockFeedbackClock(context);
  const clipboardText = 'fixture clipboard value';
  let resolveWrite;
  const writeText = context.mock.fn(() => new Promise((resolve) => { resolveWrite = resolve; }));
  mockNavigator(context, { clipboard: { writeText } });
  const button = mount(ClipboardButton, { value: clipboardText });
  assert.equal(button.render().props.children[0].props['aria-label'], 'CopyToClipboard');
  const write = button.onCopyPress({ currentTarget: {} });
  assertAnnouncement(button, 'status', 'CopyToClipboardInProgress');
  await button.onCopyPress({ currentTarget: {} });
  assert.equal(writeText.mock.callCount(), 1);
  assert.deepEqual(writeText.mock.calls[0].arguments, [clipboardText]);
  resolveWrite();
  await write;
  assertAnnouncement(button, 'status', 'CopiedToClipboard');
  assert.ok(!renderStatus(button).includes(clipboardText));
  tick(3000);
  assertEmpty(button);

  writeText.mock.mockImplementation(() => Promise.reject(new Error('permission denied')));
  await button.onCopyPress({ currentTarget: {} });
  assertAnnouncement(button, 'alert', 'CopyToClipboardError');
  button.props = { ...button.props, isDisabled: true };
  await button.onCopyPress({ currentTarget: {} });
  assert.equal(writeText.mock.callCount(), 2);
  button.componentWillUnmount();
  button.setState = () => assert.fail('unmounted clipboard received a timer update');
  tick(3000);
});

test('legacy clipboard failure removes the temporary input and restores keyboard focus', async (context) => {
  mockFeedbackClock(context);
  mockNavigator(context, {});
  const operations = [];
  const input = {
    select: () => operations.push('select'),
    remove: () => operations.push('remove'),
    setAttribute: () => {}
  };
  const trigger = {
    parentElement: { appendChild: () => operations.push('append') },
    focus: (options) => {
      assert.equal(options.preventScroll, true);
      operations.push('focus');
    }
  };
  Object.defineProperty(globalThis, 'document', { configurable: true, value: {
    createElement: () => input,
    execCommand: (command) => {
      operations.push(command);
      return false;
    }
  } });
  context.after(() => { delete globalThis.document; });

  const button = mount(ClipboardButton, { value: 'fixture clipboard value' });
  await button.onCopyPress({ currentTarget: trigger });
  assertAnnouncement(button, 'alert', 'CopyToClipboardError');
  assert.deepEqual(operations, ['append', 'select', 'copy', 'remove', 'focus']);
  button.componentWillUnmount();
});

test('save handler announces the fulfilled save after committing the new settings', () => {
  const effects = [];
  const callbacks = {};
  const { default: createSave } = loadFrontendModule('Store/Actions/Creators/createSaveProviderHandler', {
    'Components/ActionStatus': { announceSettingsSaved: () => effects.push('announcement') },
    'Utilities/createAjaxRequest': () => ({ request: {
      done: (handler) => { callbacks.done = handler; },
      fail: (handler) => { callbacks.fail = handler; }
    } }),
    'Utilities/State/getProviderState': () => ({ id: 1 }),
    'jquery': { param: () => '' },
    'redux-batched-actions': { batchActions: (actions) => actions },
    '../baseActions': { set: (data) => data, updateItem: (data) => data }
  });
  const save = createSave('settings.notifications', '/notification');
  const dispatch = (action) => effects.push(action);
  save(() => {}, { id: 1 }, dispatch);
  callbacks.fail({ status: 400 });
  assert.ok(!effects.includes('announcement'));
  save(() => {}, { id: 1 }, dispatch);
  callbacks.done({ id: 1 });
  assert.equal(effects.at(-2).at(-1).isSaving, false);
  assert.equal(effects.at(-1), 'announcement');
  assert.equal(effects.filter((effect) => effect === 'announcement').length, 1);
});
