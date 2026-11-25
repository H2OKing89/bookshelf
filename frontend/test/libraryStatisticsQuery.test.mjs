import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function statistics() {
  return {
    authors: [{ id: 11, name: 'Owned author' }],
    authorCount: 1, bookCount: 2, audioRecordingCount: 2, ownedAudioRecordingCount: 1,
    ebookEditionCount: 0, ownedEbookEditionCount: 0, confirmedWorkCount: 0, ownedConfirmedWorkCount: 0,
    fileCount: 1, mappedFileCount: 1, unresolvedFileCount: 0, audioFileCount: 1,
    ebookFileCount: 0, otherFileCount: 0, sizeOnDisk: 1024, mappedSizeOnDisk: 1024,
    unresolvedSizeOnDisk: 0, audioFilesWithKnownDuration: 0, knownAudioDurationSeconds: null,
    codecs: [{ label: 'Unknown', count: 1 }], bitrates: [], mediaInfoStatus: [],
    languages: [], narrators: [], sources: []
  };
}

function setup(t) {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: Infinity } } });
  const previous = Object.getOwnPropertyDescriptor(globalThis, 'window');
  Object.defineProperty(globalThis, 'window', { configurable: true, value: { Readarr: { apiRoot: '/owned/api/v1' } } });
  const pending = [];
  const subscriptions = [];
  const query = loadFrontendModule('LibraryStatistics/libraryStatisticsQuery', {
    'App/queryClient': client,
    'Utilities/Fetch/fetchJson': ({ path, signal, parse }) => new Promise(resolve => pending.push({ path, signal, resolve: value => resolve(parse(value)) }))
  });
  t.after(() => {
    query.clearLibraryStatisticsRefresh();
    subscriptions.forEach(unsubscribe => unsubscribe());
    client.clear();
    if (previous) Object.defineProperty(globalThis, 'window', previous);
    else delete globalThis.window;
  });
  function watch(options) {
    const observer = new QueryObserver(client, options);
    subscriptions.push(observer.subscribe(() => {}));
    return observer;
  }
  return { client, query, pending, watch };
}

const all = { media: 'all', source: 'all' };
const local = { media: 'audiobook', source: 'local', authorId: 11 };
const nextTurn = () => new Promise(resolve => setImmediate(resolve));

test('statistics accepts unknown duration but rejects nonfinite duration and invalid counts', t => {
  const { query } = setup(t);
  const payload = statistics();
  assert.equal(query.parseLibraryStatistics(payload).knownAudioDurationSeconds, null);
  assert.deepEqual(query.parseLibraryStatistics(payload).codecs, [{ label: 'Unknown', count: 1 }]);
  assert.equal(query.parseLibraryStatistics({ ...payload, knownAudioDurationSeconds: 1.5 }).knownAudioDurationSeconds, 1.5);
  assert.throws(() => query.parseLibraryStatistics({ ...payload, sizeOnDisk: -1 }));
  assert.throws(() => query.parseLibraryStatistics({ ...payload, knownAudioDurationSeconds: Infinity }));
  assert.throws(() => query.parseLibraryStatistics({ ...payload, codecs: [{ label: 'invalid', count: 0 }] }));
});

test('query scopes preserve root, author, media and source without sending invented association filters', async t => {
  const { query, pending, watch } = setup(t);
  const unfiltered = query.libraryStatisticsQuery(all);
  const filtered = query.libraryStatisticsQuery(local);
  assert.notDeepEqual(unfiltered.queryKey, filtered.queryKey);
  assert.deepEqual(unfiltered.queryKey.slice(0, 2), ['libraryStatistics', '/owned/api/v1']);
  watch(filtered);
  assert.equal(pending[0].path, '/librarystatistics?media=audiobook&source=local&authorId=11');
  pending[0].resolve(statistics());
  await nextTurn();
});

test('committed changes cancel an active outdated snapshot and invalidate inactive same-root scopes only', async t => {
  const { client, query, pending, watch } = setup(t);
  const unfiltered = query.libraryStatisticsQuery(all);
  const inactive = query.libraryStatisticsQuery(local);
  client.setQueryData(inactive.queryKey, statistics());
  const otherRoot = ['libraryStatistics', '/other/api/v1', all];
  client.setQueryData(otherRoot, statistics());
  const observer = watch(unfiltered);
  assert.equal(pending.length, 1);
  const refreshing = query.refreshLibraryStatistics();
  await nextTurn();
  assert.equal(pending[0].signal.aborted, true);
  assert.equal(pending.length, 2);
  assert.equal(client.getQueryState(inactive.queryKey).isInvalidated, true);
  assert.equal(client.getQueryState(otherRoot).isInvalidated, false);
  pending[1].resolve({ ...statistics(), bookCount: 3 });
  await refreshing;
  pending[0].resolve(statistics());
  await nextTurn();
  assert.equal(observer.getCurrentResult().data.bookCount, 3);
});
