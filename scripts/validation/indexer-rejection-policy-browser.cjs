const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verifyPolicy(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  step('indexer_settings');
  await signIn(page, config, '/settings/indexers');
  const card = page.getByText(config.indexerName, { exact: true }).locator('../../..');
  await card.getByRole('button', { name: '', exact: true }).click();
  const dialog = page.getByRole('dialog').first();
  await dialog.waitFor();
  step('advanced_choices');
  const first = dialog.locator('select[name="dangerousFileAction"]');
  if (!await first.isVisible()) {
    await dialog.getByTitle('Hidden, click to show').click();
  }
  await first.waitFor();
  const actions = ['review', 'blocklist', 'blocklistAndRetry'];
  for (const [name, value] of Object.entries(config.choices)) {
    const select = dialog.locator(`select[name="${name}"]`);
    check(`${name}_has_explicit_choices_and_saved_selection`, await select.isVisible() && await select.inputValue() === value &&
      JSON.stringify(await select.locator('option').evaluateAll(options => options.map(option => option.value))) === JSON.stringify(actions));
  }
  check('policy_explains_client_data_is_retained', await dialog.getByText('Keeps the download and files in the client', { exact: false }).count() === 3);
  await first.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `owned-indexer-rejection-${device}.png`) });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await dialog.waitFor({ state: 'hidden' });
}

report(config => runBrowser(config, verifyPolicy, verifyPolicy));
