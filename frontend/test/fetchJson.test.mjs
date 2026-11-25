import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { ApiError, default: fetchJson } = loadFrontendModule('Utilities/Fetch/fetchJson');
const origin = 'https://bookshelf.invalid';
const key = 'synthetic-api-key';
const parse = (value) => value;
const path = '/metadataproviderstatus';

function mockBrowser(context, response, apiRoot = '/api/v1') {
  const runtime = { Readarr: { apiRoot, apiKey: key }, location: { origin } };
  Object.defineProperty(globalThis, 'window', { value: runtime, configurable: true });
  context.after(() => { delete globalThis.window; });
  return context.mock.method(globalThis, 'fetch', response);
}

test('uses the initialized API root, same-origin credentials and AJAX headers', async (context) => {
  const request = mockBrowser(context, () => Response.json({ status: 'ok' }));

  for (const apiRoot of ['/api/v1', '/shelf/api/v1', '/shelf/api/v1/']) {
    window.Readarr.apiRoot = apiRoot;
    assert.equal(await fetchJson({
      path: path + '?limit=1',
      parse(value) {
        assert.deepEqual(value, { status: 'ok' });
        return value.status;
      }
    }), 'ok');

    const [target, options] = request.mock.calls.at(-1).arguments;
    assert.equal(target.href, origin + apiRoot.replace(/\/$/, '') + path + '?limit=1');
    assert.equal(options.credentials, 'same-origin');
    assert.equal(options.redirect, 'error');
    assert.equal(options.headers.get('X-Api-Key'), key);
    assert.equal(options.headers.get('X-Requested-With'), 'XMLHttpRequest');
    assert.equal(options.headers.get('Accept'), 'application/json');
    assert.equal(options.headers.has('Content-Type'), false);
    assert.equal(options.body, undefined);
  }
});

test('explicit external URLs omit credentials and cannot carry the API key', async (context) => {
  const request = mockBrowser(context, () => Response.json([]));
  for (const external of ['https://external.invalid/data', '//external.invalid/data']) {
    await fetchJson({ path: external, parse, headers: { 'x-API-key': key, 'X-Requested-With': 'XMLHttpRequest' } });
    const [target, options] = request.mock.calls.at(-1).arguments;
    assert.equal(target.href, 'https://external.invalid/data');
    assert.equal(options.credentials, 'omit');
    assert.equal(options.headers.has('X-Api-Key'), false);
    assert.equal(options.headers.has('X-Requested-With'), false);
  }

  await assert.rejects(fetchJson({ path: '/%2e%2e/%2e%2e/outside', parse }), { kind: 'invalid-request' });
  await assert.rejects(fetchJson({ path: 'https://user@example.invalid/', parse }), { kind: 'invalid-request' });
  window.Readarr.apiRoot = '//external.invalid/api/v1';
  await assert.rejects(fetchJson({ path, parse }), { kind: 'invalid-request' });
  assert.equal(request.mock.callCount(), 2);
});

test('serializes false, zero, and null bodies and decodes 204 with no payload', async (context) => {
  const request = mockBrowser(context, () => new Response(null, { status: 204 }));

  for (const value of [false, 0, null]) {
    const result = await fetchJson({ path, method: 'POST', body: value, parse });
    assert.equal(result, undefined);
    const [, options] = request.mock.calls.at(-1).arguments;
    assert.equal(options.body, JSON.stringify(value));
    assert.equal(options.headers.get('Content-Type'), 'application/json');
  }

  const circular = {};
  circular.self = circular;
  await assert.rejects(fetchJson({ path, method: 'POST', body: circular, parse }), { kind: 'invalid-request' });
  assert.equal(request.mock.callCount(), 3);
});

test('preserves HTTP status and validation arrays, JSON, text and empty error bodies', async (context) => {
  const validation = [{ propertyName: 'Name', errorMessage: 'Required', isWarning: false }];
  const cases = [
    [new Response(JSON.stringify(validation), { status: 400 }), { format: 'json', value: validation }],
    [Response.json({ message: 'Problem' }, { status: 500 }), { format: 'json', value: { message: 'Problem' } }],
    [new Response('Too many requests', { status: 429 }), { format: 'text', value: 'Too many requests' }],
    [new Response(null, { status: 401, statusText: 'Unauthorized' }), { format: 'empty' }]
  ];
  const request = mockBrowser(context);

  for (const [response, body] of cases) {
    request.mock.mockImplementation(() => response);
    await assert.rejects(fetchJson({ path, parse: () => assert.fail('parsed an error as a success') }), (error) => {
      assert.ok(error instanceof ApiError);
      assert.equal(error.kind, 'http');
      assert.deepEqual(error.details, {
        kind: 'http', status: response.status, statusText: response.statusText, body
      });
      return true;
    });
  }
});

test('reports network failures separately from malformed or rejected JSON payloads', async (context) => {
  const request = mockBrowser(context, () => Promise.reject(new TypeError('fixture network failure')));
  await assert.rejects(fetchJson({ path, parse }), { kind: 'network' });
  request.mock.mockImplementation(() => new Response('{"data":'));
  await assert.rejects(fetchJson({ path, parse }), { kind: 'invalid-response' });
  request.mock.mockImplementation(() => new Response(''));
  await assert.rejects(fetchJson({ path, parse }), { kind: 'invalid-response' });
  request.mock.mockImplementation(() => Response.json({ data: null }));
  await assert.rejects(fetchJson({ path, parse: () => { throw new TypeError('fixture shape mismatch'); } }),
    { kind: 'invalid-response' });
});

test('caller cancellation rejects without dispatching stale payloads', async (context) => {
  let deliver;
  const request = mockBrowser(context, () => new Promise((resolve) => { deliver = resolve; }));
  const caller = new AbortController();
  const outstanding = fetchJson({ path, parse: () => assert.fail('parsed a late payload'), signal: caller.signal });
  const [, options] = request.mock.calls[0].arguments;
  caller.abort();
  assert.equal(options.signal.aborted, true);
  deliver(Response.json([]));
  await assert.rejects(outstanding, { kind: 'aborted' });
  await assert.rejects(fetchJson({ path, parse, signal: caller.signal }), { kind: 'aborted' });
  assert.equal(request.mock.callCount(), 1);
});

test('timeout applies while reading the body and removes the caller listener', async (context) => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  const caller = new AbortController();
  const removed = context.mock.method(caller.signal, 'removeEventListener');
  let reading;
  const bodyStarted = new Promise((resolve) => { reading = resolve; });
  mockBrowser(context, (_url, { signal }) => ({
    status: 200,
    ok: true,
    text() {
      reading();
      return new Promise((_resolve, reject) => {
        signal.addEventListener('abort', () => reject(new Error('fixture stream aborted')), { once: true });
      });
    }
  }));

  const request = fetchJson({ path, parse, signal: caller.signal, timeout: 100 });
  await bodyStarted;
  context.mock.timers.tick(100);
  caller.abort();
  await assert.rejects(request, { kind: 'timeout' });
  assert.equal(removed.mock.callCount(), 1);
  assert.equal(removed.mock.calls[0].arguments[0], 'abort');
});

test('response and network failure both clear the deadline and detach from the caller', async (context) => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  const request = mockBrowser(context, () => Response.json([]));

  for (const successful of [true, false]) {
    const caller = new AbortController();
    const added = context.mock.method(caller.signal, 'addEventListener');
    const removed = context.mock.method(caller.signal, 'removeEventListener');
    const options = { path, parse, timeout: 100, signal: caller.signal };
    if (successful) {
      assert.deepEqual(await fetchJson(options), []);
    } else {
      request.mock.mockImplementation(() => Promise.reject(new TypeError('fixture offline')));
      await assert.rejects(fetchJson(options), { kind: 'network' });
    }

    const [, sent] = request.mock.calls.at(-1).arguments;
    assert.equal(added.mock.callCount(), 1);
    assert.equal(removed.mock.callCount(), 1);
    assert.equal(added.mock.calls[0].arguments[1], removed.mock.calls[0].arguments[1]);
    caller.abort();
    context.mock.timers.tick(100);
    assert.equal(sent.signal.aborted, false);
  }
});

test('explicit v2 keeps the configured base path and leaves v1 as default', async (context) => {
  const request = mockBrowser(context, () => Response.json([]), '/owned/base/api/v1/');
  await fetchJson({ path: '/queue', apiVersion: 2, parse });
  await fetchJson({ path: '/queue', parse });
  assert.equal(request.mock.calls[0].arguments[0].href, origin + '/owned/base/api/v2/queue');
  assert.equal(request.mock.calls[1].arguments[0].href, origin + '/owned/base/api/v1/queue');
  assert.equal(request.mock.calls[0].arguments[1].headers.get('X-Api-Key'), key);
  await assert.rejects(fetchJson({ path: 'https://external.invalid/queue', apiVersion: 2, parse }), { kind: 'invalid-request' });
  await assert.rejects(fetchJson({ path: '/../v1/queue', apiVersion: 2, parse }), { kind: 'invalid-request' });
  await assert.rejects(fetchJson({ path: '/queue', apiVersion: 3, parse }), { kind: 'invalid-request' });
  assert.equal(request.mock.callCount(), 2);
});
