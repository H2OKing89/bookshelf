import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const defaults = {
  page: 1,
  pageSize: 5,
  sortKey: 'releaseDate',
  sortDirection: 'descending',
  monitored: true
};

function response(page, id) {
  return Response.json({
    page,
    pageSize: 5,
    totalRecords: 15,
    records: [{
      id,
      authorId: 1,
      title: 'Owned book',
      titleSlug: 'owned-book',
      monitored: true
    }]
  });
}

function setup(t) {
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

  const wanted = loadFrontendModule('Wanted/wantedPageQuery', { 'App/queryClient': client });
  t.after(() => {
    wanted.clearWantedRefresh();
    client.clear();
    globalThis.window = previousWindow;
  });

  return { client, requests, ...wanted };
}

test('Wanted keeps previous results for a pending view while cancelling a superseded page', async (t) => {
  const { client, requests, wantedPageQuery } = setup(t);
  const options = (kind, changes) => wantedPageQuery(kind, { ...defaults, ...changes });
  const cutoffOption = options('cutoffUnmet');
  const observer = new QueryObserver(client, options('missing'));
  t.after(observer.subscribe(() => {}));

  const first = observer.refetch({ cancelRefetch: false });
  requests[0].resolve(response(1, 10));
  await first;

  const obsolete = options('missing', { page: 2, monitored: false });
  observer.setOptions(obsolete);
  assert.equal(observer.getCurrentResult().isPlaceholderData, true);
  assert.equal(observer.getCurrentResult().data.records[0].id, 10);
  assert.deepEqual([...requests[1].url.searchParams], [
    ['page', '2'], ['pageSize', '5'], ['sortKey', 'releaseDate'],
    ['sortDirection', 'descending'], ['monitored', 'false']
  ]);

  observer.setOptions(cutoffOption);
  assert.equal(requests[1].signal.aborted, true);
  assert.equal(requests[2].url.pathname, '/fixture/api/v1/wanted/cutoff');
  const newest = observer.refetch({ cancelRefetch: false });
  requests[2].resolve(response(1, 30));
  assert.equal((await newest).data.records[0].id, 30);

  requests[1].resolve(response(2, 20));
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(observer.getCurrentResult().data.records[0].id, 30);
  assert.equal(client.getQueryData(obsolete.queryKey), undefined);
});

test('Wanted detail queries use only active-page book IDs and never fetch an empty scope', async (t) => {
  const { client, requests, wantedQueueDetailsQuery, wantedFileDetailsQuery } = setup(t);
  assert.deepEqual(await client.fetchQuery(wantedQueueDetailsQuery('missing', [])), []);
  assert.deepEqual(await client.fetchQuery(wantedFileDetailsQuery('cutoffUnmet', [])), {});
  assert.equal(requests.length, 0);

  const queue = client.fetchQuery(wantedQueueDetailsQuery('missing', [10, 8, 10]));
  const files = client.fetchQuery(wantedFileDetailsQuery('cutoffUnmet', [8, 10]));
  assert.equal(requests[0].url.pathname, '/fixture/api/v1/queue/details');
  assert.equal(requests[1].url.pathname, '/fixture/api/v1/bookfile');
  assert.deepEqual(requests[0].url.searchParams.getAll('bookIds'), ['8', '10']);
  assert.deepEqual(requests[1].url.searchParams.getAll('bookId'), ['8', '10']);

  requests[0].resolve(Response.json([{ id: -2, book: { id: 8 } }, { id: 5, bookId: 22 }]));
  requests[1].resolve(Response.json([
    { id: 12, bookId: 10, quality: { quality: { name: 'M4B' }, revision: {} } },
    { id: 22, bookId: 10, quality: { quality: { name: 'M4B' }, revision: {} } },
    { id: 18, bookId: 33, quality: { quality: { name: 'MP3' }, revision: {} } }
  ]));
  assert.deepEqual(await queue, [8]);
  assert.deepEqual(await files, { 10: ['M4B'] });
});

test('refreshing Wanted drops obsolete initial reads and scopes cached pages by view and API root', async (t) => {
  const { client, requests, wantedPageQuery, refreshWantedPages } = setup(t);
  const active = wantedPageQuery('missing', defaults);
  const inactive = wantedPageQuery('missing', { ...defaults, page: 2 });
  const cutoff = wantedPageQuery('cutoffUnmet', defaults);
  client.setQueryData(inactive.queryKey, { records: [] });
  client.setQueryData(cutoff.queryKey, { records: [] });

  window.Readarr.apiRoot = '/separate/api/v1';
  const separate = wantedPageQuery('missing', defaults);
  client.setQueryData(separate.queryKey, { records: [] });
  window.Readarr.apiRoot = '/fixture/api/v1';

  const observer = new QueryObserver(client, active);
  t.after(observer.subscribe(() => {}));
  const refreshed = refreshWantedPages('missing');
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(requests[0].signal.aborted, true);
  assert.equal(requests.length, 2);
  requests[1].resolve(response(1, 90));
  requests[0].resolve(response(1, 10));

  await refreshed;
  assert.equal(observer.getCurrentResult().data.records[0].id, 90);
  assert.equal(client.getQueryState(inactive.queryKey).isInvalidated, true);
  assert.equal(client.getQueryState(cutoff.queryKey).isInvalidated, false);
  assert.equal(client.getQueryState(separate.queryKey).isInvalidated, false);
});

test('Wanted monitor action preserves false and rejects malformed page resources', async (t) => {
  const { client, requests, setWantedMonitoring, wantedPageQuery } = setup(t);
  const monitoring = setWantedMonitoring([8, 10], false);
  assert.equal(requests[0].url.pathname, '/fixture/api/v1/book/monitor');
  assert.equal(requests[0].method, 'PUT');
  assert.deepEqual(JSON.parse(requests[0].body), { bookIds: [8, 10], monitored: false });
  requests[0].resolve(Response.json([], { status: 202 }));
  await monitoring;

  const invalid = client.fetchQuery(wantedPageQuery('missing', defaults));
  requests[1].resolve(Response.json({ page: 1, pageSize: 5, totalRecords: 1, records: [{ id: 0 }] }));
  await assert.rejects(invalid, { kind: 'invalid-response' });
});
