import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function browserHistory(snapshot) {
  const origin = 'http://localhost';
  const entries = snapshot?.entries.slice() ?? [{ url: new URL('/base/start', origin), state: null }];
  let index = snapshot?.index ?? 0;
  const browser = new EventTarget();
  Object.assign(browser, {
    document: { createElement: () => ({}) },
    navigator: { userAgent: 'Synthetic Browser' }
  });
  Object.defineProperty(browser, 'location', { get: () => entries[index].url });
  browser.snapshot = () => ({ entries, index });
  browser.history = {
    get state() { return entries[index].state; },
    get length() { return entries.length; },
    pushState(state, _, path) {
      entries.splice(index + 1, entries.length, { url: new URL(path, origin), state });
      index += 1;
    },
    replaceState(state, _, path) {
      const url = path === undefined ? entries[index].url : new URL(path, origin);
      entries[index] = { url, state };
    },
    go(delta) {
      const nextIndex = index + delta;

      if (nextIndex < 0 || nextIndex >= entries.length) {
        return;
      }

      index = nextIndex;
      const event = new Event('popstate');
      Object.defineProperty(event, 'state', { value: this.state });
      browser.dispatchEvent(event);
    }
  };

  return browser;
}

function historyFixture(t, browser = browserHistory()) {
  globalThis.window = browser;
  const { default: createAppHistory } = loadFrontendModule('App/createAppHistory.js');
  const history = createAppHistory();
  const transitions = [];
  const prompts = [];
  const unlisten = history.listen((location, action) => transitions.push({ location, action }));
  const unsubscribe = history.unsavedChanges.listen((open) => prompts.push(open));
  t.after(() => {
    globalThis.window = browser;
    unsubscribe();
    unlisten();
    delete globalThis.window;
  });

  return { browser, history, transitions, prompts };
}

test('confirmation completes the original push or replace once, with search, hash and state', (t) => {
  const { browser, history, transitions, prompts } = historyFixture(t);
  let discarded = 0;
  history.unsavedChanges.register(() => { discarded += 1; });
  const destination = { pathname: '/base/books', search: '?page=2', hash: '#recording', state: { from: 'settings' } };

  history.push(destination);
  assert.equal(prompts.at(-1), true);
  assert.equal(browser.location.pathname, '/base/start');
  history.unsavedChanges.resolve(false);
  assert.equal(discarded, 0);
  assert.equal(transitions.length, 0);

  history.replace(destination);
  history.unsavedChanges.resolve(true);
  history.unsavedChanges.resolve(true);
  assert.equal(discarded, 1);
  assert.equal(transitions.length, 1);
  assert.equal(transitions[0].action, 'REPLACE');
  assert.equal(browser.history.length, 1);
  assert.equal(browser.location.pathname, destination.pathname);
  assert.equal(browser.location.search, destination.search);
  assert.equal(browser.location.hash, destination.hash);
  assert.deepEqual(history.location.state, destination.state);
});

test('cancel restores the current browser entry and confirm completes the intended forward POP', (t) => {
  const { browser, history, transitions, prompts } = historyFixture(t);
  history.push('/base/settings?section=client#form');
  history.push('/base/books?page=2#recording');
  history.goBack();
  const original = browser.location.href;
  const originalKey = history.location.key;
  history.unsavedChanges.register();

  history.goBack();
  assert.equal(prompts.at(-1), true);
  assert.equal(history.location.key, originalKey);
  history.unsavedChanges.resolve(false);
  assert.equal(browser.location.href, original);
  assert.equal(history.location.key, originalKey);

  const count = transitions.length;
  history.goForward();
  assert.equal(prompts.at(-1), true);
  history.unsavedChanges.resolve(true);
  assert.equal(transitions.length, count + 1);
  assert.equal(transitions.at(-1).action, 'POP');
  assert.equal(browser.location.href, 'http://localhost/base/books?page=2#recording');
  assert.equal(browser.history.length, 3);
});

test('dirty forms share unload protection and the last cleanup cancels an unresolved navigation', (t) => {
  const { browser, history, prompts } = historyFixture(t);
  const original = '/base/settings';
  history.push(original);
  const unregisterParent = history.unsavedChanges.register();
  const unregisterEditor = history.unsavedChanges.register();
  function isUnloadBlocked() {
    const event = new Event('beforeunload', { cancelable: true });
    Object.defineProperty(event, 'returnValue', { value: '', writable: true });

    return !browser.dispatchEvent(event);
  }

  assert.equal(isUnloadBlocked(), true);
  history.goBack();
  assert.equal(prompts.at(-1), true);
  unregisterEditor();
  assert.equal(isUnloadBlocked(), true);
  assert.equal(prompts.at(-1), true);
  unregisterParent();
  assert.equal(prompts.at(-1), false);
  assert.equal(isUnloadBlocked(), false);
  assert.equal(browser.location.pathname, original);
  history.goBack();
  assert.equal(history.location.pathname, '/base/start');
});

test('after reload cancellation can restore a native entry outside the new router session', (t) => {
  const previous = historyFixture(t);
  previous.history.push('/base/settings?section=client#form');
  previous.history.push('/base/books?page=2#recording');
  previous.history.goBack();
  const { browser, history, prompts } = historyFixture(t, browserHistory(previous.browser.snapshot()));
  const original = browser.location.href;
  history.unsavedChanges.register();

  history.goBack();
  assert.equal(browser.location.pathname, '/base/start');
  assert.equal(prompts.at(-1), true);
  history.unsavedChanges.resolve(false);
  assert.equal(browser.location.href, original);
  assert.equal(history.location.pathname, '/base/settings');

  history.goForward();
  assert.equal(browser.location.pathname, '/base/books');
  assert.equal(prompts.at(-1), true);
  history.unsavedChanges.resolve(false);
  assert.equal(browser.location.href, original);
  assert.equal(browser.history.length, 3);
});

test('a newer browser traversal supersedes the pending one without replaying stale history', (t) => {
  const { browser, history, prompts } = historyFixture(t);
  history.push('/base/other');
  history.push('/base/settings');
  history.unsavedChanges.register();

  history.goBack();
  history.goForward();
  assert.equal(prompts.at(-1), false);
  assert.equal(history.location.pathname, '/base/settings');

  history.goBack();
  history.goBack();
  history.unsavedChanges.resolve(true);
  assert.equal(history.location.pathname, '/base/start');
  assert.equal(browser.location.pathname, '/base/start');
});

test('download client modal marks changed schema fields dirty without treating reversions as edits', () => {
  const { default: selectState } = loadFrontendModule('Settings/DownloadClients/DownloadClients/EditDownloadClientModalConnector.js', {
    'react-redux': { connect: (mapState) => () => mapState },
    'Settings/PendingChangesModal': () => null,
    'Settings/useUnsavedNavigation': () => null,
    'Store/Actions/baseActions': {},
    'Store/Actions/settingsActions': {},
    './EditDownloadClientModal': () => null
  });
  const saved = { id: 2, name: 'Client', enable: false, fields: [{ name: 'port', value: 0 }] };
  const state = (pendingChanges) => ({ settings: { downloadClients: { items: [saved], pendingChanges } } });
  const isDirty = (changes, isOpen = true) => selectState(state(changes), { id: 2, isOpen }).isDirty;

  assert.equal(isDirty({ name: 'Changed' }, false), false);
  assert.equal(isDirty({ name: 'Client', enable: false, fields: { port: 0 } }), false);
  assert.equal(isDirty({ enable: true }), true);
  assert.equal(isDirty({ fields: { port: 80 } }), true);
});
