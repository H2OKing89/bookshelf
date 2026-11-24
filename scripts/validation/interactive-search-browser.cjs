const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');
const { api, isAuthenticatedFetch, requestArgs } = require('./operational-paging-browser.cjs');

const count = 280;

function token(scope, generation, index) {
  return (BigInt(scope) * 1000000n + BigInt(generation) * 1000n + BigInt(index)).toString(16).padStart(32, '0');
}

function title(scope, index) {
  return `Owned Search ${scope} result ${String(index).padStart(3, '0')}`;
}

function release(scope, generation, index) {
  const rejected = index === 1;
  return {
    guid: index < 2 ? 'owned-shared-guid' : `owned-release-${index}`,
    indexerId: index === 1 ? 22 : 21,
    searchResultId: token(scope, generation, index),
    title: title(scope, index),
    indexer: 'Owned Synthetic Indexer',
    protocol: 'torrent',
    age: 2, ageHours: 48, ageMinutes: 2880,
    publishDate: '2026-01-01T00:00:00Z',
    size: 12345678, seeders: 10, leechers: 2,
    infoUrl: 'https://synthetic.invalid/release',
    quality: { quality: { id: 12, name: 'M4B' }, revision: { version: 1 } },
    qualityWeight: 12,
    mediaInfo: { audioCodec: 'AAC LC', audioChannels: 2, audioBitRate: '128 kbps', language: 'eng', durationSeconds: 3600, source: 'MyAnonaMouse' },
    customFormats: [{ id: 1, name: 'Synthetic English' }],
    customFormatScore: 25,
    releaseWeight: index,
    indexerFlags: 0,
    discography: false,
    downloadAllowed: !rejected,
    rejections: rejected ? ['Synthetic rejected release', 'Synthetic quality policy reason'] : []
  };
}

async function setup(page, base, config) {
  const searches = [];
  const grabs = [];
  const generations = new Map();
  let expires = false;
  await page.route(url => url.origin + url.pathname === api(base, 'release'), async route => {
    const request = route.request();
    if (request.method() === 'POST') {
      const body = request.postDataJSON();
      grabs.push(body);
      if (expires) {
        expires = false;
        return route.fulfill({ status: 404, json: { message: 'Owned search result expired. Search again.' } });
      }
      return route.fulfill({ status: 200, json: body });
    }
    const args = requestArgs(base, request, 'release');
    if (!args) return route.fallback();
    const scope = Number(args.get('bookId') || args.get('authorId'));
    const generation = (generations.get(scope) || 0) + 1;
    generations.set(scope, generation);
    searches.push({ scope, generation, request });
    const size = scope === config.first.id ? count : scope === config.second.id ? 3 : 0;
    return route.fulfill({ json: Array.from({ length: size }, (_unused, index) => release(scope, generation, index)) });
  });
  const waitSearch = scope => page.waitForResponse(response => {
    const args = requestArgs(base, response.request(), 'release');
    return args !== null && Number(args.get('bookId') || args.get('authorId')) === scope && response.ok();
  });
  const searchTab = page.getByRole('tab', { name: 'Search', exact: true });
  const panel = page.getByRole('region', { name: 'Interactive Search', exact: true });
  const filter = page.getByRole('searchbox', { name: 'Filter releases by text' });
  const region = page.getByRole('region', { name: 'Search results', exact: true });
  const rows = page.locator('[data-release-row]');
  const row = (scope, index) => rows.filter({ hasText: title(scope, index) });
  const showing = (shown, total) => page.getByText(`Showing ${shown} of ${total} results`, { exact: true });
  const selectSearch = async scope => {
    const request = waitSearch(scope);
    await searchTab.click();
    return request;
  };
  return { searches, grabs, waitSearch, searchTab, panel, filter, region, rows, row, showing, selectSearch, expire: () => { expires = true; } };
}

async function scrollToEnd(region, row) {
  await region.evaluate(element => { element.scrollTop = element.scrollHeight; });
  await row.waitFor();
}

async function verifyDesktop(page, base, config, { check, step, output }) {
  await page.setViewportSize({ width: 1600, height: 960 });
  const view = await setup(page, base, config);
  const id = config.first.id;
  step('initial_search');
  await signIn(page, config, `/book/${config.first.slug}`);
  const initial = await view.selectSearch(id);
  await view.showing(count, count).waitFor();
  await view.row(id, 0).waitFor();
  check('wide_search_has_authenticated_results_and_bounded_rows', await isAuthenticatedFetch(page, initial.request()) &&
    await view.region.getByRole('table').count() === 1 && await view.rows.count() < 80);

  step('local_filter_and_sort');
  await view.filter.fill('  RESULT 001  ');
  await view.showing(1, count).waitFor();
  check('title_filter_is_immediate_and_local', await view.row(id, 1).isVisible() && view.searches.length === 1);
  await view.filter.fill('no owned result matches');
  await view.showing(0, count).waitFor();
  check('filtered_empty_is_distinct_from_no_results', await page.getByText('All results are hidden by the applied filter', { exact: true }).isVisible());
  await view.filter.fill('');
  await view.showing(count, count).waitFor();
  const filterMenu = view.panel.getByRole('button', { name: 'Filter', exact: true });
  await filterMenu.click();
  await page.getByRole('menuitemradio', { name: 'Discography', exact: true }).click();
  await view.showing(0, count).waitFor();
  await filterMenu.click();
  await page.getByRole('menuitemradio', { name: 'All', exact: true }).click();
  await view.showing(count, count).waitFor();
  check('saved_filter_presets_still_filter_existing_results', view.searches.length === 1);
  await filterMenu.click();
  await page.getByRole('menuitem', { name: 'Custom Filters', exact: true }).click();
  const custom = page.getByRole('dialog', { name: 'Custom Filter', exact: true });
  await custom.waitFor();
  await page.keyboard.press('Escape');
  await custom.waitFor({ state: 'hidden' });
  check('custom_filter_editor_remains_available_without_a_new_search', view.searches.length === 1);
  await view.region.getByRole('columnheader', { name: 'Title', exact: true }).click();
  check('sorting_does_not_search_again', view.searches.length === 1);
  // Restore the deterministic ascending title order for the scroll checks.
  if (!await view.row(id, 0).count()) await view.region.getByRole('columnheader', { name: 'Title', exact: true }).click();

  step('windowed_desktop_scroll');
  await scrollToEnd(view.region, view.row(id, count - 1));
  check('large_desktop_scroll_keeps_dom_bounded', await view.rows.count() < 80 && await view.row(id, count - 1).isVisible());
  await page.screenshot({ path: path.join(output, 'owned-search-table.png'), fullPage: false });

  step('confirmed_grab_keeps_exact_release_identity');
  await view.filter.fill('result 001');
  const rejected = view.row(id, 1);
  await rejected.waitFor();
  await rejected.getByRole('button', { name: 'Grab Release' }).click();
  const confirm = page.getByRole('dialog', { name: 'Grab Release', exact: true });
  await confirm.waitFor();
  const posted = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/release'));
  await confirm.getByRole('button', { name: 'Grab', exact: true }).click();
  await posted;
  check('forced_grab_sends_only_the_owned_selected_token_and_context', view.grabs.length === 1 &&
    view.grabs[0].bookId === id && view.grabs[0].indexerId === 22 && view.grabs[0].searchResultId === token(id, 1, 1));

  step('expired_release_requires_explicit_refresh');
  await view.filter.fill('result 000');
  const normal = view.row(id, 0);
  const refresh = view.panel.getByRole('button', { name: 'Refresh', exact: true });
  view.expire();
  await normal.getByRole('button', { name: 'Grab Release' }).click();
  await page.getByText('Refresh search results before grabbing a release.', { exact: true }).waitFor();
  check('expired_result_disables_grabs_without_retry', await normal.getByRole('button', { name: 'Grab Release' }).isDisabled() && view.grabs.length === 2);
  const renewed = view.waitSearch(id);
  await refresh.click();
  await renewed;
  await normal.getByRole('button', { name: 'Grab Release' }).locator('xpath=self::*[not(@disabled)]').waitFor();
  check('manual_refresh_replaces_the_old_token', (await normal.getAttribute('data-release-row')).includes(token(id, 2, 0)));

  step('fresh_reopen_and_new_search_contexts');
  await page.getByRole('tab', { name: 'History', exact: true }).click();
  await view.searchTab.click();
  await view.showing(count, count).waitFor();
  check('reopening_fresh_search_does_not_contact_indexers', view.searches.length === 2);
  const author = page.getByRole('link', { name: config.author.name, exact: true }).first();
  await author.click();
  await page.waitForURL(url => url.pathname.endsWith(`/author/${config.author.slug}`));
  await view.selectSearch(config.author.id);
  await page.getByText('No results found', { exact: true }).waitFor();
  check('author_search_does_not_reuse_book_releases', view.searches.at(-1).scope === config.author.id && await view.rows.count() === 0);
  await page.getByRole('tab', { name: /^Books/ }).click();
  await page.getByRole('link', { name: config.second.title, exact: true }).first().click();
  await page.waitForURL(url => url.pathname.endsWith(`/book/${config.second.slug}`));
  await view.selectSearch(config.second.id);
  await view.showing(3, 3).waitFor();
  const other = view.row(config.second.id, 0);
  await other.waitFor();
  check('different_recordings_get_their_own_release_identity', (await other.getAttribute('data-release-row')).includes(token(config.second.id, 1, 0)) &&
    await view.row(id, 0).count() === 0);
}

async function verifyPhone(page, base, config, { check, step, output }) {
  const view = await setup(page, base, config);
  const id = config.first.id;
  step('phone_release_cards');
  await signIn(page, config, `/book/${config.first.slug}`);
  await view.selectSearch(id);
  await view.showing(count, count).waitFor();
  await view.row(id, 0).waitFor();
  check('phone_results_use_bounded_cards', await view.region.getByRole('listitem').count() > 0 && await view.rows.count() < 80);
  await view.filter.fill('result 001');
  const rejected = view.row(id, 1);
  await rejected.waitFor();
  check('card_exposes_audio_format_score_and_rejections', await rejected.getByText('Synthetic rejected release', { exact: true }).isVisible() &&
    await rejected.getByText('Synthetic English', { exact: true }).isVisible() && await rejected.getByText(/AAC LC/).first().isVisible() &&
    await rejected.getByText('eng', { exact: true }).first().isVisible() && await rejected.getByRole('button', { name: 'Grab Release' }).isVisible());
  await page.screenshot({ path: path.join(output, 'owned-search-card.png'), fullPage: false });
  await view.filter.fill('');
  await view.showing(count, count).waitFor();
  await scrollToEnd(view.region, view.row(id, count - 1));
  check('phone_scrolling_keeps_the_late_release_reachable', await view.rows.count() < 80 && view.searches.length === 1);
}

report(config => runBrowser(config, verifyDesktop, verifyPhone));
