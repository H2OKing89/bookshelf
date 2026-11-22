const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');
const { api, isAuthenticatedFetch, observeHub, requestArgs, responseFor, rows, selection } = require('./operational-paging-browser.cjs');

function confirmedRow(page, id) {
  return rows(page).filter({ has: page.locator(`input[type="checkbox"][name="${id}"]`) });
}

function queueRecord(id) {
  const unknown = id === 799;
  return {
    id,
    title: 'Owned Queue Release ' + id,
    status: 'downloading',
    trackedDownloadStatus: 'ok',
    trackedDownloadState: 'downloading',
    statusMessages: [],
    authorId: unknown ? undefined : 11,
    bookId: unknown ? undefined : id % 2 ? 104 : 105,
    quality: { quality: { id: 12, name: 'M4B' }, revision: { version: 1, real: 0, isRepack: false } },
    customFormats: [],
    customFormatScore: 0,
    size: 11000,
    sizeleft: 3500,
    timeleft: '00:04:00',
    downloadId: 'owned-shared-download-identifier',
    downloadClient: id % 2 ? 'Owned Queue Client A' : 'Owned Queue Client B',
    downloadClientId: id % 2 ? 21 : 22,
    downloadClientAvailable: true,
    downloadClientHasPostImportCategory: true,
    protocol: 'torrent',
    indexer: 'Owned synthetic indexer',
    downloadForced: false
  };
}

function queueBurst(connection) {
  const event = JSON.stringify({ type: 1, target: 'receiveMessage', arguments: [{ name: 'queue', body: { action: 'sync' } }] });
  return connection.socket.send(`${event}\x1e`.repeat(5));
}

async function verifyQueue(page, base, config, { check, step, output }) {
  const hub = await observeHub(page, base);
  const readCalls = [];
  const removalCalls = [];
  let known = [701, 702];
  let fixtureIsEmpty;
  await page.route(api(base, 'queue'), async route => {
    const args = requestArgs(base, route.request(), 'queue');
    if (!args) return route.fallback();
    if (fixtureIsEmpty === undefined) {
      const response = await route.fetch({ maxRedirects: 0 });
      const data = response.ok() ? await response.json() : {};
      fixtureIsEmpty = data.totalRecords === 0 && data.records?.length === 0;
    }
    readCalls.push(args.get('includeUnknownAuthorItems'));
    const ids = args.get('includeUnknownAuthorItems') === 'true' ? [...known, 799] : known;
    return route.fulfill({ json: {
      page: Number(args.get('page') || 1),
      pageSize: Number(args.get('pageSize') || 20),
      sortKey: args.get('sortKey') || 'timeleft',
      sortDirection: args.get('sortDirection') || 'ascending',
      totalRecords: ids.length,
      records: ids.map(queueRecord)
    } });
  });
  await page.route(url => url.origin + url.pathname === api(base, 'queue/bulk'), route => {
    const args = requestArgs(base, route.request(), 'queue/bulk', 'DELETE');
    if (!args) return route.fallback();
    removalCalls.push({ ids: route.request().postDataJSON()?.ids, options: Object.fromEntries(args) });
    // The synthetic IDs exist in the browser response only. No removal reaches the backend.
    return route.fulfill({ status: 404, json: { message: 'Owned queue target has already left the client' } });
  });

  step('queue_authenticated_read');
  const first = responseFor(page, base, 'queue');
  await signIn(page, config, '/activity/queue');
  const [initial] = await Promise.all([first, hub.started]);
  check('queue_uses_authenticated_prefixed_native_fetch', fixtureIsEmpty === true &&
    await isAuthenticatedFetch(page, initial.request()) && (await initial.json()).records.length === 3 && readCalls[0] === 'true');
  await selection(page, 799).waitFor();

  step('queue_distinguishes_saved_unknown_author_option');
  await page.getByRole('button', { name: 'Table Options', exact: true }).first().click();
  const options = page.getByRole('dialog', { name: 'Table Options', exact: true });
  const excludes = responseFor(page, base, 'queue', args => args.get('includeUnknownAuthorItems') === 'false');
  await options.getByLabel('Show Unknown Author Items', { exact: true }).uncheck();
  const reduced = await excludes;
  await page.keyboard.press('Escape');
  await options.waitFor({ state: 'hidden' });
  await selection(page, 799).waitFor({ state: 'hidden' });
  check('queue_explicit_false_selects_the_matching_cache_page', (await reduced.json()).records.length === 2);

  step('single_removal_confirmation_holds_live_refresh');
  await confirmedRow(page, 701).getByRole('button', { name: 'Remove from queue', exact: true }).click();
  const single = page.getByRole('dialog', { name: 'Remove - Owned Queue Release 701', exact: true });
  await single.waitFor();
  const readsWhileSingleOpen = readCalls.length;
  known = [703, 702];
  await queueBurst(hub.connections[0]);
  await page.waitForTimeout(350);
  check('single_removal_keeps_its_original_target_during_hub_burst', await single.isVisible() &&
    readCalls.length === readsWhileSingleOpen && await selection(page, 701).isVisible());
  const resumed = responseFor(page, base, 'queue');
  await single.getByRole('button', { name: 'Close', exact: true }).last().click();
  await resumed;
  await selection(page, 703).waitFor();
  await page.waitForTimeout(200);
  check('closing_single_confirmation_releases_one_refresh', readCalls.length === readsWhileSingleOpen + 1 &&
    await selection(page, 701).count() === 0 && removalCalls.length === 0);

  step('bulk_confirmation_keeps_client_scoped_selection_and_options');
  await selection(page, 703).check();
  await selection(page, 702).check();
  await page.getByRole('button', { name: 'Remove Selected', exact: true }).click();
  const bulk = page.getByRole('dialog', { name: 'Remove Selected Items', exact: true });
  const blocklist = bulk.locator('select[name="blocklistMethod"]');
  await blocklist.selectOption('blocklistOnly');
  await bulk.locator('select[name="removalMethod"]').selectOption('changeCategory');
  const readsWhileBulkOpen = readCalls.length;
  known = [704, 703];
  await queueBurst(hub.connections[0]);
  await page.waitForTimeout(350);
  check('bulk_confirmation_holds_original_row_set_and_options', await bulk.isVisible() &&
    await selection(page, 702).isVisible() && readCalls.length === readsWhileBulkOpen && await blocklist.inputValue() === 'blocklistOnly');
  const removal = page.waitForResponse(response => requestArgs(base, response.request(), 'queue/bulk', 'DELETE') !== null);
  const afterConflict = responseFor(page, base, 'queue');
  await bulk.getByRole('button', { name: 'Remove', exact: true }).click();
  const refused = await removal;
  await afterConflict;
  await selection(page, 704).waitFor();
  check('bulk_action_sends_captured_queue_ids_even_when_download_ids_agree', refused.status() === 404 &&
    removalCalls.length === 1 && removalCalls[0].ids?.slice().sort().join(',') === '702,703' &&
    removalCalls[0].options.removeFromClient === 'false' && removalCalls[0].options.changeCategory === 'true' &&
    removalCalls[0].options.blocklist === 'true' && removalCalls[0].options.skipRedownload === 'true' &&
    await selection(page, 702).count() === 0);
  check('removed_target_is_reported_without_retrying_the_mutation', await page.getByRole('alert')
    .filter({ hasText: 'Unable to complete the queue action.' }).isVisible() && removalCalls.length === 1);

  step('queue_reconnect_keeps_explicit_saved_option');
  const caughtUp = responseFor(page, base, 'queue', args => args.get('includeUnknownAuthorItems') === 'false');
  known = [704];
  const { socket, server } = hub.connections[0];
  const close = { code: 4000, reason: 'Owned fixture reconnect' };
  const [reconnected] = await Promise.all([caughtUp, socket.close(close), server.close(close)]);
  await selection(page, 703).waitFor({ state: 'hidden' });
  check('queue_reconnect_uses_the_current_page_key', hub.connections.length === 2 &&
    (await reconnected.json()).totalRecords === 1 && await selection(page, 704).isVisible());
  await page.screenshot({ path: path.join(output, 'owned-activity-queue.png'), fullPage: true });
}

report(config => runBrowser(config, verifyQueue));
