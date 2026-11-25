const fs = require('fs');
const path = require('path');

const longPageBook = 'Synthetic Independent Recording';
const shortPageBook = 'Synthetic Joint Work';

function deferred() {
  let resolve;
  const promise = new Promise(answer => { resolve = answer; });
  return { promise, resolve };
}

function historyApi(base) {
  const prefix = base.pathname.replace(/\/$/, '');
  return new URL(prefix + '/api/v1/history', base).href;
}

function historyRequest(base, request) {
  const url = new URL(request.url());
  return request.method() === 'GET' && url.origin + url.pathname === historyApi(base) ? url.searchParams : null;
}

function historyResponse(page, base, match = () => true) {
  return page.waitForResponse(response => {
    const args = historyRequest(base, response.request());
    return response.ok() && args !== null && match(args);
  });
}

async function signIn(page, config, route = '/activity/history') {
  await page.goto(config.url + route);
  await page.locator('input[name="username"]').fill(config.username);
  await page.locator('input[name="password"]').fill(config.password);
  await Promise.all([
    page.waitForURL(url => url.pathname.endsWith(route)),
    page.locator('#login-button').click()
  ]);
}

function validateFixture(config) {
  const base = new URL(config.url);
  const output = path.resolve(config.output);
  const directory = fs.lstatSync(output);
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port || base.username || base.password ||
      base.search || base.hash || !/^synthetic-/.test(config.username) ||
      !directory.isDirectory() || directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(output).length) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }
  return { base, output, browsers: require(config.playwrightModule) };
}

function pageButton(page, number, total) {
  return page.getByRole('button', { name: number + ' / ' + total, exact: true });
}

async function selectPage(page, number, total, target) {
  await pageButton(page, number, total).click();
  await page.locator('select[name="pageSelect"]').selectOption(String(target));
}

async function selectFilter(page, value) {
  const button = page.getByRole('button', { name: 'Filter', exact: true });
  await button.click();
  await page.getByRole('menu', { name: 'Filter' }).getByRole('menuitemradio', { name: value, exact: true }).click();
}

async function openHistoryFilters(page) {
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  await page.getByRole('menu', { name: 'Filter' }).getByRole('menuitem', { name: 'Custom Filters', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'History Filters', exact: true });
  await dialog.waitFor();
  return dialog;
}

async function verifyPaging(page, base, config, { check, step, output }) {
  let injectedResponse;
  await page.route(url => url.origin + url.pathname === historyApi(base), async route => {
    if (injectedResponse) {
      const args = historyRequest(base, route.request());
      if (args !== null && injectedResponse.match(args)) {
        const transform = injectedResponse.transform;
        injectedResponse = undefined;
        return transform(route);
      }
    }
    return route.fallback();
  });

  step('sign_in_and_fetch');
  const initial = historyResponse(page, base, args => args.get('page') === '1');
  await signIn(page, config);
  const first = await initial;
  await pageButton(page, 1, 2).waitFor();
  const request = first.request();
  const apiKey = await request.headerValue('X-Api-Key');
  check('history_uses_authenticated_prefixed_native_fetch', request.resourceType() === 'fetch' &&
    await request.headerValue('X-Requested-With') === 'XMLHttpRequest' &&
    await page.evaluate(key => !!key && key === window.Readarr.apiKey, apiKey));

  const table = page.getByRole('table');
  const rows = table.locator('tbody tr');
  const firstRow = rows.first();
  const details = row => row.getByRole('button', { name: 'History Details', exact: true });
  await firstRow.getByRole('link', { name: longPageBook, exact: true }).waitFor();
  check('default_page_has_20_rows_in_descending_date_order', await rows.count() === 20 &&
    (await first.json()).records[0].id === config.firstGrab);

  step('current_details');
  await details(firstRow).click();
  const grabbed = page.getByRole('dialog', { name: 'Grabbed', exact: true });
  await grabbed.waitFor();
  check('confirmed_rows_can_open_details', await grabbed.getByText('Owned history row ' + config.firstGrab, { exact: true }).isVisible() &&
    await grabbed.getByRole('button', { name: 'Mark as Failed', exact: true }).isEnabled());
  await page.keyboard.press('Escape');
  await grabbed.waitFor({ state: 'hidden' });

  step('hold_page_change');
  const slow = deferred();
  const release = deferred();
  const flushed = deferred();
  injectedResponse = {
    match: args => args.get('page') === '2' && args.get('sortDirection') === 'descending',
    async transform(route) {
      const response = await route.fetch({ maxRedirects: 0 });
      const data = await response.json();
      slow.resolve({ request: route.request(), data });
      await release.promise;
      try {
        await route.fulfill({ response, json: data });
      } catch {
        // The browser aborts the held request after the sort changes.
      } finally {
        flushed.resolve();
      }
    }
  };
  const slowRequest = page.waitForRequest(value => {
    const args = historyRequest(base, value);
    return args?.get('page') === '2' && args.get('sortDirection') === 'descending';
  });
  try {
    await selectPage(page, 1, 2, 2);
    const pending = await slowRequest;
    await pageButton(page, 2, 2).waitFor();
    check('pending_page_keeps_previous_rows', await rows.count() === 20 &&
      await firstRow.getByRole('link', { name: longPageBook, exact: true }).isVisible() &&
      await page.getByText('No history.', { exact: true }).count() === 0);
    const staleButtons = details(table);
    check('pending_page_disables_details_actions', await staleButtons.count() === 20 &&
      await staleButtons.evaluateAll(buttons => buttons.every(button => button.disabled || button.getAttribute('aria-disabled') === 'true')));
    await staleButtons.first().evaluate(button => button.click());
    check('stale_details_activation_cannot_open_mark_failed', await page.getByRole('dialog').count() === 0 &&
      await page.getByRole('button', { name: 'Mark as Failed', exact: true }).count() === 0);

    step('sort_during_pending_page');
    const aborted = page.waitForEvent('requestfailed', { predicate: failed => failed === pending });
    const sorted = historyResponse(page, base, args => args.get('sortKey') === 'date' && args.get('sortDirection') === 'ascending');
    await table.getByRole('columnheader', { name: 'Date', exact: true }).click();
    const [cancelled, newer] = await Promise.all([aborted, sorted]);
    check('new_sort_cancels_older_request', cancelled.failure()?.errorText === 'net::ERR_ABORTED');
    const expected = await newer.json();
    await details(firstRow).locator('xpath=self::*[not(@disabled) and not(@aria-disabled="true")]').waitFor();
    const stale = await Promise.race([
      slow.promise,
      page.waitForTimeout(4000).then(() => { throw new Error('HeldHistoryResponseMissing'); })
    ]);
    release.resolve();
    await flushed.promise;
    await details(firstRow).click();
    const current = page.getByRole('dialog').filter({ hasText: 'Owned history row ' + expected.records[0].id });
    await current.waitFor();
    check('late_cancelled_page_cannot_replace_new_sort', stale.request === pending &&
      stale.data.records[0].id !== expected.records[0].id &&
      await current.getByText('Owned history row ' + expected.records[0].id, { exact: true }).isVisible());
    await page.keyboard.press('Escape');
    await current.waitFor({ state: 'hidden' });
  } finally {
    release.resolve();
  }

  step('filter_page_reset_and_empty');
  const [filtered] = await Promise.all([
    historyResponse(page, base, args => args.get('eventType') === '1'),
    selectFilter(page, 'Grabbed')
  ]);
  await pageButton(page, 1, 1).waitFor();
  check('filter_resets_to_first_page_and_keeps_sort', new URL(filtered.url()).searchParams.get('page') === '1' &&
    new URL(filtered.url()).searchParams.get('sortDirection') === 'ascending' &&
    (await filtered.json()).records.length === 2 && await rows.count() === 2);
  await Promise.all([
    historyResponse(page, base, args => args.get('eventType') === '7'),
    selectFilter(page, 'Import Failed')
  ]);
  const empty = page.getByText('No history matches your filters.', { exact: true });
  await empty.waitFor();
  check('confirmed_empty_filter_has_empty_state', await table.count() === 0);
  await selectFilter(page, 'All');
  await pageButton(page, 1, 2).waitFor();
  await selectPage(page, 1, 2, 2);
  await details(firstRow).locator('xpath=self::*[not(@disabled)]').waitFor();

  step('page_size_reset');
  await page.getByRole('button', { name: 'Table Options', exact: true }).first().click();
  const options = page.getByRole('dialog', { name: 'Table Options', exact: true });
  const size = options.getByLabel('Page Size', { exact: true });
  const [resized] = await Promise.all([
    historyResponse(page, base, args => args.get('page') === '1' && args.get('pageSize') === '10'),
    size.fill('10')
  ]);
  await page.keyboard.press('Escape');
  await options.waitFor({ state: 'hidden' });
  await pageButton(page, 1, 3).waitFor();
  check('changing_page_size_resets_to_first', (await resized.json()).records.length === 10);

  step('browser_back_restores_page');
  await selectPage(page, 1, 3, 2);
  await details(firstRow).locator('xpath=self::*[not(@disabled)]').waitFor();
  await firstRow.getByRole('link', { name: longPageBook, exact: true }).click();
  await page.waitForURL(url => url.pathname.endsWith('/book/synthetic-identity-local'));
  await page.goBack();
  await page.waitForURL(url => url.pathname.endsWith('/activity/history'));
  await pageButton(page, 2, 3).waitFor();
  await details(firstRow).locator('xpath=self::*[not(@disabled)]').waitFor();
  check('browser_back_restores_page_and_saved_size', await rows.count() === 10 &&
    await firstRow.getByRole('link', { name: longPageBook, exact: true }).isVisible());

  step('clamp_removed_last_page');
  injectedResponse = {
    match: args => args.get('page') === '3' && args.get('pageSize') === '10',
    async transform(route) {
      const response = await route.fetch({ maxRedirects: 0 });
      await route.fulfill({ response, json: { ...await response.json(), records: [], totalRecords: 11 } });
    }
  };
  const rebased = historyResponse(page, base, args => args.get('page') === '2' && args.get('pageSize') === '10');
  await selectPage(page, 2, 3, 3);
  await rebased;
  await pageButton(page, 2, 3).waitFor();
  await details(firstRow).locator('xpath=self::*[not(@disabled)]').waitFor();
  check('shrunken_last_page_clamps_and_refetches', await rows.count() === 10 && await empty.count() === 0);

  step('saved_preferences');
  await selectFilter(page, 'Grabbed');
  await pageButton(page, 1, 1).waitFor();
  const persisted = historyResponse(page, base, args => args.get('eventType') === '1' &&
    args.get('sortDirection') === 'ascending' && args.get('pageSize') === '10');
  await page.reload();
  await persisted;
  await pageButton(page, 1, 1).waitFor();
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  const selected = page.getByRole('menu', { name: 'Filter' }).getByRole('menuitemradio', { name: 'Grabbed', exact: true });
  check('filter_size_and_sort_survive_document_reload', await selected.getAttribute('aria-checked') === 'true' &&
    await rows.count() === 2);
  await page.keyboard.press('Escape');
  await page.screenshot({ path: path.join(output, 'desktop-history-paging.png'), fullPage: true });
  await verifyHistoryFilters(page, base, { check, step, output });
}

async function verifyHistoryFilters(page, base, { check, step, output }) {
  const hasCombinedFilter = args => args.get('bookId') === '104' && args.getAll('eventType').join(',') === '1,3';
  const table = page.getByRole('table');

  step('apply_combined_filter_from_later_page');
  await selectFilter(page, 'All');
  await pageButton(page, 1, 3).waitFor();
  await Promise.all([
    historyResponse(page, base, args => args.get('page') === '3'),
    selectPage(page, 1, 3, 3)
  ]);
  const details = table.getByRole('button', { name: 'History Details', exact: true }).first();
  await details.locator('xpath=self::*[not(@disabled) and not(@aria-disabled="true")]').waitFor();
  let dialog = await openHistoryFilters(page);
  await dialog.getByRole('combobox', { name: 'Book', exact: true }).selectOption('104');
  await dialog.getByRole('checkbox', { name: 'Grabbed', exact: true }).press('Space');
  await dialog.getByRole('checkbox', { name: 'Book Imported', exact: true }).press('Space');
  await page.screenshot({ path: path.join(output, 'desktop-history-filters.png'), fullPage: true });

  const observedPages = [];
  const capturePage = request => {
    const args = historyRequest(base, request);
    if (args && hasCombinedFilter(args)) observedPages.push(args.get('page'));
  };
  page.on('request', capturePage);
  const [filtered] = await Promise.all([
    historyResponse(page, base, hasCombinedFilter),
    dialog.getByRole('button', { name: 'Apply Filters', exact: true }).click()
  ]);
  await pageButton(page, 1, 2).waitFor();
  page.off('request', capturePage);
  const records = await filtered.json();
  check('combined_book_and_events_use_first_page_of_server_filtered_counts', observedPages.length === 1 && observedPages[0] === '1' &&
    records.totalRecords === 20 && records.records.length === 10 && records.records.every(record => record.bookId === 104));

  step('combined_filter_paging_and_reload');
  const [next] = await Promise.all([
    historyResponse(page, base, args => hasCombinedFilter(args) && args.get('page') === '2'),
    selectPage(page, 1, 2, 2)
  ]);
  const later = await next.json();
  check('later_page_keeps_the_same_predicates_and_exact_count', later.totalRecords === 20 && later.records.length === 10 &&
    later.records.some(record => record.eventType === 'grabbed') && later.records.some(record => record.eventType === 'bookFileImported'));
  const restored = historyResponse(page, base, args => hasCombinedFilter(args) && args.get('page') === '1' &&
    args.get('sortDirection') === 'ascending' && args.get('pageSize') === '10');
  await page.reload();
  await restored;
  await pageButton(page, 1, 2).waitFor();
  await page.getByRole('button', { name: 'Filter', exact: true }).click();
  check('active_saved_filter_sort_and_size_survive_reload', await page.getByRole('menuitemradio', { name: 'Saved History Filter', exact: true })
    .getAttribute('aria-checked') === 'true');
  await page.keyboard.press('Escape');

  step('quick_presets_remain_independent_of_saved_filter');
  const [grabbed] = await Promise.all([
    historyResponse(page, base, args => !args.has('bookId') && args.getAll('eventType').join(',') === '1'),
    selectFilter(page, 'Grabbed')
  ]);
  check('quick_filter_keeps_both_books_and_does_not_erase_saved_filter', (await grabbed.json()).totalRecords === 2);
  await selectFilter(page, 'Saved History Filter');
  await pageButton(page, 1, 2).waitFor();
  dialog = await openHistoryFilters(page);
  await dialog.getByRole('combobox', { name: 'Book', exact: true }).selectOption('105');
  await dialog.getByRole('checkbox', { name: 'Grabbed', exact: true }).press('Space');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  dialog = await openHistoryFilters(page);
  check('cancel_keeps_the_saved_book_and_event_types', await dialog.getByRole('combobox', { name: 'Book', exact: true }).inputValue() === '104' &&
    await dialog.getByRole('checkbox', { name: 'Grabbed', exact: true }).isChecked());
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();

  step('saved_unavailable_book_stays_in_scope');
  await page.evaluate(() => {
    localStorage.setItem('bookshelf:history-filter:' + window.Readarr.apiRoot,
      JSON.stringify({ version: 1, active: true, filters: { bookId: 2147483647, eventTypes: ['1'] } }));
  });
  const unavailable = historyResponse(page, base, args => args.get('bookId') === '2147483647' && args.get('eventType') === '1');
  await page.reload();
  await unavailable;
  const empty = page.getByText('No history matches your filters.', { exact: true });
  await empty.waitFor();
  dialog = await openHistoryFilters(page);
  check('unavailable_saved_book_keeps_its_id_and_shows_filtered_empty',
    await dialog.getByRole('option', { name: 'Unavailable book (ID 2147483647)', exact: true }).count() === 1 &&
    await dialog.getByRole('combobox', { name: 'Book', exact: true }).inputValue() === '2147483647');
  await dialog.getByRole('button', { name: 'Clear', exact: true }).click();
  const [cleared] = await Promise.all([
    historyResponse(page, base, args => !args.has('bookId') && !args.has('eventType')),
    dialog.getByRole('button', { name: 'Apply Filters', exact: true }).click()
  ]);
  check('clearing_custom_filter_restores_all_server_records', (await cleared.json()).totalRecords === 25);

  step('invalid_saved_filter_recovers');
  await page.evaluate(() => {
    localStorage.setItem('bookshelf:history-filter:' + window.Readarr.apiRoot, '{invalid History preference');
    const legacy = JSON.parse(localStorage.getItem('readarr'));
    legacy.history.selectedFilterKey = 'missing-legacy-preset';
    localStorage.setItem('readarr', JSON.stringify(legacy));
  });
  const recovered = historyResponse(page, base, args => !args.has('bookId') && !args.has('eventType'));
  await page.reload();
  const result = await recovered;
  await pageButton(page, 1, 3).waitFor();
  check('malformed_saved_filter_and_missing_preset_recover_to_all', (await result.json()).totalRecords === 25);
}

async function verifyPhone(page, base, config, { check, step, output }) {
  step('sign_in_and_first_page');
  const initial = historyResponse(page, base);
  await signIn(page, config);
  await initial;
  await pageButton(page, 1, 2).waitFor();
  step('next_page');
  const [response] = await Promise.all([
    historyResponse(page, base, args => args.get('page') === '2'),
    selectPage(page, 1, 2, 2)
  ]);
  await page.getByRole('table').getByRole('link', { name: shortPageBook, exact: true }).first().waitFor();
  check('phone_can_change_page', (await response.json()).records.length === 5 && await pageButton(page, 2, 2).isVisible());
  await page.screenshot({ path: path.join(output, 'phone-history-paging.png'), fullPage: true });

  step('phone_combined_history_filter');
  const filters = await openHistoryFilters(page);
  await filters.getByRole('combobox', { name: 'Book', exact: true }).selectOption('105');
  await filters.getByRole('checkbox', { name: 'Grabbed', exact: true }).press('Space');
  const [filtered] = await Promise.all([
    historyResponse(page, base, args => args.get('bookId') === '105' && args.get('eventType') === '1'),
    filters.getByRole('button', { name: 'Apply Filters', exact: true }).click()
  ]);
  await pageButton(page, 1, 1).waitFor();
  const pageResult = await filtered.json();
  check('phone_applies_book_and_event_filters_through_the_api', pageResult.page === 1 &&
    pageResult.totalRecords === 1 && pageResult.records[0].id === config.secondGrab);
}

async function runBrowser(config, verifyDesktop, verifyMobile) {
  const { base, output, browsers } = validateFixture(config);
  const browser = await browsers.chromium.launch({ headless: true });
  const checks = {};
  let failureCheck;
  let failureStep;
  let failureType;
  let completed = false;
  try {
    const layouts = [['desktop', { viewport: { width: 1280, height: 960 } }, verifyDesktop]];
    if (verifyMobile) {
      const { defaultBrowserType, ...options } = browsers.devices['iPhone 13'];
      layouts.push(['phone', options, verifyMobile]);
    }
    for (const [layout, options, verify] of layouts) {
      const context = await browser.newContext(options);
      try {
        let externalRequests = 0;
        let writes = 0;
        let pageErrors = 0;
        await context.route('**/*', route => {
          const request = route.request();
          const url = new URL(request.url());
          if (url.origin !== base.origin && !['data:', 'blob:'].includes(url.protocol)) {
            externalRequests++;
            return route.abort();
          }
          if (/\/api\/v[12]\//.test(url.pathname) && !['GET', 'HEAD'].includes(request.method())) {
            writes++;
            return route.abort();
          }
          return route.continue();
        });
        const page = await context.newPage();
        page.setDefaultTimeout(9000);
        page.on('pageerror', () => { pageErrors++; });
        const check = (name, condition) => {
          const label = layout + '_' + name;
          checks[label] = Boolean(condition);
          if (!checks[label]) {
            failureCheck = label;
            throw new Error('CheckFailed');
          }
        };
        const step = name => { failureStep = layout + '_' + name; };
        await verify(page, base, config, { check, step, output });
        check('browser_uses_owned_read_requests', externalRequests === 0 && writes === 0);
        check('no_page_errors', pageErrors === 0);
      } finally {
        await context.close();
      }
    }
    completed = true;
  } catch (error) {
    failureType = error.name;
  } finally {
    await browser.close();
  }
  return { checks, completed, failureCheck, failureStep: completed ? undefined : failureStep, failureType };
}

async function report(run) {
  try {
    const config = JSON.parse(fs.readFileSync(process.stdin.fd, 'utf8'));
    const result = await run(config);
    process.stdout.write(JSON.stringify(result));
    process.exitCode = result.completed ? 0 : 1;
  } catch (error) {
    process.stdout.write(JSON.stringify({ checks: { browser_started: false }, completed: false, failureType: error.name }));
    process.exitCode = 1;
  }
}

if (require.main === module) {
  report(config => runBrowser(config, verifyPaging, verifyPhone));
}

module.exports = { deferred, historyApi, historyRequest, historyResponse, pageButton, report, runBrowser, selectFilter, selectPage, signIn };
