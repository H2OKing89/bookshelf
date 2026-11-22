import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const filters = {
  pageSize: 20,
  sortKey: 'date',
  sortDirection: 'descending',
  eventTypes: []
};

function response(page, id, fields = {}) {
  return Response.json({
    page,
    pageSize: 20,
    totalRecords: 60,
    records: [{
      id,
      authorId: 5,
      bookId: 6,
      eventType: 'grabbed',
      sourceTitle: 'Synthetic Recording',
      date: '2026-11-21T00:00:00Z',
      customFormatScore: 0,
      qualityCutoffNotMet: false,
      ...fields
    }]
  });
}

function createFixture(t) {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: Infinity } } });
  const requests = [];
  const previousWindow = globalThis.window;
  globalThis.window = {
    location: { origin: 'https://bookshelf.invalid' },
    Readarr: { apiRoot: '/fixture/api/v1', apiKey: 'synthetic-key' }
  };
  t.mock.method(globalThis, 'fetch', (url, options) => new Promise((resolve) => {
    requests.push({ url, ...options, resolve });
  }));
  t.after(() => {
    client.clear();
    globalThis.window = previousWindow;
  });

  const { default: queryOptions, historyPageFamilyKey, invalidateHistoryPages } =
    loadFrontendModule('Activity/History/historyPageQuery', { 'App/queryClient': client });
  const options = (page, changes) => queryOptions({ ...filters, page, ...changes });

  return { client, requests, options, historyPageFamilyKey, invalidateHistoryPages };
}

test('history keeps the displayed records during a page change and discards a superseded response', async (t) => {
  const { client, requests, options } = createFixture(t);
  const observer = new QueryObserver(client, options(1));
  const stop = observer.subscribe(() => {});
  t.after(stop);

  const first = observer.refetch({ cancelRefetch: false });
  requests[0].resolve(response(1, 101));
  await first;

  observer.setOptions(options(2));
  assert.equal(observer.getCurrentResult().isPlaceholderData, true);
  assert.equal(observer.getCurrentResult().data.records[0].id, 101);
  assert.equal(requests[1].url.searchParams.get('page'), '2');

  observer.setOptions(options(3));
  assert.equal(requests[1].signal.aborted, true);
  const newest = observer.refetch({ cancelRefetch: false });
  requests[2].resolve(response(3, 301));
  const page = await newest;
  assert.equal(page.isPlaceholderData, false);
  assert.equal(page.data.records[0].id, 301);

  requests[1].resolve(response(2, 201));
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(observer.getCurrentResult().data.records[0].id, 301);
  assert.equal(client.getQueryData(options(2).queryKey), undefined);
});

test('history serializes all paging inputs and invalidates only the matching API root', async (t) => {
  const { client, requests, options, historyPageFamilyKey, invalidateHistoryPages } = createFixture(t);
  const selected = options(2, {
    pageSize: 10, sortKey: 'books.title', sortDirection: 'ascending', eventTypes: ['1', '4']
  });
  const requested = client.fetchQuery(selected);
  assert.equal(requests[0].url.pathname, '/fixture/api/v1/history');
  assert.deepEqual([...requests[0].url.searchParams], [
    ['page', '2'], ['pageSize', '10'], ['sortKey', 'books.title'], ['sortDirection', 'ascending'],
    ['eventType', '1'], ['eventType', '4']
  ]);
  assert.deepEqual(selected.queryKey.slice(0, 2), historyPageFamilyKey());
  requests[0].resolve(response(2, 201, { sourceTitle: null, customFormats: null, data: null, eventType: 2 }));
  const [record] = (await requested).records;
  assert.equal(record.sourceTitle, '');
  assert.deepEqual(record.customFormats, []);
  assert.deepEqual({ ...record.data }, {});
  assert.equal(record.eventType, 'unknown');

  window.Readarr.apiRoot = '/other/api/v1';
  const alternate = options(2);
  client.setQueryData(alternate.queryKey, { records: [] });
  window.Readarr.apiRoot = '/fixture/api/v1';
  await invalidateHistoryPages({ refetchType: 'none' });
  assert.equal(client.getQueryState(selected.queryKey).isInvalidated, true);
  assert.equal(client.getQueryState(alternate.queryKey).isInvalidated, false);
});

test('invalid history records fail the query before rendering a row', async (t) => {
  const { client, requests, options } = createFixture(t);
  const requested = client.fetchQuery(options(1));
  requests[0].resolve(response(1, 101, { date: 'invalid' }));
  await assert.rejects(requested, { kind: 'invalid-response' });
  assert.equal(client.getQueryData(options(1).queryKey), undefined);
});
