import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function release(token, changes = {}) {
  return {
    searchResultId: String(token).padStart(32, '0'), guid: 'synthetic-shared-release', indexerId: 3,
    title: 'Synthetic Audiobook', quality: { quality: { id: 12, name: 'M4B' }, revision: {} },
    customFormats: [], rejections: [], downloadAllowed: true, ...changes
  };
}

function setup(t, fetch) {
  const previousWindow = Object.getOwnPropertyDescriptor(globalThis, 'window');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    location: { origin: 'https://fixture.test' }, Readarr: { apiRoot: '/books/api/v1', apiKey: 'synthetic' }
  } });
  const client = new QueryClient();
  t.mock.method(globalThis, 'fetch', fetch);
  t.after(() => {
    client.clear();
    if (previousWindow) Object.defineProperty(globalThis, 'window', previousWindow);
    else delete globalThis.window;
  });
  const search = loadFrontendModule('InteractiveSearch/releaseSearchQuery', { 'App/queryClient': client });
  return { client, search };
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
}

test('fresh reopen reuses only the exact search scope, and title filtering is local', async (t) => {
  const requests = [];
  const { client, search } = setup(t, async url => {
    requests.push(new URL(url));
    const id = requests.at(-1).searchParams.get('bookId') || requests.at(-1).searchParams.get('authorId');
    return json([release(id, { title: `Synthetic Title ${id}` })]);
  });
  const first = search.default({ bookId: 11 });
  const second = search.default({ bookId: 12 });
  const author = search.default({ authorId: 11 });
  const before = await client.fetchQuery(first);
  assert.equal((await client.fetchQuery(first)).items[0].searchResultId, before.items[0].searchResultId);
  assert.equal(requests.length, 1);
  assert.equal(search.filterReleaseTitles(before.items, ' title 11 ').length, 1);
  assert.equal(search.filterReleaseTitles(before.items, 'TITLE 12').length, 0);
  await client.fetchQuery(second);
  await client.fetchQuery(author);
  assert.equal(requests.length, 3);
  assert.deepEqual(requests.map(url => url.search), ['?bookId=11', '?bookId=12', '?authorId=11']);
  assert.equal(search.currentRelease(client, second.queryKey, before.items[0]), undefined);
  assert.throws(() => search.parseReleaseSearchScope({ bookId: 11, authorId: 12 }));
});

test('switching search scopes cancels a pending read and never presents previous results', async (t) => {
  let finishFirst;
  let firstSignal;
  const { client, search } = setup(t, async (url, options) => {
    if (new URL(url).searchParams.get('bookId') === '11') {
      firstSignal = options.signal;
      return new Promise(resolve => { finishFirst = resolve; });
    }
    return json([release(12)]);
  });
  const first = search.default({ bookId: 11 });
  const second = search.default({ bookId: 12 });
  const observer = new QueryObserver(client, first);
  const unsubscribe = observer.subscribe(() => {});
  observer.setOptions(second);
  assert.equal(firstSignal.aborted, true);
  assert.equal(observer.getCurrentResult().data, undefined);
  await observer.refetch({ cancelRefetch: false });
  finishFirst(json([release(11)]));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(observer.getCurrentResult().data.items[0].searchResultId, release(12).searchResultId);
  assert.equal(client.getQueryData(first.queryKey), undefined);
  unsubscribe();
});

test('grab outcomes patch only the exact token without extending its search lifetime', async (t) => {
  const requests = [];
  const { client, search } = setup(t, async (url, options) => {
    const body = JSON.parse(options.body);
    requests.push(body);
    return json(body);
  });
  const key = search.releaseSearchKey({ bookId: 11 });
  const otherKey = search.releaseSearchKey({ bookId: 12 });
  const first = release(11);
  const second = release(12, { indexerId: 4 });
  const fetchedAt = Date.now() - 1000;
  client.setQueryData(key, { items: [first, second], fetchedAt }, { updatedAt: fetchedAt });
  client.setQueryData(otherKey, { items: [release(13)], fetchedAt });
  const action = { key, identity: first };
  await search.grabSearchRelease(action);
  assert.deepEqual(requests[0], { bookId: 11, guid: first.guid, indexerId: first.indexerId, searchResultId: first.searchResultId });
  search.setReleaseStatus(client, action, { isGrabbed: true });
  assert.equal(client.getQueryData(key).items[0].isGrabbed, true);
  assert.equal(client.getQueryData(key).items[1].isGrabbed, undefined);
  assert.equal(client.getQueryData(otherKey).items[0].isGrabbed, undefined);
  assert.equal(client.getQueryState(key).dataUpdatedAt, fetchedAt);
  client.setQueryData(key, { items: [release(14)], fetchedAt });
  search.setReleaseStatus(client, action, { grabError: 'late result' }, true);
  assert.equal(client.getQueryData(key).items[0].grabError, undefined);
  assert.equal(client.getQueryData(key).refreshRequired, undefined);
});

test('expired, invalidated and malformed results cannot authorize grabs', async (t) => {
  const item = release(11);
  const { client, search } = setup(t, async () => json([item, item]));
  const key = search.releaseSearchKey({ bookId: 11 });
  await assert.rejects(client.fetchQuery(search.default({ bookId: 11 })));
  client.setQueryData(key, { items: [item], fetchedAt: Date.now() - search.releaseFreshnessMs });
  assert.equal(search.currentRelease(client, key, item), undefined);
  client.setQueryData(key, { items: [item], fetchedAt: Date.now() });
  assert.equal(search.currentRelease(client, key, { ...item, indexerId: 999 }), undefined);
  assert.equal(search.currentRelease(client, key, item).searchResultId, item.searchResultId);
  search.setReleaseStatus(client, { key, identity: item }, { grabError: 'Expired' }, true);
  assert.equal(search.currentRelease(client, key, item), undefined);
  client.setQueryData(key, { items: [item], fetchedAt: Date.now() });
  await search.invalidateReleaseSearches({ bookId: 11 });
  assert.equal(search.currentRelease(client, key, item), undefined);
});

test('invalidation during a search requires refresh even if that search subsequently succeeds', async (t) => {
  let finishFirst;
  let requests = 0;
  const { client, search } = setup(t, async () => {
    requests++;
    return requests === 1 ? new Promise(resolve => { finishFirst = resolve; }) : json([release(12)]);
  });
  const scope = { bookId: 11 };
  const options = search.default(scope);
  const pending = client.fetchQuery(options);
  await search.invalidateReleaseSearches(scope);
  finishFirst(json([release(11)]));
  const outdated = await pending;
  assert.equal(outdated.refreshRequired, true);
  assert.equal(search.currentRelease(client, options.queryKey, outdated.items[0]), undefined);
  assert.equal(requests, 1);

  const refreshed = await client.fetchQuery({ ...options, staleTime: 0 });
  assert.equal(refreshed.refreshRequired, false);
  assert.equal(search.currentRelease(client, options.queryKey, refreshed.items[0]).searchResultId, release(12).searchResultId);
});
