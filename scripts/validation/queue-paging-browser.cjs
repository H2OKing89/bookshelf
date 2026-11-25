const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');
const { api, isAuthenticatedFetch, observeHub, rows, setCheckbox } = require('./operational-paging-browser.cjs');

function queueApi(base, resource) {
  const url = api(base, resource);
  return resource.startsWith('queue') ? url.replace('/api/v1/', '/api/v2/') : url;
}

function requestArgs(base, request, resource, method = 'GET') {
  const url = new URL(request.url());
  return request.method() === method && url.origin + url.pathname === queueApi(base, resource) ? url.searchParams : null;
}

function responseFor(page, base, resource, match = () => true) {
  return page.waitForResponse(response => {
    const args = requestArgs(base, response.request(), resource);
    return response.ok() && args !== null && match(args);
  });
}

function selection(page, id) {
  return page.getByRole('table').locator(`tbody input[type="checkbox"][name="${queueRecord(id).id}"]`);
}

function confirmedRow(page, id) {
  return rows(page).filter({ has: page.locator(`input[type="checkbox"][name="${queueRecord(id).id}"]`) });
}

function queueRecord(id) {
  const unknown = id === 799;
  const downloadClientId = id % 2 ? 21 : 22;
  const downloadId = id === 702 || id === 703 ? 'owned-shared-download-identifier' : 'owned-task-' + id;
  const recordings = unknown ? [] : [
    { bookId: 104, title: 'Owned recording US', titleSlug: 'owned-us', recordingId: 'owned-us' },
    ...(id % 2 ? [{ bookId: 105, title: 'Owned recording UK', titleSlug: 'owned-uk', recordingId: 'owned-uk' }] : [])
  ];
  return {
    id: `download:${downloadClientId}:${downloadId}`,
    revision: 'owned-revision-' + id,
    kind: 'download',
    coverageSource: unknown ? 'unknown' : 'recordedGrab',
    recordings,
    author: unknown ? undefined : { authorName: 'Owned author', titleSlug: 'owned-author' },
    canRemove: true,
    canRemoveFromClient: true,
    canIgnore: !unknown,
    canChangeCategory: true,
    canBlocklist: !unknown,
    canRetry: !unknown,
    canGrab: false,
    canImport: false,
    title: 'Owned Queue Release ' + id,
    status: 'downloading',
    trackedDownloadStatus: 'ok',
    trackedDownloadState: 'downloading',
    statusMessages: [],
    authorId: unknown ? undefined : 11,
    quality: { quality: { id: 12, name: 'M4B' }, revision: { version: 1, real: 0, isRepack: false } },
    customFormats: [],
    customFormatScore: 0,
    size: 11000,
    sizeleft: 3500,
    timeleft: '00:04:00',
    downloadId,
    downloadClient: id % 2 ? 'Owned Queue Client A' : 'Owned Queue Client B',
    downloadClientId,
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

async function verifyQueuePreferences(page, base, { check, step }) {
  const legacyQueue = {
    options: { includeUnknownAuthorItems: false },
    paged: {
      pageSize: 37,
      sortKey: 'books.title',
      sortDirection: 'descending',
      columns: [
        { name: 'status', isVisible: true },
        { name: 'books.title', isVisible: true },
        { name: 'authorMetadata.sortName', isVisible: true },
        { name: 'books.releaseDate', isVisible: false },
        { name: 'protocol', isVisible: true, label: 'Old saved label' },
        { name: 'quality', isVisible: false },
        { name: 'obsolete-column', isVisible: true },
        { name: 'actions', isVisible: true }
      ]
    }
  };
  const key = `bookshelf:queue-preferences:${new URL(api(base, 'queue')).pathname.replace(/\/queue$/, '')}`;
  await page.addInitScript(({ key, legacyQueue }) => {
    const marker = 'owned-legacy-queue-seeded';
    if (!sessionStorage.getItem(marker)) {
      sessionStorage.setItem(marker, '1');
      const saved = JSON.parse(localStorage.getItem('readarr') ?? '{}');
      localStorage.setItem('readarr', JSON.stringify({ ...saved, queue: legacyQueue }));
      localStorage.removeItem(key);
    }
  }, { key, legacyQueue });

  step('queue_reads_legacy_table_preferences_on_upgrade');
  const matches = (args, direction) => args.get('pageSize') === '37' && args.get('sortKey') === 'book.title' &&
    args.get('sortDirection') === direction && args.get('includeUnknownAuthorItems') === 'false';
  const migrated = responseFor(page, base, 'queue', args => matches(args, 'descending'));
  await page.reload();
  await migrated;
  const headers = page.getByRole('table').getByRole('columnheader');
  await headers.filter({ hasText: 'Protocol' }).waitFor();
  const names = (await headers.allTextContents()).map(name => name.trim());
  check('legacy_layout_and_visibility_are_retained', names.indexOf('Book Title') >= 0 &&
    names.indexOf('Author') > names.indexOf('Book Title') && !names.includes('Quality') && !names.includes('Old saved label'));
  check('legacy_page_size_sort_and_explicit_false_reach_the_queue_request', await selection(page, 799).count() === 0);

  step('queue_reloads_changes_from_its_versioned_preference_store');
  const ascending = responseFor(page, base, 'queue', args => matches(args, 'ascending'));
  await headers.filter({ hasText: 'Book Title' }).click();
  await ascending;
  await page.getByRole('button', { name: 'Table Options', exact: true }).first().click();
  const options = page.getByRole('dialog', { name: 'Table Options', exact: true });
  const unknown = options.locator('input[name="includeUnknownAuthorItems"]');
  await unknown.waitFor();
  check('legacy_unknown_author_setting_is_off_in_table_options', !await unknown.isChecked());
  await setCheckbox(options.locator('input[name="protocol"]'), false);
  await page.keyboard.press('Escape');
  const reloaded = responseFor(page, base, 'queue', args => matches(args, 'ascending'));
  await page.reload();
  await reloaded;
  await headers.filter({ hasText: 'Book Title' }).waitFor();
  check('queue_changes_survive_reload_in_the_versioned_store', await headers.filter({ hasText: 'Protocol' }).count() === 0 &&
    await page.evaluate(({ key, legacyQueue }) => {
      const next = JSON.parse(localStorage.getItem(key));
      const legacy = JSON.parse(localStorage.getItem('readarr')).queue;
      return next.version === 1 && next.includeUnknownAuthorItems === false &&
        next.columns.find(({ name }) => name === 'protocol')?.isVisible === false &&
        JSON.stringify(legacy) === JSON.stringify(legacyQueue);
    }, { key, legacyQueue }));

  step('queue_recovers_from_corrupt_browser_preferences');
  await page.addInitScript((key) => {
    localStorage.setItem('readarr', '{bad legacy preferences');
    localStorage.setItem(key, '{bad Queue preferences');
  }, key);
  const defaulted = responseFor(page, base, 'queue', args => args.get('pageSize') === '20' &&
    args.get('sortKey') === 'timeleft' && args.get('sortDirection') === 'ascending' && args.get('includeUnknownAuthorItems') === 'true');
  await page.reload();
  await defaulted;
  await selection(page, 799).waitFor();
  check('corrupt_storage_opens_the_queue_with_defaults', await headers.filter({ hasText: 'Quality' }).isVisible() &&
    await headers.filter({ hasText: 'Protocol' }).count() === 0);
}

async function verifyQueueFilters(page, base, apiKey, { check, step, output }) {
  step('queue_filter_api_accepts_only_supported_values');
  const request = search => page.context().request.get(queueApi(base, 'queue') + '?' + search, {
    headers: { 'X-Api-Key': apiKey }, maxRedirects: 0
  });
  const [valid, invalidStatus, invalidProtocol] = await Promise.all([
    request('status=downloading&protocol=torrent&includeUnknownAuthorItems=false'),
    request('status=obsolete'),
    request('protocol=42')
  ]);
  check('queue_api_binds_filters_and_rejects_invalid_values', valid.ok() && (await valid.json()).totalRecords === 0 &&
    invalidStatus.status() === 400 && invalidProtocol.status() === 400);

  const filter = page.getByRole('button', { name: /^Queue Filters/ });
  const dialog = page.getByRole('dialog', { name: 'Queue Filters', exact: true });
  const status = dialog.getByLabel('Download Status', { exact: true });
  const protocol = dialog.getByLabel('Protocol', { exact: true });
  const apply = dialog.getByRole('button', { name: 'Apply Filters', exact: true });
  const set = async (statusValue, protocolValue) => {
    await filter.click();
    await status.selectOption(statusValue);
    await protocol.selectOption(protocolValue);
  };

  step('queue_combines_status_and_protocol');
  await setCheckbox(selection(page, 704), true);
  await set('downloading', 'torrent');
  const combinedArgs = args => args.get('status') === 'downloading' && args.get('protocol') === 'torrent' &&
    args.get('includeUnknownAuthorItems') === 'false' && args.get('page') === '1';
  const combined = responseFor(page, base, 'queue', combinedArgs);
  await apply.click();
  const matched = await combined;
  await selection(page, 704).locator('xpath=self::*[not(@disabled)]').waitFor();
  check('combined_filters_select_a_new_authenticated_page_and_clear_selection', await isAuthenticatedFetch(page, matched.request()) &&
    (await matched.json()).totalRecords === 1 && !await selection(page, 704).isChecked());

  step('queue_restores_applied_filters_after_reload');
  const restored = responseFor(page, base, 'queue', combinedArgs);
  await page.reload();
  await restored;
  await filter.click();
  await dialog.waitFor();
  check('saved_filters_and_explicit_false_survive_reload', await status.inputValue() === 'downloading' &&
    await protocol.inputValue() === 'torrent');
  await page.screenshot({ path: path.join(output, 'owned-queue-filters.png'), fullPage: true });
  await status.selectOption('failed');
  const empty = responseFor(page, base, 'queue', args => args.get('status') === 'failed' && args.get('protocol') === 'torrent');
  await apply.click();
  await empty;
  const noMatches = page.getByText('No queue items match these filters.', { exact: true });
  await noMatches.waitFor();
  check('filtered_empty_is_distinct_from_an_empty_queue', !await page.getByText('Queue is empty', { exact: true }).isVisible());

  step('queue_clear_filters_keeps_unknown_author_preference');
  await set('', '');
  const unfiltered = responseFor(page, base, 'queue', args => !args.has('status') && !args.has('protocol') &&
    args.get('includeUnknownAuthorItems') === 'false');
  await apply.click();
  await unfiltered;
  await selection(page, 704).waitFor();
  check('clearing_filters_restores_matching_unfiltered_rows', !await noMatches.isVisible() && !await selection(page, 799).isVisible());
}

async function verifyQueue(page, base, config, { check, step, output }) {
  const hub = await observeHub(page, base);
  const readCalls = [];
  const removalCalls = [];
  let known = [701, 702];
  let fixtureIsEmpty;
  await page.route(url => url.origin + url.pathname === queueApi(base, 'queue'), async route => {
    const args = requestArgs(base, route.request(), 'queue');
    if (!args) return route.fallback();
    if (fixtureIsEmpty === undefined) {
      const response = await route.fetch({ maxRedirects: 0 });
      const data = response.ok() ? await response.json() : {};
      fixtureIsEmpty = data.totalRecords === 0 && data.records?.length === 0;
    }
    readCalls.push(args.get('includeUnknownAuthorItems'));
    const status = args.get('status');
    const protocol = args.get('protocol');
    const ids = args.get('includeUnknownAuthorItems') === 'true' ? [...known, 799] : known;
    const records = ids.map(queueRecord).filter(item => (!status || item.status === status) && (!protocol || item.protocol === protocol));
    return route.fulfill({ json: {
      page: Number(args.get('page') || 1),
      pageSize: Number(args.get('pageSize') || 20),
      sortKey: args.get('sortKey') || 'timeleft',
      sortDirection: args.get('sortDirection') || 'ascending',
      totalRecords: records.length,
      records
    } });
  });
  await page.route(url => url.origin + url.pathname === queueApi(base, 'queue/remove'), route => {
    const args = requestArgs(base, route.request(), 'queue/remove', 'POST');
    if (!args) return route.fallback();
    removalCalls.push(route.request().postDataJSON());
    // The synthetic IDs exist in the browser response only. No removal reaches the backend.
    return route.fulfill({ status: 409, json: { message: 'Owned queue target has already left the client' } });
  });
  await page.route(api(base, 'command'), route => {
    if (route.request().method() === 'POST' && route.request().postDataJSON()?.name === 'RefreshMonitoredDownloads') {
      return route.continue();
    }
    return route.fallback();
  });

  step('queue_authenticated_read');
  const first = responseFor(page, base, 'queue');
  await signIn(page, config, '/activity/queue');
  const [initial] = await Promise.all([first, hub.started]);
  check('queue_uses_authenticated_prefixed_native_fetch', fixtureIsEmpty === true &&
    await isAuthenticatedFetch(page, initial.request()) && (await initial.json()).records.length === 3 && readCalls[0] === 'true');
  await selection(page, 799).waitFor();
  check('one_row_lists_all_recorded_coverage_and_unresolved_downloads_are_visible',
    await confirmedRow(page, 701).getByText('Owned recording US').count() === 1 &&
    await confirmedRow(page, 701).getByText('Owned recording UK').count() === 1 &&
    await confirmedRow(page, 799).getByText('Unresolved recording coverage').count() === 1);

  step('queue_distinguishes_saved_unknown_author_option');
  await page.getByRole('button', { name: 'Table Options', exact: true }).first().click();
  const options = page.getByRole('dialog', { name: 'Table Options', exact: true });
  const excludes = responseFor(page, base, 'queue', args => args.get('includeUnknownAuthorItems') === 'false');
  await setCheckbox(options.locator('input[name="includeUnknownAuthorItems"]'), false);
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

  step('bulk_confirmation_opens_for_two_selected_items');
  await setCheckbox(selection(page, 703), true);
  await setCheckbox(selection(page, 702), true);
  await page.getByRole('button', { name: 'Remove Selected', exact: true }).click();
  const bulk = page.getByRole('dialog', { name: 'Remove Selected Items', exact: true });
  await bulk.waitFor();
  step('bulk_confirmation_applies_clicked_removal_options');
  await bulk.getByRole('button', { name: /Do not Blocklist/i }).click();
  await page.getByText('Blocklist Only', { exact: true }).click();
  await bulk.getByRole('button', { name: /Remove from Download Client/i }).click();
  await page.getByText('Change Category', { exact: true }).click();
  const blocklist = bulk.getByRole('button', { name: /Blocklist Only/i });
  const changeCategory = bulk.getByRole('button', { name: /Change Category/i });
  step('bulk_confirmation_keeps_client_scoped_selection_and_options');
  const readsWhileBulkOpen = readCalls.length;
  known = [704, 703];
  await queueBurst(hub.connections[0]);
  await page.waitForTimeout(350);
  check('bulk_confirmation_holds_original_row_set_and_options', await bulk.isVisible() &&
    await selection(page, 702).isVisible() && readCalls.length === readsWhileBulkOpen &&
    await blocklist.isVisible() && await changeCategory.isVisible());
  step('bulk_confirmation_sends_the_captured_removal');
  const removal = page.waitForResponse(response => requestArgs(base, response.request(), 'queue/remove', 'POST') !== null);
  const afterConflict = responseFor(page, base, 'queue');
  await bulk.getByRole('button', { name: 'Remove', exact: true }).click();
  const refused = await removal;
  await afterConflict;
  await selection(page, 704).waitFor();
  check('bulk_action_sends_captured_queue_ids_even_when_download_ids_agree', refused.status() === 409 &&
    removalCalls.length === 1 && removalCalls[0].targets?.map(({ id, revision }) => id + ':' + revision).sort().join(',') ===
      [702, 703].map(queueRecord).map(({ id, revision }) => id + ':' + revision).sort().join(',') &&
    removalCalls[0].removeFromClient === false && removalCalls[0].changeCategory === true &&
    removalCalls[0].blocklist === true && removalCalls[0].skipRedownload === true &&
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

  step('queue_toolbar_refresh_finishes_once');
  const beforeManualRefresh = readCalls.length;
  const command = page.waitForResponse(response => requestArgs(base, response.request(), 'command', 'POST') !== null);
  const manualRead = responseFor(page, base, 'queue');
  const refresh = page.getByRole('button', { name: 'Refresh', exact: true });
  await refresh.click();
  const [issued] = await Promise.all([command, manualRead]);
  const issuedCommand = await issued.json();
  const apiKey = await page.evaluate(() => window.Readarr.apiKey);
  const status = await page.context().request.get(api(base, 'command/' + issuedCommand.id), {
    headers: { 'X-Api-Key': apiKey }, maxRedirects: 0
  });
  await refresh.locator('xpath=self::*[not(@disabled)]').waitFor();
  await page.waitForTimeout(200);
  check('manual_refresh_waits_for_the_real_command_then_fetches_once', issued.ok() && status.ok() &&
    (await status.json()).status === 'completed' && readCalls.length === beforeManualRefresh + 1);
  await page.screenshot({ path: path.join(output, 'owned-activity-queue.png'), fullPage: true });
  await verifyQueueFilters(page, base, apiKey, { check, step, output });
  await verifyQueuePreferences(page, base, { check, step });
}

async function verifyQueuePhone(page, base, config, { check, step, output }) {
  step('phone_queue_lists_all_recordings_once');
  await page.route(url => url.origin + url.pathname === queueApi(base, 'queue'), route => route.fulfill({
    json: { page: 1, pageSize: 20, totalRecords: 2, records: [queueRecord(701), queueRecord(799)] }
  }));
  await signIn(page, config, '/activity/queue');
  await selection(page, 701).waitFor();
  check('single_download_row_has_distinct_recording_links', await confirmedRow(page, 701).getByRole('link', { name: 'Owned recording US', exact: true }).count() === 1 &&
    await confirmedRow(page, 701).getByRole('link', { name: 'Owned recording UK', exact: true }).count() === 1 &&
    await confirmedRow(page, 799).getByText('Unresolved recording coverage').count() === 1);
  await setCheckbox(selection(page, 701), true);
  await page.getByRole('button', { name: 'Remove Selected', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Remove Selected Item', exact: true });
  await modal.waitFor();
  check('phone_selection_removes_one_download_not_its_book_count', await modal.getByRole('button', { name: 'Remove', exact: true }).isEnabled());
  await page.screenshot({ path: path.join(output, 'owned-activity-queue-phone.png'), fullPage: true });
  await modal.getByRole('button', { name: 'Close', exact: true }).last().click();
}

report(config => runBrowser(config, verifyQueue, verifyQueuePhone));
