import assert from 'node:assert/strict';
import test from 'node:test';
import { createBrowserRouter } from 'react-router-dom';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { createUnsavedChanges } = loadFrontendModule('Settings/UnsavedNavigationProvider.js', {
  './PendingChangesModal': () => null
});
const { default: migrateBrowserHistory } = loadFrontendModule('App/migrateBrowserHistory.js');

function browserHistory(snapshot) {
  const origin = 'http://localhost';
  const entries = snapshot?.entries.slice() ?? [{ url: new URL('/base/start', origin), state: null }];
  let index = snapshot?.index ?? 0;
  const browser = new EventTarget();
  browser.document = { createElement: () => ({}) };
  browser.sessionStorage = { getItem: () => null };
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
      browser.dispatchEvent(new Event('popstate'));
    }
  };

  return browser;
}

function routerFixture(t, browser = browserHistory()) {
  const updateEntry = migrateBrowserHistory(browser);
  const router = createBrowserRouter([{ path: '*' }], { basename: '/base', window: browser, hydrationData: {} });
  const transitions = [];
  const dirtyTransitions = [];
  const changes = createUnsavedChanges(browser, (dirty) => dirtyTransitions.push(dirty));
  let location = router.state.location;
  router.subscribe((state) => {
    if (location !== state.location) {
      location = state.location;
      transitions.push(state.historyAction);
      updateEntry?.();
    }
  });
  const blockerKey = 'unsaved';
  router.getBlocker(blockerKey, changes.hasChanges);
  t.after(() => router.dispose());

  return { browser, router, changes, transitions, dirtyTransitions, blocker: () => router.state.blockers.get(blockerKey) };
}

test('one basename and the original push or replace preserve query, hash and state', async (t) => {
  const { browser, router, changes, transitions, blocker } = routerFixture(t);
  let discarded = 0;
  changes.register(() => { discarded += 1; });
  const destination = '/books?page=2#recording';
  const state = { from: 'settings' };

  await router.navigate(destination, { state });
  assert.equal(blocker().state, 'blocked');
  assert.equal(browser.location.pathname, '/base/start');
  blocker().reset();
  assert.equal(discarded, 0);
  assert.deepEqual(transitions, []);

  await router.navigate(destination, { state, replace: true });
  changes.discard();
  blocker().proceed();
  assert.equal(discarded, 1);
  assert.deepEqual(transitions, ['REPLACE']);
  assert.equal(browser.history.length, 1);
  assert.equal(browser.location.href, 'http://localhost/base/books?page=2#recording');
  assert.deepEqual(router.state.location.state, state);
});

test('cancel restores the edited entry and confirm completes the requested Forward', async (t) => {
  const { browser, router, changes, transitions, blocker } = routerFixture(t);
  await router.navigate('/settings?section=client#form');
  await router.navigate('/books?page=2#recording');
  await router.navigate(-1);
  const original = browser.location.href;
  const originalKey = router.state.location.key;
  changes.register();

  await router.navigate(-1);
  assert.equal(blocker().state, 'blocked');
  assert.equal(router.state.location.key, originalKey);
  blocker().reset();
  assert.equal(browser.location.href, original);

  const count = transitions.length;
  await router.navigate(1);
  assert.equal(blocker().state, 'blocked');
  blocker().proceed();
  await Promise.resolve();
  assert.equal(transitions.length, count + 1);
  assert.equal(transitions.at(-1), 'POP');
  assert.equal(browser.location.href, 'http://localhost/base/books?page=2#recording');
  assert.equal(browser.history.length, 3);
});

test('the last dirty form removes the shared reload warning', (t) => {
  const { browser, changes, dirtyTransitions } = routerFixture(t);
  const unregisterParent = changes.register();
  const unregisterEditor = changes.register();
  function isUnloadBlocked() {
    const event = new Event('beforeunload', { cancelable: true });
    Object.defineProperty(event, 'returnValue', { value: '', writable: true });

    return !browser.dispatchEvent(event);
  }

  assert.equal(isUnloadBlocked(), true);
  unregisterEditor();
  assert.equal(isUnloadBlocked(), true);
  assert.equal(changes.hasChanges(), true);
  unregisterParent();
  assert.equal(isUnloadBlocked(), false);
  assert.equal(changes.hasChanges(), false);
  assert.deepEqual(dirtyTransitions, [true, false]);
});

test('Back and Forward can be cancelled after reloading a data-router entry', async (t) => {
  const previous = routerFixture(t);
  await previous.router.navigate('/settings?section=client#form');
  await previous.router.navigate('/books?page=2#recording');
  await previous.router.navigate(-1);
  const { browser, router, changes, blocker } = routerFixture(t, browserHistory(previous.browser.snapshot()));
  const original = browser.location.href;
  changes.register();

  await router.navigate(-1);
  assert.equal(blocker().state, 'blocked');
  blocker().reset();
  assert.equal(browser.location.href, original);
  await router.navigate(1);
  assert.equal(blocker().state, 'blocked');
  blocker().reset();
  assert.equal(browser.location.href, original);
  assert.equal(browser.history.length, 3);
});

test('positions written by the previous build still protect Back after a new entry and reload', async (t) => {
  const origin = 'http://localhost';
  const previous = browserHistory({
    index: 1,
    entries: ['/base/start', '/base/settings'].map((path, index) => ({
      url: new URL(path, origin),
      state: {
        key: 'owned-' + index,
        state: { index },
        bookshelfNavigationPosition: { stack: 'owned-legacy', index }
      }
    }))
  });
  const beforeReload = routerFixture(t, previous);
  assert.deepEqual(beforeReload.router.state.location.state, { index: 1 });
  await beforeReload.router.navigate('/settings?section=client#form');
  const { browser, router, changes, blocker } = routerFixture(t, browserHistory(previous.snapshot()));
  changes.register();
  await router.navigate(-2);
  assert.equal(blocker().state, 'blocked');
  assert.deepEqual(blocker().location.state, { index: 0 });
  blocker().reset();
  assert.equal(browser.location.href, 'http://localhost/base/settings?section=client#form');
});

test('collection pagination and scroll restore from router navigation state', () => {
  let action = 'POP';
  let state = null;
  const replacements = {
    'react-router-dom': {
      useLocation: () => ({ state }),
      useNavigationType: () => action
    },
    'Store/scrollPositions': { authors: 640 }
  };
  const [{ default: withCurrentPage }, { default: withScrollPosition }] = loadFrontendModule([
    'Components/withCurrentPage.js', 'Components/withScrollPosition.js'
  ], replacements);
  const Collection = () => null;
  const Paged = withCurrentPage(Collection);
  const Scrolled = withScrollPosition(Collection, 'authors');

  assert.equal(Paged({}).props.useCurrentPage, true);
  assert.equal(Scrolled({}).props.scrollTop, 640);
  action = 'PUSH';
  assert.equal(Paged({}).props.useCurrentPage, false);
  assert.equal(Scrolled({}).props.scrollTop, 0);
  action = 'REPLACE';
  state = { restoreScrollPosition: true };
  assert.equal(Scrolled({}).props.scrollTop, 640);
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
