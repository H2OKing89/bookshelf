import assert from 'node:assert/strict';
import test from 'node:test';

import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const state = loadFrontendModule('Settings/DownloadClients/Options/downloadClientOptionsState');
const initial = {
  id: 1,
  downloadClientWorkingFolders: '_UNPACK_|_FAILED_',
  enableCompletedDownloadHandling: true,
  autoRedownloadFailed: true,
  autoRedownloadFailedFromInteractiveSearch: false
};

function configureApi(t) {
  const previousWindow = Object.getOwnPropertyDescriptor(global, 'window');
  Object.defineProperty(global, 'window', {
    configurable: true,
    value: {
      location: { origin: 'http://localhost' },
      Readarr: { apiRoot: '/fixture/api/v1', apiKey: 'fixture-key' }
    }
  });
  t.after(() => {
    if (previousWindow) {
      Object.defineProperty(global, 'window', previousWindow);
    } else {
      delete global.window;
    }
  });
}

test('download handling drafts preserve false, remove reverted fields, and remain isolated', () => {
  const { changeDownloadClientOption, isDownloadClientOption } = state;
  const first = changeDownloadClientOption(initial, {}, 'enableCompletedDownloadHandling', false);
  const second = changeDownloadClientOption(initial, first, 'autoRedownloadFailedFromInteractiveSearch', true);

  assert.deepEqual(first, { enableCompletedDownloadHandling: false });
  assert.deepEqual(second, { enableCompletedDownloadHandling: false, autoRedownloadFailedFromInteractiveSearch: true });
  assert.equal(changeDownloadClientOption(initial, first, 'enableCompletedDownloadHandling', false), first);
  assert.deepEqual(changeDownloadClientOption(initial, first, 'enableCompletedDownloadHandling', true), {});
  assert.deepEqual(changeDownloadClientOption(initial, second, 'autoRedownloadFailedFromInteractiveSearch', false), first);
  assert.deepEqual(changeDownloadClientOption(initial, {}, 'autoRedownloadFailedFromInteractiveSearch', false), {});
  assert.equal(isDownloadClientOption('downloadClientWorkingFolders'), false);
  assert.equal(isDownloadClientOption('password'), false);
  assert.equal(initial.enableCompletedDownloadHandling, true);
});

test('download handling uses an authenticated URL-based settings read and the canonical PUT response', async (t) => {
  configureApi(t);
  const canonical = { ...initial, enableCompletedDownloadHandling: false, downloadClientWorkingFolders: '_NORMALIZED_' };
  const requests = [];
  t.mock.method(global, 'fetch', async (url, options) => {
    requests.push({ url: String(url), ...options });
    return Response.json(options.method === 'PUT' ? { ...canonical, providerPassword: 'unrelated-fixture' } : initial, {
      status: options.method === 'PUT' ? 202 : 200
    });
  });

  const query = state.downloadClientOptionsQuery('/fixture/api/v1');
  assert.notDeepEqual(query.queryKey, state.downloadClientOptionsQuery('/other/api/v1').queryKey);
  const loaded = await query.queryFn({ signal: new AbortController().signal });
  const body = { ...loaded, enableCompletedDownloadHandling: false };
  assert.deepEqual(await state.saveDownloadClientOptions(body), canonical);

  assert.equal(requests[0].url, 'http://localhost/fixture/api/v1/config/downloadclient');
  assert.equal(requests[1].url, 'http://localhost/fixture/api/v1/config/downloadclient/1');
  assert.equal(requests[1].method, 'PUT');
  assert.equal(requests[1].headers.get('X-Api-Key'), 'fixture-key');
  assert.equal(requests[1].headers.get('X-Requested-With'), 'XMLHttpRequest');
  assert.equal(requests[1].headers.get('Content-Type'), 'application/json');
  assert.deepEqual(JSON.parse(requests[1].body), body);
});

test('download handling keeps field validation and rejects malformed canonical settings', async (t) => {
  configureApi(t);
  const { downloadClientOptionValidation, saveDownloadClientOptions } = state;
  const error = { propertyName: 'EnableCompletedDownloadHandling', errorMessage: 'Owned validation fixture' };
  const warning = { ...error, isWarning: true, errorMessage: 'Owned warning fixture' };
  const send = t.mock.method(global, 'fetch', async () => Response.json([null, error, warning, { propertyName: 2 }], { status: 400 }));

  await assert.rejects(saveDownloadClientOptions(initial), (failure) => {
    assert.equal(failure.kind, 'http');
    const validation = downloadClientOptionValidation(failure, 'enableCompletedDownloadHandling');
    assert.equal(validation.errors[0].message, error.errorMessage);
    assert.equal(validation.warnings[0].message, warning.errorMessage);
    assert.deepEqual(downloadClientOptionValidation(failure, 'autoRedownloadFailed'), { errors: [], warnings: [] });
    return true;
  });
  send.mock.mockImplementation(async () => Response.json({ id: 1 }, { status: 202 }));
  await assert.rejects(saveDownloadClientOptions(initial), { kind: 'invalid-response' });
});
