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
  step('loaded_indexer_form');
  await dialog.locator('input[name="name"]').waitFor();
  step('advanced_choices');
  const controls = {
    dangerousFileAction: 'Potentially dangerous files',
    executableFileAction: 'Executable files',
    userRejectedExtensionAction: 'User-rejected file extensions'
  };
  const actions = {
    review: 'Warn and review',
    blocklist: 'Mark failed and blocklist',
    blocklistAndRetry: 'Mark failed, blocklist and search again'
  };
  const getControl = name => dialog.getByRole('button', { name: new RegExp(`^${controls[name]}`) });
  const first = getControl('dangerousFileAction');
  try {
    if (!await first.isVisible()) {
      step('toggle_advanced');
      await dialog.getByTitle('Hidden, click to show').click();
    }
    step('visible_advanced_choices');
    await first.waitFor();
  } catch (error) {
    await page.screenshot({ path: path.join(output, `owned-indexer-navigation-${device}.png`) });
    throw error;
  }
  for (const [name, value] of Object.entries(config.choices)) {
    step(`${name}_selection`);
    const button = getControl(name);
    check(`${name}_has_saved_selection`, await button.isVisible() && (await button.innerText()).trim() === actions[value]);
    // Enhanced selects use a visible portal on desktop and a nested Options
    // dialog on phone. Verify the real options, without changing any settings.
    const before = {};
    for (const label of Object.values(actions)) {
      before[label] = await page.getByText(label, { exact: true }).count();
    }
    await button.click();
    if (device === 'phone') {
      const options = page.getByRole('dialog', { name: 'Options', exact: true });
      await options.waitFor();
      for (const label of Object.values(actions)) {
        await options.getByText(label, { exact: true }).waitFor();
      }
      check(`${name}_has_explicit_choices`, Object.keys(actions).length === await options.getByText(/^(Warn and review|Mark failed and blocklist|Mark failed, blocklist and search again)$/).count());
      await options.getByRole('button', { name: 'Close', exact: true }).click();
      await options.waitFor({ state: 'hidden' });
    } else {
      for (const label of Object.values(actions)) {
        await page.getByText(label, { exact: true }).nth(before[label]).waitFor();
      }
      check(`${name}_has_explicit_choices`, await button.getAttribute('aria-expanded') === 'true');
      await button.press('Escape');
    }
  }
  check('policy_explains_client_data_is_retained', await dialog.getByText('Keeps the download and files in the client', { exact: false }).count() === 3);
  await first.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `owned-indexer-rejection-${device}.png`) });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await dialog.waitFor({ state: 'hidden' });
}

report(config => runBrowser(config, verifyPolicy, verifyPolicy));
