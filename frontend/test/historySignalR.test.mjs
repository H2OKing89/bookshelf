import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function historyPage(id) {
  return {
    page: 1,
    pageSize: 20,
    totalRecords: 1,
    records: [{
      id,
      bookId: 14,
      authorId: 23,
      date: '2026-11-21T12:00:00Z',
      eventType: 'grabbed',
      sourceTitle: 'Synthetic history',
      customFormatScore: 0,
      qualityCutoffNotMet: false
    }]
  };
}

function setup(t) {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: Infinity } } });
  const subscriptions = [];
  const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
  Object.defineProperty(globalThis, 'window', {
    configurable: true,
    value: { Readarr: { apiRoot: '/fixture/api/v1' } }
  });
  t.after(() => {
    subscriptions.forEach((unsubscribe) => unsubscribe());
    client.clear();
    if (previousWindow) {
      Object.defineProperty(globalThis, 'window', previousWindow);
    } else {
      delete globalThis.window;
    }
  });

  const requests = [];
  const history = loadFrontendModule('Activity/History/historyPageQuery', {
    'App/queryClient': client,
    'Utilities/Fetch/fetchJson': ({ signal, parse }) => new Promise((resolve) => {
      requests.push({ signal, resolve: (response) => resolve(parse(response)) });
    })
  });
  const legacy = [];
  const queue = [];
  const wanted = [];
  const statistics = [];
  const { default: Connector } = loadFrontendModule('Components/SignalRConnector', {
    '@microsoft/signalr/dist/browser/signalr.js': {},
    'react-redux': { connect: () => (component) => component },
    'Activity/History/historyPageQuery': history,
    'Activity/Queue/queuePageQuery': {
      clearQueueRefresh: () => queue.push('clear'),
      refreshQueuePages: () => queue.push('reconnect'),
      scheduleQueueRefresh: () => queue.push('queue')
    },
    'App/queryClient': client,
    'InteractiveSearch/releaseSearchQuery': { invalidateReleaseSearches: () => client.invalidateQueries({ queryKey: ['interactiveRelease', '/fixture/api/v1'], refetchType: 'none' }) },
    'LibraryStatistics/libraryStatisticsQuery': {
      clearLibraryStatisticsRefresh: () => statistics.push('clear'),
      refreshLibraryStatistics: () => statistics.push('reconnect'),
      scheduleLibraryStatisticsRefresh: () => statistics.push('refresh')
    },
    'Store/Actions/appActions': {},
    'Store/Actions/authorActions': {},
    'Store/Actions/baseActions': {},
    'Store/Actions/bookActions': {},
    'Store/Actions/commandActions': {},
    'Store/Actions/queueActions': {},
    'Store/Actions/settingsActions': {},
    'Store/Actions/systemActions': {},
    'Store/Actions/tagActions': {},
    'Utilities/pagePopulator': { repopulatePage: () => legacy.push('repopulate') },
    'Wanted/wantedPageQuery': {
      clearWantedRefresh: () => wanted.push('clear'),
      refreshWantedPages: () => wanted.push('reconnect'),
      scheduleWantedQueueDetailsRefresh: () => wanted.push('queue details'),
      scheduleWantedRefresh: (kind) => wanted.push(kind ?? 'both')
    }
  });
  const connector = new Connector({
    dispatchFetchAuthor: () => legacy.push('authors'),
    dispatchFetchBooks: () => legacy.push('books'),
    dispatchFetchCommands: () => legacy.push('commands'),
    dispatchFetchQueueDetails: () => legacy.push('queue details'),
    dispatchSetAppValue: () => legacy.push('connection'),
    dispatchUpdate: (item) => legacy.push(item),
    dispatchUpdateItem: (item) => legacy.push(item),
    dispatchRemoveItem: (item) => legacy.push(item),
    dispatchDeleteAuthorBooks: (item) => legacy.push(item)
  });

  const timers = new Map();
  t.mock.method(globalThis, 'setTimeout', (callback) => {
    const timer = Symbol();
    timers.set(timer, callback);
    return timer;
  });
  t.mock.method(globalThis, 'clearTimeout', (timer) => timers.delete(timer));

  const options = history.default({
    page: 1, pageSize: 20, eventTypes: [], sortKey: 'date', sortDirection: 'descending'
  });

  function watch() {
    const observer = new QueryObserver(client, options);
    subscriptions.push(observer.subscribe(() => {}));
    return observer;
  }

  return { client, connector, legacy, options, queue, requests, timers, wanted, watch, statistics, scheduleLocalRefresh: history.scheduleHistoryRefresh };
}

function nextTurn() {
  return new Promise((resolve) => setImmediate(resolve));
}

test('a history event burst cancels the pending first read and only refreshes the active API root', async (t) => {
  const { client, connector, legacy, options, requests, timers, watch, scheduleLocalRefresh } = setup(t);
  const inactive = [...options.queryKey.slice(0, 2), 'page=2'];
  const otherRoot = ['history', '/other/api/v1', 'page=1'];
  client.setQueryData(inactive, historyPage(30));
  client.setQueryData(otherRoot, historyPage(31));

  const observer = watch();
  assert.equal(requests.length, 1);
  assert.equal(observer.getCurrentResult().data, undefined);

  connector.handleMessage({ name: 'history', body: { action: 'sync' } });
  scheduleLocalRefresh();
  connector.handleMessage({ name: 'history', body: { action: 'sync' } });
  connector.handleMessage({ name: 'book', body: { action: 'sync' } });
  connector.handleMessage({ name: 'book', body: { action: 'deleted' } });
  connector.handleMessage({ name: 'author', body: { action: 'deleted', resource: { id: 23 } } });
  assert.deepEqual(legacy, [{ section: 'authors', id: 23 }, { authorId: 23 }]);
  assert.equal(timers.size, 1);

  const [[timer, refresh]] = timers;
  timers.delete(timer);
  refresh();
  await nextTurn();
  assert.equal(requests.length, 2);
  assert.equal(requests[0].signal.aborted, true);
  assert.equal(client.getQueryState(inactive).isInvalidated, true);
  assert.equal(client.getQueryState(otherRoot).isInvalidated, false);

  const current = observer.refetch({ cancelRefetch: false });
  requests[1].resolve(historyPage(2));
  assert.equal((await current).data.records[0].id, 2);
  requests[0].resolve(historyPage(1));
  await nextTurn();
  assert.equal(observer.getCurrentResult().data.records[0].id, 2);
});

test('reconnect invalidates inactive history, absorbs scheduled work, and preserves legacy refreshes', async (t) => {
  const { client, connector, legacy, options, requests, timers } = setup(t);
  client.setQueryData(options.queryKey, historyPage(1));
  connector.handleHistory({ action: 'history' });
  assert.equal(timers.size, 0);
  connector.handleHistory({ action: 'sync' });
  assert.equal(timers.size, 1);

  connector.onReconnected();
  await nextTurn();
  assert.equal(timers.size, 0);
  assert.equal(requests.length, 0);
  assert.equal(client.getQueryState(options.queryKey).isInvalidated, true);
  assert.deepEqual(legacy, ['connection', 'authors', 'books', 'commands', 'repopulate']);

  connector.connection = { stop: () => legacy.push('stop') };
  connector.handleHistory({ action: 'sync' });
  connector.componentWillUnmount();
  assert.equal(timers.size, 0);
  assert.equal(legacy.at(-1), 'stop');
});

test('Wanted routes membership events and queue details to the matching query scopes', (t) => {
  const { connector, legacy, wanted } = setup(t);
  connector.handleMessage({ name: 'wanted/missing', body: { action: 'updated' } });
  connector.handleMessage({ name: 'wanted/cutoff', body: { action: 'sync' } });
  connector.handleMessage({ name: 'wanted/missing', body: { action: 'ignored' } });
  connector.handleMessage({ name: 'bookfile', body: { action: 'updated', resource: { id: 11 } } });
  connector.handleMessage({ name: 'book', body: { action: 'updated', resource: { id: 12 } } });
  connector.handleMessage({ name: 'author', body: { action: 'sync' } });
  connector.handleMessage({ name: 'queue/details', body: { action: 'sync' } });
  connector.onReconnected();
  connector.connection = { stop: () => {} };
  connector.componentWillUnmount();

  assert.deepEqual(wanted, ['missing', 'cutoffUnmet', 'both', 'both', 'both', 'queue details', 'reconnect', 'clear']);
  assert.equal(legacy.filter((call) => call === 'queue details').length, 1);
});

test('Queue routes list events through its refresh coordinator and preserves status and details', (t) => {
  const { connector, legacy, queue } = setup(t);
  connector.handleMessage({ name: 'queue', body: { action: 'sync' } });
  connector.handleMessage({ name: 'queue/details', body: { action: 'sync' } });
  connector.handleMessage({ name: 'queue/status', body: { action: 'updated', resource: { totalCount: 2 } } });
  connector.onReconnected();
  connector.connection = { stop: () => {} };
  connector.componentWillUnmount();

  assert.deepEqual(queue, ['queue', 'reconnect', 'clear']);
  assert.deepEqual(legacy.slice(0, 2), ['queue details', { section: 'queue.status', data: { totalCount: 2 } }]);
});

test('Library statistics refresh follows committed book, author, file and membership changes', (t) => {
  const { connector, statistics } = setup(t);
  connector.handleMessage({ name: 'bookfile', body: { action: 'updated', resource: { id: 11 } } });
  connector.handleMessage({ name: 'author', body: { action: 'sync' } });
  connector.handleMessage({ name: 'book', body: { action: 'updated', resource: { id: 12 } } });
  connector.handleMessage({ name: 'librarystatistics', body: { action: 'sync' } });
  connector.handleMessage({ name: 'librarystatistics', body: { action: 'ignored' } });
  connector.onReconnected();
  connector.connection = { stop: () => {} };
  connector.componentWillUnmount();
  assert.deepEqual(statistics, ['refresh', 'refresh', 'refresh', 'refresh', 'reconnect', 'clear']);
});


test('quality rank events refresh library and the active page plus history without changing other queries', (t) => {
  const { connector, legacy, timers } = setup(t);
  connector.handleMessage({ name: 'qualityrank', body: { action: 'ignored' } });
  assert.equal(legacy.length, 0);
  connector.handleMessage({ name: 'qualityrank', body: { action: 'sync' } });
  assert.deepEqual(legacy, ['books', 'repopulate']);
  assert.equal(timers.size, 1);

  connector.handleMessage({ name: 'qualityrank', body: { action: 'sync', authorAssignmentsChanged: true } });
  assert.deepEqual(legacy, ['books', 'repopulate', 'authors', 'books', 'repopulate']);
  assert.equal(timers.size, 1);
});
