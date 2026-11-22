import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const nextTurn = () => new Promise((resolve) => setImmediate(resolve));

function record(id, extras) {
  return { id, title: 'Owned queue fixture', status: 'queued', bookId: id, authorId: 33, downloadClientId: 7, downloadId: 'owned-task', ...extras };
}

function page(pageNumber, ...records) {
  return { page: pageNumber, pageSize: 20, totalRecords: 60, records };
}

function setup(t) {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: Infinity } } });
  const active = [];
  const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: { Readarr: { apiRoot: '/fixture/api/v1' } } });
  const requests = [];
  const timers = new Map();
  const queue = loadFrontendModule('Activity/Queue/queuePageQuery', {
    'App/queryClient': client,
    'Utilities/Fetch/fetchJson': (options) => new Promise((resolve, reject) => {
      requests.push({
        ...options,
        resolve: (response) => {
          try {
            resolve(options.parse(response));
          } catch (error) {
            reject(error);
          }
        },
        reject
      });
    })
  });

  t.mock.method(globalThis, 'setTimeout', (callback) => {
    const id = Symbol();
    timers.set(id, callback);
    return id;
  });
  t.mock.method(globalThis, 'clearTimeout', (id) => timers.delete(id));
  t.after(() => {
    queue.clearQueueRefresh();
    active.forEach((unsubscribe) => unsubscribe());
    client.clear();
    if (previousWindow) {
      Object.defineProperty(globalThis, 'window', previousWindow);
    } else {
      delete globalThis.window;
    }
  });

  const options = (pageNumber, changes) => queue.default({
    page: pageNumber,
    pageSize: 20,
    sortKey: 'books.title',
    sortDirection: 'ascending',
    includeUnknownAuthorItems: false,
    ...changes
  });

  function watch(queryOptions = options(1)) {
    const observer = new QueryObserver(client, queryOptions);
    active.push(observer.subscribe(() => {}));
    return observer;
  }

  async function runTimer() {
    assert.equal(timers.size, 1);
    const [[id, handle]] = timers;
    timers.delete(id);
    handle();
    await nextTurn();
  }

  return { client, options, queue, requests, runTimer, timers, watch };
}

test('queue keys retain explicit false and previous rows and discard a superseded page', async (t) => {
  const { client, options, requests, watch } = setup(t);
  const initial = options(1);
  const observer = watch(initial);
  assert.deepEqual(initial.queryKey.slice(0, 2), ['activityQueue', '/fixture/api/v1']);
  assert.match(requests[0].path, /^\/queue\?page=1&pageSize=20&sortKey=book.title&sortDirection=ascending&includeUnknownAuthorItems=false$/);
  assert.notDeepEqual(initial.queryKey, options(1, { includeUnknownAuthorItems: true }).queryKey);

  const first = observer.refetch({ cancelRefetch: false });
  requests[0].resolve(page(1, record(12)));
  await first;
  observer.setOptions(options(2));
  assert.equal(observer.getCurrentResult().isPlaceholderData, true);
  assert.equal(observer.getCurrentResult().data.records[0].id, 12);

  observer.setOptions(options(3));
  assert.equal(requests[1].signal.aborted, true);
  const current = observer.refetch({ cancelRefetch: false });
  requests[2].resolve(page(3, record(14)));
  assert.equal((await current).data.records[0].id, 14);
  requests[1].resolve(page(2, record(13)));
  await nextTurn();
  assert.equal(observer.getCurrentResult().data.records[0].id, 14);
  assert.equal(client.getQueryData(options(2).queryKey), undefined);
});

test('queue refresh waits for all interaction holds and coalesces while a read is running', async (t) => {
  const { client, options, queue, requests, runTimer, timers, watch } = setup(t);
  const inactive = options(2);
  const otherRoot = ['activityQueue', '/other/api/v1', 'page=1'];
  client.setQueryData(inactive.queryKey, page(2, record(20)));
  client.setQueryData(otherRoot, page(1, record(31)));
  const observer = watch();
  const first = observer.refetch({ cancelRefetch: false });
  requests[0].resolve(page(1, record(11)));
  await first;

  const releaseDialog = queue.holdQueueRefresh();
  const releaseAction = queue.holdQueueRefresh();
  queue.scheduleQueueRefresh();
  queue.scheduleQueueRefresh();
  await queue.refreshQueuePages();
  releaseDialog();
  assert.equal(timers.size, 0);
  assert.equal(requests.length, 1);

  releaseAction();
  await runTimer();
  assert.equal(requests.length, 2);
  assert.equal(client.getQueryState(inactive.queryKey).isInvalidated, true);
  assert.equal(client.getQueryState(otherRoot).isInvalidated, false);
  queue.scheduleQueueRefresh();
  queue.scheduleQueueRefresh();
  assert.equal(timers.size, 0);
  assert.equal(requests[1].signal.aborted, false);
  requests[1].resolve(page(1, record(12)));
  await nextTurn();

  await runTimer();
  assert.equal(requests.length, 3);
  requests[2].resolve(page(1, record(13)));
  await nextTurn();
  assert.equal(observer.getCurrentResult().data.records[0].id, 13);
  assert.equal(timers.size, 0);
});

test('queue action identity guards client changes and submits only the captured IDs and options', async (t) => {
  const { queue, requests, options } = setup(t);
  const first = record(11);
  const second = record(16, { downloadClientId: 8 });
  const targets = [first, second].map(queue.queueTarget);
  assert.equal(queue.queueTargetsAreCurrent([second, first], targets), true);
  assert.equal(queue.queueTargetsAreCurrent([record(11, { downloadClientId: 8 }), second], targets), false);
  assert.equal(queue.queueTargetsAreCurrent([first], targets), false);

  const removed = queue.submitQueueAction({
    kind: 'remove',
    bulk: true,
    pageKey: JSON.stringify(options(1).queryKey),
    targets,
    options: { remove: false, blocklist: true, skipRedownload: true, changeCategory: false }
  });
  assert.equal(requests[0].method, 'DELETE');
  assert.equal(requests[0].path, '/queue/bulk?removeFromClient=false&blocklist=true&skipRedownload=true&changeCategory=false');
  assert.deepEqual(requests[0].body, { ids: [11, 16] });
  assert.equal(requests[0].timeout, 120_000);
  requests[0].reject(new Error('owned queue target changed'));
  await assert.rejects(removed, /owned queue target changed/);
  assert.equal(requests.length, 1);
});
