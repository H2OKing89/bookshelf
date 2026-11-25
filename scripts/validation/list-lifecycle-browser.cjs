const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verify(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  const prefix = base.pathname.replace(/\/$/, '');
  const optionPrefix = `${prefix.toLowerCase()}/api/v1/importlist/action/`;
  const options = new Set(['getprofiles', 'gettags', 'getrootfolders']);
  await page.route(url => url.origin === base.origin && url.pathname.toLowerCase().startsWith(optionPrefix) &&
    options.has(url.pathname.toLowerCase().slice(optionPrefix.length)),
  route => route.request().method() === 'POST' ? route.continue() : route.fallback());
  step('list_tag_policy');
  await signIn(page, config, '/settings/importlists');
  const card = page.getByText(config.firstList.name, { exact: true }).locator('../..');
  await card.getByRole('button', { name: '', exact: true }).click();
  const dialog = page.getByRole('dialog').first();
  await dialog.waitFor();
  await dialog.getByText('Tag existing authors', { exact: true }).waitFor();
  const selected = dialog.getByText('Synchronize', { exact: true });
  await selected.waitFor();
  check('saved_sync_tag_policy_is_visible', await selected.isVisible());
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await dialog.waitFor({ state: 'hidden' });
  step('cleanup_preview');
  await page.getByText('Review former list-managed recordings', { exact: true }).click();
  const action = page.getByRole('combobox', { name: /Action/ });
  await action.selectOption('unmonitor');
  const response = page.waitForResponse(result => new URL(result.url()).pathname === `${prefix}/api/v1/importlistcleanup` && result.ok());
  await page.getByRole('button', { name: 'Generate preview', exact: true }).click();
  const preview = await (await response).json();
  const button = page.getByRole('button', { name: 'Confirm unmonitoring', exact: true });
  await button.waitFor();
  check('cleanup_requires_explicit_confirmation_for_exact_candidate', preview.candidates.length === 1 &&
    preview.candidates[0].bookId === 101 && await page.getByText(/audible:us:B011111111/).isVisible() &&
    await page.getByText('1 recording(s) eligible.', { exact: true }).isVisible());
  await button.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `owned-cleanup-${device}.png`), fullPage: false });
  await action.selectOption('preview');
  check('changing_action_discards_previous_confirmation', !await button.isVisible());
}

report(config => runBrowser(config, verify, verify));
