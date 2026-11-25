const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verify(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  const card = name => page.locator('div:has(> [class*="underlay"])')
    .filter({ has: page.getByText(name, { exact: true }) });
  const open = async name => {
    await card(name).locator(':scope > [class*="underlay"]').click();
    const dialog = page.getByRole('dialog').first();
    await dialog.waitFor();
    await dialog.locator('input[name="name"]').waitFor();
    return dialog;
  };
  const close = async dialog => {
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    await dialog.waitFor({ state: 'hidden' });
  };
  step('notification_settings');
  await signIn(page, config, '/settings/connect');
  const badge = card(config.recoveryName).getByText('On Health Restored', { exact: true });
  await badge.waitFor();
  check('recovery_only_connection_is_not_disabled', await badge.isVisible() &&
    !await card(config.recoveryName).getByText('Disabled', { exact: true }).count());
  const hook = await open(config.recoveryName);
  const restored = hook.locator('input[name="onHealthRestored"]');
  const issue = hook.locator('input[name="onHealthIssue"]');
  const warnings = hook.locator('input[name="includeHealthWarnings"]');
  await restored.waitFor({ state: 'attached' });
  check('independent_recovery_trigger_and_severity_are_editable', await restored.isEnabled() &&
    await restored.isChecked() && !await issue.isChecked() && await warnings.isEnabled() && await warnings.isChecked());
  await hook.getByText('On Health Restored (when a previously failing health check recovers)', { exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `health-restored-${device}.png`), fullPage: false });
  await close(hook);

  step('plex_capability');
  const plex = await open(config.plexName);
  const unsupported = plex.locator('input[name="onHealthRestored"]');
  check('plex_does_not_offer_unsupported_health_trigger', await unsupported.isDisabled() && !await unsupported.isChecked());
  check('plex_update_library_checkbox_keeps_explicit_false', !await plex.locator('input[name="updateLibrary"]').isChecked());
  await close(plex);

  step('subsonic_capability');
  const subsonic = await open(config.subsonicName);
  const supported = subsonic.locator('input[name="onHealthRestored"]');
  check('subsonic_recovery_capable_but_opt_in_remains_off', await supported.isEnabled() && !await supported.isChecked());
  check('subsonic_scan_and_chat_settings_remain_enabled',
    await subsonic.locator('input[name="updateLibrary"]').isChecked() && await subsonic.locator('input[name="notify"]').isChecked());
  await close(subsonic);
}

report(config => runBrowser(config, verify, verify));
