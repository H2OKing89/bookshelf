const path = require('path');
const { deferred, pageButton, report, runBrowser, selectPage, signIn } = require('./history-paging-browser.cjs');
const { api, isAuthenticatedFetch, observeHub, putOwned, requestArgs, responseFor, rows, selection, setCheckbox } = require('./operational-paging-browser.cjs');

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

function selectAllBooks(page) {
  return page.getByRole('table').locator('thead input[name="selectAll"]');
}

function selectedBookIds(page) {
  return page.getByRole('table').locator('tbody input[type="checkbox"]:checked')
    .evaluateAll(inputs => inputs.map(input => Number(input.name)));
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
  await page.route(url => url.origin + url.pathname === api(base, 'queue/details'), async route => {
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
  const descending = (await (await resized).json()).records.map(({ id }) => id);
  step('missing_close_options_and_select');
  await page.keyboard.press('Escape');
  await options.waitFor({ state: 'hidden' });
  await pageButton(page, 1, 2).waitFor();

  step('missing_select_all_and_shift_range');
  const allBooks = selectAllBooks(page);
  await setCheckbox(allBooks, true);
  check('missing_select_all_is_scoped_to_the_current_page', (await selectedBookIds(page)).join(',') === descending.join(',') &&
    descending.length === 5);
  await setCheckbox(allBooks, false);
  await setCheckbox(selection(page, descending[0]), true);
  await selection(page, descending[2]).locator('..').click({ modifiers: ['Shift'] });
  check('missing_shift_select_uses_the_current_row_order', (await selectedBookIds(page)).join(',') === descending.slice(0, 3).join(','));

  step('missing_sort_preserves_survivors_and_clears_shift_anchor');
  const sort = page.getByRole('columnheader', { name: 'Release Date', exact: true });
  const ascendingResponse = responseFor(page, base, missing, args => args.get('page') === '1' && args.get('sortDirection') === 'ascending');
  await sort.click();
  const ascending = (await (await ascendingResponse).json()).records.map(({ id }) => id);
  await selection(page, ascending[0]).locator('xpath=self::*[not(@disabled)]').waitFor();
  const survivors = ascending.filter(id => descending.slice(0, 3).includes(id));
  check('missing_sort_keeps_only_selected_books_still_on_the_page', survivors.length === 2 &&
    (await selectedBookIds(page)).join(',') === survivors.join(','));
  await selection(page, ascending[1]).locator('..').click({ modifiers: ['Shift'] });
  check('missing_sort_clears_the_previous_shift_anchor', (await selectedBookIds(page)).join(',') === [ascending[1], ...survivors].join(','));

  await sort.click();
  await selection(page, descending[0]).locator('xpath=self::*[not(@disabled)]').waitFor();
  await setCheckbox(allBooks, false);
  await setCheckbox(selection(page, config.importBook), true);
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
      await allBooks.isDisabled() && await selection(page, config.importBook).isDisabled() &&
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
  await setCheckbox(selection(page, changedBook), true);
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
  // A refresh from the previous monitoring write can reach the browser before
  // the association refresh. The page and SignalR event can also arrive in either order.
  const afterImport = page.waitForResponse(async response => {
    if (!response.ok() || !requestArgs(base, response.request(), missing)) return false;
    try {
      return (await response.json()).totalRecords === 4;
    } catch {
      return false;
    }
  });
  const fileUpdated = deferred();
  const isImportEvent = event => event.name.toLowerCase() === 'bookfile' &&
    event.action === 'updated' && event.id === config.importFile;
  hub.drop(event => {
    if (isImportEvent(event)) fileUpdated.resolve();
    return false;
  });
  try {
    const [importPage] = await Promise.all([
      afterImport,
      associateOwnedRecording(page, base, config),
      Promise.race([fileUpdated.promise, page.waitForTimeout(4500).then(() => { throw new Error('OwnedBookFileHubEventMissing'); })])
    ]);
    await selection(page, config.importBook).waitFor({ state: 'hidden' });
    check('real_file_association_removes_the_missing_recording', (await importPage.json()).totalRecords === 4 &&
      hub.messages.some(isImportEvent));
  } finally {
    hub.drop(() => false);
  }

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

  step('cutoff_select_all_searches_only_active_books');
  await setCheckbox(selectAllBooks(page), true);
  const cutoffSearch = page.waitForResponse(response => requestArgs(base, response.request(), 'command', 'POST') !== null);
  await action(page, 'Search Selected').click();
  await cutoffSearch;
  check('cutoff_select_all_sends_the_current_local_book_ids', searches.length === 2 &&
    searches[1].name === 'BookSearch' && searches[1].bookIds?.join(',') === config.cutoffBooks.join(','));

  step('wanted_reconnect_catches_missed_monitoring');
  const missingAgain = responseFor(page, base, missing);
  await page.getByRole('link', { name: 'Missing', exact: true }).click();
  await missingAgain;
  const missedId = config.missedMonitorBook;
  await selection(page, missedId).waitFor();
  const dropped = deferred();
  hub.drop(event => {
    const wanted = /^(book|wanted\/?(missing|cutoff))$/i.test(event.name) && event.action === 'updated' && event.id === missedId;
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
