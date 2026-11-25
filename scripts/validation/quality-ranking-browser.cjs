const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verifyRank(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  step('history_quality_sort');
  await signIn(page, config, '/activity/history');
  const table = page.getByRole('table');
  const quality = table.getByRole('columnheader', { name: 'Quality', exact: true });
  await quality.waitFor();
  const sorted = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.origin === base.origin && url.pathname === `${base.pathname}/api/v1/history` && response.ok() && url.searchParams.get('sortKey') === 'quality';
  });
  await quality.click();
  const response = await sorted;
  const direction = new URL(response.url()).searchParams.get('sortDirection');
  const ids = (await response.json()).records.map(row => row.id);
  const expected = direction === 'ascending' ? [901, 903, 902, 904] : [904, 902, 901, 903];
  check('quality_header_requests_profile_order', JSON.stringify(ids) === JSON.stringify(expected));
  await table.locator('tbody tr').first().getByText(direction === 'ascending' ? 'MP3' : 'Unknown Audio', { exact: true }).waitFor();
  await page.screenshot({ path: path.join(output, `owned-history-rank-${device}.png`) });

  step('file_quality_sort');
  await page.goto(`${config.url}/book/synthetic-identity-us`);
  await page.getByRole('tab', { name: /^Files/ }).click();
  const files = page.getByRole('table');
  await files.getByText('FLAC', { exact: true }).first().waitFor();
  const fileQuality = files.getByRole('columnheader', { name: 'Quality', exact: true });
  await fileQuality.click();
  // A second click reverses ordered files, independent of extension or title.
  const firstOrder = await files.locator('tbody tr').allTextContents();
  await fileQuality.click();
  const secondOrder = await files.locator('tbody tr').allTextContents();
  check('file_sort_reverses_profile_rank_not_format_weight', firstOrder.length === 2 && secondOrder.length === 2 &&
    firstOrder[0] === secondOrder[1] && firstOrder[1] === secondOrder[0]);
  await page.screenshot({ path: path.join(output, `owned-file-rank-${device}.png`) });

  step('library_rank_sort');
  await page.goto(`${config.url}/books`);
  const sort = page.getByRole('button', { name: 'Sort', exact: true });
  await sort.click();
  const rank = page.getByRole('menuitemradio', { name: 'Lowest File Quality Rank', exact: true });
  await rank.click();
  await sort.click();
  check('library_offers_lowest_file_quality_rank', await rank.getAttribute('aria-checked') === 'true');
  await page.keyboard.press('Escape');
  await page.screenshot({ path: path.join(output, `owned-library-rank-${device}.png`) });
}

report(config => runBrowser(config, verifyRank, verifyRank));
