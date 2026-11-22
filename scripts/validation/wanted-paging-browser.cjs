const path = require('path');
const { deferred, pageButton, report, runBrowser, selectPage, signIn } = require('./history-paging-browser.cjs');
const { api, isAuthenticatedFetch, observeHub, putOwned, requestArgs, responseFor, rows, selection } = require('./operational-paging-browser.cjs');

const missing = 'wanted/missing';
const cutoff = 'wanted/cutoff';
const listPath = '/' + missing;

function idsMatch(params, name, expected) {
  const ids = params.getAll(name).map(Number).sort((left, right) => left - right);
  return ids.join(',') === [...expected].sort((left, right) => left - right).join(',');
}

function action(page, name) {
  return page.getByRole('button', { name, exact: true });
}

async function associateOwnedRecording(page, base, { importFile, importBook }) {
  const resource = 'bookfile/' + importFile + '/recordingreview';
  const key = await page.evaluate(() => window.Readarr.apiKey);
  const preview = await page.context().request.get(api(base, resource), { headers: { 'X-Api-Key': key }, maxRedirects: 0 });
  const { candidates } = preview.ok() ? await preview.json() : {};
  const editionId = importBook + 1000;
  if (candidates?.length !== 1 || candidates[0].editionId !== editionId || !candidates[0].revision) {
    throw new Error('OwnedWantedAssociationPreviewMissing');
  }
  return putOwned(page, base, resource, { editionId, revision: candidates[0].revision });
}

async function verifyWanted(page, base, config, { check, step, output }) {
  const hub = await observeHub(page, base);
  const changedBook = config.unmonitorBook;
  const searches = [];
  const monitoring = [];
  let held;
  let emptyCutoffQueueDetails;
  await page.route(api(base, 'queue/details'), async route => {
    const args = requestArgs(base, route.request(), 'queue/details');
    if (!args || !idsMatch(args, 'bookIds', config.cutoffBooks)) return route.fallback();
    const response = await route.fetch({ maxRedirects: 0 });
    const details = response.ok() ? await response.json() : null;
    emptyCutoffQueueDetails = Array.isArray(details) && details.length === 0;
    // Supply one owned queue detail and one out-of-page decoy solely in the browser.
    return route.fulfill({ json: [{ id: 9100, bookId: config.cutoffBooks[0] }, { id: 9101, bookId: changedBook }] });
  });
  await page.route(url => url.origin + url.pathname === api(base, missing), route => {
    const args = requestArgs(base, route.request(), missing);
    if (held && args?.get('page') === '2' && args.get('sortDirection') === 'descending') {
      const gate = held;
      held = undefined;
      return gate(route);
    }
    return route.fallback();
  });
  await page.route(api(base, 'command'), route => {
    if (route.request().method() !== 'POST') return route.fallback();
    const payload = route.request().postDataJSON();
    searches.push(payload);
    return route.fulfill({ status: 201, json: { id: 9100, name: 'BookSearch', status: 'completed', body: payload } });
  });
  await page.route(api(base, 'book/monitor'), route => {
    if (route.request().method() !== 'PUT') return route.fallback();
    const payload = route.request().postDataJSON();
    monitoring.push(payload);
    if (payload?.monitored === false && payload.bookIds?.length === 1 && payload.bookIds[0] === changedBook) {
      return route.continue();
    }
    return route.abort();
  });

  step('wanted_missing_authenticated_initial_page');
  const first = responseFor(page, base, missing);
  await signIn(page, config, listPath);
  const [initial] = await Promise.all([first, hub.started]);
  const original = await initial.json();
  await selection(page, config.importBook).waitFor();
  check('missing_uses_authenticated_prefixed_native_fetch', await isAuthenticatedFetch(page, initial.request()) &&
    original.totalRecords === 6 && await rows(page).count() === 6);

  step('missing_open_table_options');
  await action(page, 'Table Options').first().click();
  const options = page.getByRole('dialog', { name: 'Table Options', exact: true });
  await options.waitFor();
  step('missing_change_page_size');
  const resized = responseFor(page, base, missing, args => args.get('pageSize') === '5' && args.get('page') === '1');
  await options.getByLabel('Page Size', { exact: true }).fill('5');
  await resized;
  step('missing_close_options_and_select');
  await page.keyboard.press('Escape');
  await options.waitFor({ state: 'hidden' });
  await pageButton(page, 1, 2).waitFor();
  await selection(page, config.importBook).check();
  await action(page, 'Search Selected').locator('xpath=self::*[not(@disabled)]').waitFor();

  step('missing_stale_guard_and_out_of_order_page');
  const release = deferred();
  const returned = deferred();
  held = async route => {
    try {
      const response = await route.fetch({ maxRedirects: 0 });
      await release.promise;
      await route.fulfill({ response });
    } catch {
      // The browser has already cancelled the superseded request.
    } finally {
      returned.resolve();
    }
  };
  try {
    const pendingRequest = page.waitForRequest(request => requestArgs(base, request, missing)?.get('page') === '2');
    await selectPage(page, 1, 2, 2);
    const pending = await pendingRequest;
    const search = action(page, 'Search Selected');
    const unmonitor = action(page, 'Unmonitor Selected');
    await search.locator('xpath=self::*[@disabled or @aria-disabled="true"]').waitFor();
    check('placeholder_rows_cannot_search_or_change_monitoring', await rows(page).count() === 5 &&
      await selection(page, config.importBook).isVisible() && await search.isDisabled() && await unmonitor.isDisabled() &&
      await rows(page).first().getByRole('button', { name: 'Search Book', exact: true }).isDisabled());
    await search.evaluate(button => button.click());
    await unmonitor.evaluate(button => button.click());
    check('stale_actions_send_no_mutation_or_search', searches.length === 0 && monitoring.length === 0);

    const cancelled = page.waitForEvent('requestfailed', { predicate: request => request === pending });
    const sorted = responseFor(page, base, missing, args => args.get('sortDirection') === 'ascending');
    await page.getByRole('columnheader', { name: 'Release Date', exact: true }).click();
    const [aborted, current] = await Promise.all([cancelled, sorted]);
    release.resolve();
    await returned.promise;
    const result = await current.json();
    const currentId = result.records[0].id;
    await selection(page, currentId).locator('xpath=self::*[not(@disabled)]').waitFor();
    check('latest_sort_supersedes_the_held_page', aborted.failure()?.errorText === 'net::ERR_ABORTED' &&
      await rows(page).count() === result.records.length && await selection(page, currentId).isEnabled());
    if (result.page === 1) {
      await selectPage(page, 1, 2, 2);
    }
  } finally {
    release.resolve();
  }

  step('missing_back_preserves_page');
  await pageButton(page, 2, 2).waitFor();
  await selection(page, config.missedMonitorBook).locator('xpath=self::*[not(@disabled)]').waitFor();
  await rows(page).first().getByRole('link', { name: config.titles[config.missedMonitorBook], exact: true }).click();
  await page.waitForURL(url => url.pathname.includes('/book/'));
  await page.goBack();
  await page.waitForURL(url => url.pathname.endsWith(listPath));
  await pageButton(page, 2, 2).waitFor();
  await selection(page, config.missedMonitorBook).locator('xpath=self::*[not(@disabled)]').waitFor();
  check('back_restores_selected_missing_page', await rows(page).count() === 1);

  step('missing_actions_are_bound_to_the_current_selection');
  await selectPage(page, 2, 2, 1);
  await selection(page, changedBook).check();
  const found = page.waitForResponse(response => requestArgs(base, response.request(), 'command', 'POST') !== null);
  await action(page, 'Search Selected').click();
  await found;
  check('search_posts_only_the_active_owned_book', searches.length === 1 &&
    searches[0].name === 'BookSearch' && searches[0].bookIds?.join(',') === String(changedBook));
  const monitored = responseFor(page, base, missing, args => args.get('monitored') === 'true');
  await action(page, 'Unmonitor Selected').click();
  const refreshed = await monitored;
  await selection(page, changedBook).waitFor({ state: 'hidden' });
  check('monitoring_write_posts_only_the_active_owned_book', monitoring.length === 1 && monitoring[0].monitored === false &&
    monitoring[0].bookIds?.join(',') === String(changedBook) && (await refreshed.json()).totalRecords === 5);

  step('missing_live_recording_association');
  const afterImport = responseFor(page, base, missing);
  const [importPage] = await Promise.all([afterImport, associateOwnedRecording(page, base, config)]);
  await selection(page, config.importBook).waitFor({ state: 'hidden' });
  check('real_file_association_removes_the_missing_recording', (await importPage.json()).totalRecords === 4 &&
    hub.messages.some(event => event.name.toLowerCase() === 'bookfile' && event.action === 'updated' && event.id === config.importFile));

  step('cutoff_list_and_page_scoped_details');
  const cutoffRequest = responseFor(page, base, cutoff, args => args.get('page') === '1');
  const queueDetails = responseFor(page, base, 'queue/details', args => idsMatch(args, 'bookIds', config.cutoffBooks));
  const fileDetails = responseFor(page, base, 'bookfile', args => idsMatch(args, 'bookId', config.cutoffBooks));
  await page.getByRole('link', { name: 'Cutoff Unmet', exact: true }).click();
  const [cutoffResponse, detailQueue, detailFiles] = await Promise.all([cutoffRequest, queueDetails, fileDetails]);
  const cutoffRows = await cutoffResponse.json();
  const files = await detailFiles.json();
  await selection(page, config.cutoffBooks[0]).waitFor();
  const firstCutoff = selection(page, config.cutoffBooks[0]).locator('xpath=ancestor::tr');
  const secondCutoff = selection(page, config.cutoffBooks[1]).locator('xpath=ancestor::tr');
  await firstCutoff.getByText('MP3 · Queued', { exact: true }).waitFor();
  check('cutoff_uses_authenticated_fetch_and_exact_current_page_details',
    await Promise.all([cutoffResponse, detailQueue, detailFiles].map(response => isAuthenticatedFetch(page, response.request()))).then(results => results.every(Boolean)) &&
    cutoffRows.totalRecords === 2 && emptyCutoffQueueDetails === true &&
    files.map(file => file.bookId).sort((a, b) => a - b).join(',') === config.cutoffBooks.join(',') &&
    files.every(file => file.quality.quality.name === 'MP3') &&
    await secondCutoff.getByText('MP3', { exact: true }).isVisible());

  step('wanted_reconnect_catches_missed_monitoring');
  const missingAgain = responseFor(page, base, missing);
  await page.getByRole('link', { name: 'Missing', exact: true }).click();
  await missingAgain;
  const missedId = config.missedMonitorBook;
  await selection(page, missedId).waitFor();
  const dropped = deferred();
  hub.drop(event => {
    const wanted = /^(book|wanted\/(missing|cutoff))$/i.test(event.name) && event.action === 'updated' && event.id === missedId;
    if (wanted) dropped.resolve();
    return wanted;
  });
  await putOwned(page, base, 'book/monitor', { bookIds: [missedId], monitored: false });
  await Promise.race([dropped.promise, page.waitForTimeout(4500).then(() => { throw new Error('OwnedBookHubEventMissing'); })]);
  check('fixture_misses_the_book_monitoring_event', await selection(page, missedId).isVisible());
  const catchUp = responseFor(page, base, missing);
  const { socket, server } = hub.connections[0];
  const close = { code: 4000, reason: 'Owned fixture reconnect' };
  const [reconnected] = await Promise.all([catchUp, socket.close(close), server.close(close)]);
  await selection(page, missedId).waitFor({ state: 'hidden' });
  check('reconnect_refreshes_missing_membership', hub.connections.length === 2 && (await reconnected.json()).totalRecords === 3);
  await page.screenshot({ path: path.join(output, 'owned-wanted-missing.png'), fullPage: true });
}

report(config => runBrowser(config, verifyWanted));
