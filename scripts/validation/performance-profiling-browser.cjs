const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verify(page, base, config, { check, step, output }) {
  step('open_diagnostics');
  await signIn(page, config, '/system/status');
  const region = page.getByRole('group', { name: 'Performance diagnostics' });
  const requests = region.getByRole('table', { name: 'API requests' });
  const rows = requests.locator('tbody tr');
  await rows.first().waitFor();
  check('shows_timing_groups', await rows.count() > 0 && await requests.getByRole('columnheader', { name: 'Mean ms' }).isVisible());
  const jobs = region.getByRole('table', { name: 'Background jobs' });
  const scheduled = jobs.getByRole('row').filter({ hasText: 'RefreshAuthor' }).filter({ hasText: 'Scheduled' });
  await scheduled.waitFor();
  check('shows_independent_job_category_trigger_and_count', await scheduled.getByText('Author refresh', { exact: true }).isVisible() &&
    await jobs.getByRole('columnheader', { name: 'Executions' }).isVisible() &&
    await jobs.getByRole('row').filter({ hasText: 'ManualImport' }).getByText('Media import', { exact: true }).isVisible());
  step('refresh');
  const response = page.waitForResponse(value => value.ok() && new URL(value.url()).pathname === base.pathname + '/api/v1/system/performance');
  await region.getByRole('button', { name: 'Refresh timings' }).click();
  const request = (await response).request();
  check('refresh_is_authenticated', !!await request.headerValue('X-Api-Key') && await request.headerValue('X-Requested-With') === 'XMLHttpRequest');
  await page.screenshot({ path: path.join(output, `performance-${page.viewportSize().width}.png`), fullPage: true });
}

report(config => runBrowser(config, verify, verify));
