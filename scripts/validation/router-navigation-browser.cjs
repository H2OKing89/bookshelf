const path = require('path');
const { report, runBrowser } = require('./history-paging-browser.cjs');

async function verifyTabs(page, check, name) {
  const tabs = page.getByRole('tablist').getByRole('tab');
  const first = tabs.first();
  const last = tabs.last();
  await first.waitFor();
  await first.focus();
  await first.press('ArrowLeft');
  await page.waitForFunction(node => node === document.activeElement && node.getAttribute('aria-selected') === 'true',
    await last.elementHandle());
  const panel = page.locator(`[id="${await last.getAttribute('aria-controls')}"]`);
  await panel.waitFor();
  check(name + '_arrow_left_wraps_focus_and_selection', await first.getAttribute('aria-selected') === 'false' &&
    await panel.getAttribute('aria-labelledby') === await last.getAttribute('id'));
  await last.press('ArrowRight');
  await page.waitForFunction(node => node === document.activeElement && node.getAttribute('aria-selected') === 'true',
    await first.elementHandle());
  const firstPanel = page.locator(`[id="${await first.getAttribute('aria-controls')}"]`);
  await firstPanel.waitFor();
  check(name + '_arrow_right_restores_first_panel', await last.getAttribute('aria-selected') === 'false' &&
    await firstPanel.getAttribute('aria-labelledby') === await first.getAttribute('id'));
}

async function verifyNavigation(page, base, config, { check, step, output }) {
  const prefix = base.pathname.replace(/\/$/, '');
  const bookPath = `${prefix}/book/${config.recording.slug}`;
  const authorPath = `${prefix}/author/${config.author.slug}`;
  const protectedBook = new URL(`${bookPath}?fixture=bookmark`, base);
  const prefixedReads = new Set();
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.origin === base.origin && url.pathname.includes('/api/v1/')) prefixedReads.add(url.pathname);
  });

  step('login_returns_to_requested_book');
  await page.goto(protectedBook.href);
  await page.waitForURL(url => url.pathname === `${prefix}/login`);
  await page.locator('input[name="username"]').fill(config.username);
  await page.locator('input[name="password"]').fill(config.password);
  await Promise.all([page.waitForURL(protectedBook.href), page.locator('#login-button').click()]);
  const recordingTitle = page.getByText(config.recording.title, { exact: true }).first();
  await recordingTitle.waitFor();
  const instanceName = await page.evaluate(() => window.Readarr.instanceName);
  const expectedBookTitle = `${config.recording.title} - ${instanceName}`;
  const expectedAuthorTitle = `${config.author.name} - ${instanceName}`;
  const expectTitle = async title => {
    await page.waitForFunction(expected => document.title === expected, title);
    return await page.title() === title;
  };
  check('recording_document_title', await expectTitle(expectedBookTitle));
  await verifyTabs(page, check, 'book_tabs');
  check('authentication_returns_to_the_prefixed_book_and_query', page.url() === protectedBook.href &&
    await page.evaluate(value => window.Readarr.urlBase === value, prefix));

  step('bookmark_and_single_prefix_link');
  const bookmark = new URL('#fixture-recording', protectedBook);
  await page.goto(bookmark.href);
  await page.reload();
  await recordingTitle.waitFor();
  const author = page.getByRole('link', { name: config.author.name, exact: true }).first();
  await author.waitFor();
  check('direct_recording_bookmark_preserves_query_and_fragment_on_reload', page.url() === bookmark.href);
  check('recording_to_author_link_has_one_base', await author.getAttribute('href') === authorPath);

  step('browser_back_and_forward');
  await author.click();
  await page.waitForURL(new URL(authorPath, base).href);
  await page.getByText(config.author.name, { exact: true }).first().waitFor();
  check('author_document_title_after_client_navigation', await expectTitle(expectedAuthorTitle));
  await verifyTabs(page, check, 'author_tabs');
  await page.goBack();
  await recordingTitle.waitFor();
  check('back_returns_to_the_exact_recording_bookmark', page.url() === bookmark.href);
  check('back_restores_recording_document_title', await expectTitle(expectedBookTitle));
  await page.goForward();
  await page.waitForURL(new URL(authorPath, base).href);
  check('forward_restores_the_same_author_route', page.url() === new URL(authorPath, base).href);
  check('forward_restores_author_document_title', await expectTitle(expectedAuthorTitle));

  step('calendar_and_not_found');
  const calendar = new URL(`${prefix}/calendar?fixture=calendar#owned`, base);
  await page.goto(calendar.href);
  await page.getByRole('link', { name: 'Calendar', exact: true }).waitFor();
  check('calendar_bookmark_preserves_query_and_fragment', page.url() === calendar.href);
  check('calendar_document_title', await expectTitle(`Calendar - ${instanceName}`));
  const unknown = new URL(`${prefix}/owned-route-not-found`, base);
  await page.goto(unknown.href);
  await page.getByText('You must be lost, nothing to see here.', { exact: true }).waitFor();
  check('unknown_route_renders_the_application_not_found_page', page.url() === unknown.href);
  check('not_found_does_not_keep_previous_route_title', await expectTitle(`MIA - ${instanceName}`));
  check('router_leaves_api_requests_on_the_deployment_base', prefixedReads.size > 0 &&
    [...prefixedReads].every(request => request.startsWith(`${prefix}/api/v1/`)));
  await page.screenshot({ path: path.join(output, 'owned-router-not-found.png'), fullPage: true });
}

report(config => runBrowser(config, verifyNavigation));
