const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verify(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  const prefix = base.pathname.replace(/\/$/, '');
  const openCard = async name => {
    const card = page.getByText(name, { exact: true }).locator('../..');
    await card.getByRole('button', { name: '', exact: true }).click();
    const dialog = page.getByRole('dialog').first();
    await dialog.waitFor();
    return dialog;
  };

  step('custom_import_list');
  await signIn(page, config, '/settings/importlists');
  const list = await openCard(config.listName);
  const endpointField = list.locator('input[name="baseUrl"]');
  const tokenField = list.locator('input[name="apiKey"]');
  await endpointField.waitFor();
  const endpoint = new URL(await endpointField.inputValue());
  check('custom_endpoint_and_masked_token_inputs_are_visible', endpoint.hostname === '127.0.0.1' && endpoint.pathname === '/owned-custom-list' &&
    await tokenField.getAttribute('type') === 'password');
  const received = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.origin === base.origin && url.pathname === `${prefix}/api/v1/importlistmembership/${config.listId}` && response.ok();
  });
  await list.getByText('List membership', { exact: true }).click();
  const snapshot = await (await received).json();
  await list.getByText('Source book: audible:us:B011111111', { exact: true }).waitFor();
  const excluded = list.getByText('Source book: audible:uk:B011111111', { exact: true });
  await excluded.waitFor();
  check('custom_list_shows_exact_regional_and_excluded_members', snapshot.items.length === 3 &&
    await excluded.locator('..').getByText('Excluded', { exact: true }).isVisible());
  await excluded.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `custom-list-membership-${device}.png`), fullPage: false });
  await list.getByRole('button', { name: 'Cancel', exact: true }).click();
  await list.waitFor({ state: 'hidden' });

  step('qbit_download_client');
  await page.goto(`${config.url}/settings/downloadclients`);
  const client = await openCard(config.clientName);
  await client.getByText('Seed Time Type', { exact: true }).waitFor();
  const inactive = client.getByText('Inactive', { exact: true });
  await inactive.waitFor();
  check('qbit_saved_inactive_mode_and_version_help_are_visible', await inactive.isVisible() &&
    await client.getByText(/Inactive seed time requires qBittorrent 4\.6\.0 or later/).isVisible());
  await inactive.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `qbit-seed-mode-${device}.png`), fullPage: false });
  await client.getByRole('button', { name: 'Cancel', exact: true }).click();
  await client.waitFor({ state: 'hidden' });
}

report(config => runBrowser(config, verify, verify));
