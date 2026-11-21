import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { fetchMetadataProviderStatus, parseMetadataProviderStatus } =
  loadFrontendModule('System/Status/MetadataProviders/metadataProviderStatus');

const observation = {
  provider: 'audnexus',
  operation: 'bookSearch',
  state: 'rateLimited',
  reason: 'tooManyRequests',
  observedAtUtc: '2026-11-21T08:19:27.1234567Z',
  lastCompleteAtUtc: null,
  retryAfterUtc: '2026-11-21T02:20:27-06:00'
};

test('validates the exact observation fields and nullable or omitted optional data', () => {
  assert.deepEqual(parseMetadataProviderStatus([]), []);
  assert.deepEqual(parseMetadataProviderStatus([{ ...observation, unexpected: 'discarded' }]), [observation]);

  const { reason, retryAfterUtc, ...withoutOptionalValues } = observation;
  const [legacy] = parseMetadataProviderStatus([withoutOptionalValues]);
  assert.equal(legacy.reason, undefined);
  assert.equal(legacy.retryAfterUtc, undefined);
  assert.equal(legacy.lastCompleteAtUtc, null);

  assert.throws(() => parseMetadataProviderStatus({ data: [] }), TypeError);
  assert.throws(() => parseMetadataProviderStatus([null]), TypeError);
  for (const malformed of [
    { provider: 'other' },
    { operation: 'constructor' },
    { state: false },
    { reason: ['tooManyRequests'] },
    { observedAtUtc: undefined },
    { observedAtUtc: '2026-11-21' },
    { observedAtUtc: '2026-02-30T08:19:27Z' },
    { observedAtUtc: '2026-11-21T24:00:00Z' },
    { lastCompleteAtUtc: 'invalid' },
    { retryAfterUtc: 0 }
  ]) {
    assert.throws(() => parseMetadataProviderStatus([observation, { ...observation, ...malformed }]), TypeError);
  }
});

test('only typed valid observations reach the metadata consumer', async (context) => {
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    Readarr: { apiRoot: '/books/api/v1', apiKey: 'synthetic-key' },
    location: { origin: 'https://bookshelf.invalid' }
  } });
  context.after(() => { delete globalThis.window; });
  const request = context.mock.method(globalThis, 'fetch', () => Response.json([observation]));
  assert.deepEqual(await fetchMetadataProviderStatus(), [observation]);
  assert.equal(request.mock.calls[0].arguments[0].pathname, '/books/api/v1/metadataproviderstatus');
  request.mock.mockImplementation(() => Response.json([{ ...observation, observedAtUtc: 'bad timestamp' }]));
  await assert.rejects(fetchMetadataProviderStatus(), { kind: 'invalid-response' });
});
