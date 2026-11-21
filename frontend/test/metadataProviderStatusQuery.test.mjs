import assert from 'node:assert/strict';
import test from 'node:test';
import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function createFixture(t) {
  const client = new QueryClient({ defaultOptions: { queries: { gcTime: Infinity } } });
  const requests = [];
  const subscriptions = [];
  const previousWindow = globalThis.window;
  globalThis.window = { Readarr: { apiRoot: '/fixture/api/v1' } };

  const { default: options } = loadFrontendModule('System/Status/MetadataProviders/metadataProviderStatusQueryOptions', {
    './metadataProviderStatus': {
      fetchMetadataProviderStatus: (signal) => new Promise((resolve, reject) => {
        requests.push({ root: window.Readarr.apiRoot, signal, resolve, reject });
      })
    }
  });

  t.after(() => {
    subscriptions.forEach((stop) => stop());
    client.clear();
    globalThis.window = previousWindow;
  });

  function watch() {
    const observer = new QueryObserver(client, options());
    const stop = observer.subscribe(() => {});
    subscriptions.push(stop);
    return { observer, stop };
  }

  return { client, options, requests, watch };
}

function observation(state) {
  return [{
    provider: 'audnexus',
    operation: 'bookSearch',
    state,
    observedAtUtc: '2026-11-21T12:00:00Z'
  }];
}

test('metadata status observers share a request and abort only after the last consumer leaves', async (t) => {
  const { requests, watch } = createFixture(t);
  const first = watch();
  const second = watch();
  assert.equal(requests.length, 1);
  assert.equal(requests[0].root, '/fixture/api/v1');

  const completed = second.observer.refetch({ cancelRefetch: false });
  assert.equal(requests.length, 1);
  const current = observation('complete');
  requests[0].resolve(current);
  assert.deepEqual((await completed).data, current);
  assert.deepEqual(first.observer.getCurrentResult().data, current);

  const refresh = first.observer.refetch();
  assert.equal(requests.length, 2);
  first.stop();
  assert.equal(requests[1].signal.aborted, false);
  second.stop();
  assert.equal(requests[1].signal.aborted, true);
  requests[1].resolve(observation('missing'));
  await refresh;
});

test('a late result for a prior API root cannot update the active status', async (t) => {
  const { client, options, requests, watch } = createFixture(t);
  const { observer } = watch();
  const originalKey = options().queryKey;

  window.Readarr.apiRoot = '/other-fixture/api/v1';
  observer.setOptions(options());
  assert.equal(requests.length, 2);
  assert.equal(requests[0].signal.aborted, true);
  assert.equal(requests[1].root, '/other-fixture/api/v1');

  const current = observation('cached');
  const completed = observer.refetch({ cancelRefetch: false });
  requests[1].resolve(current);
  await completed;
  requests[0].resolve(observation('missing'));
  await new Promise((resolve) => setImmediate(resolve));

  assert.deepEqual(observer.getCurrentResult().data, current);
  assert.equal(client.getQueryData(originalKey), undefined);
});

test('a failed background refresh retains observations and a later refresh clears the error', async (t) => {
  const { requests, watch } = createFixture(t);
  const { observer } = watch();
  const first = observer.refetch({ cancelRefetch: false });
  const current = observation('complete');
  requests[0].resolve(current);
  await first;

  const failed = observer.refetch();
  assert.equal(observer.getCurrentResult().isFetching, true);
  assert.deepEqual(observer.getCurrentResult().data, current);
  requests[1].reject(new Error('Synthetic unavailable response'));
  const error = await failed;
  assert.equal(error.isError, true);
  assert.deepEqual(error.data, current);
  assert.equal(requests.length, 2);

  const refresh = observer.refetch();
  const updated = observation('rateLimited');
  requests[2].resolve(updated);
  const recovered = await refresh;
  assert.equal(recovered.isError, false);
  assert.deepEqual(recovered.data, updated);
});
