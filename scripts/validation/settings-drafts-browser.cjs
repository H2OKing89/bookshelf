const path = require('path');
const { deferred, report, runBrowser, signIn } = require('./history-paging-browser.cjs');
const { api, isAuthenticatedFetch, requestArgs, responseFor, setCheckbox } = require('./operational-paging-browser.cjs');

const settingsPath = '/settings/downloadclients';
const configResource = 'config/downloadclient';
const saveResource = configResource + '/1';

function options(page) {
  return page.locator('input[name="enableCompletedDownloadHandling"]');
}

async function showOptions(page) {
  const button = page.locator('button[title="Hidden, click to show"], button[title="Shown, click to hide"]').first();
  await button.waitFor();
  if (await button.getAttribute('title') === 'Hidden, click to show') await button.click();
  await options(page).waitFor();
}

function editClient(page, name) {
  return page.locator('div:has(> button[class*="underlay"])')
    .filter({ has: page.getByText(name, { exact: true }) })
    .locator(':scope > button[class*="underlay"]');
}

function saveResult(page, base) {
  return page.waitForResponse(response => requestArgs(base, response.request(), saveResource, 'PUT') !== null);
}

async function verifyDraft(page, base, config, { check, step }) {
  let failNext = true;
  let writes = 0;
  const submit = deferred();
  const sent = deferred();
  await page.route(api(base, saveResource), async route => {
    if (route.request().method() !== 'PUT') return route.fallback();
    writes++;
    if (!failNext) {
      sent.resolve();
      await submit.promise;
      return route.continue();
    }
    failNext = false;
    return route.fulfill({ status: 400, json: [{ propertyName: 'enableCompletedDownloadHandling',
      errorMessage: 'Synthetic settings validation failure' }] });
  });

  step('options_load');
  const fetched = responseFor(page, base, configResource);
  await signIn(page, config, settingsPath);
  const first = await fetched;
  await showOptions(page);
  const checkbox = options(page);
  const original = await first.json();
  check('settings_read_uses_authenticated_prefixed_fetch', await isAuthenticatedFetch(page, first.request()));
  check('settings_loads_server_default', original.enableCompletedDownloadHandling === true && await checkbox.isChecked());

  const clean = page.getByRole('button', { name: 'No Changes', exact: true });
  const save = page.getByRole('button', { name: 'Save Changes', exact: true });
  step('revert_draft');
  await setCheckbox(checkbox, false);
  await save.waitFor();
  await setCheckbox(checkbox, true);
  await clean.waitFor();
  check('reverting_to_original_clears_dirty_state', await clean.isDisabled() && writes === 0);

  step('reject_draft');
  await setCheckbox(checkbox, false);
  const rejected = saveResult(page, base);
  await save.click();
  const failure = await rejected;
  await page.getByRole('alert').filter({ hasText: 'Save failed.' }).waitFor();
  check('failed_save_retains_the_draft_and_allows_retry', failure.status() === 400 && !await checkbox.isChecked() &&
    await save.isEnabled() && writes === 1);

  step('save_draft');
  const accepted = saveResult(page, base);
  await save.click();
  await sent.promise;
  try {
    await checkbox.locator('xpath=self::*[@disabled]').waitFor();
    await page.keyboard.press('Control+s');
    check('outstanding_save_disables_duplicate_submissions', await save.isDisabled() && writes === 2);
  } finally {
    submit.resolve();
  }
  const success = await accepted;
  const canonical = await success.json();
  await clean.waitFor();
  check('retry_sends_explicit_false_and_adopts_the_canonical_response', success.status() === 202 &&
    await isAuthenticatedFetch(page, success.request()) && writes === 2 &&
    success.request().postDataJSON().enableCompletedDownloadHandling === false &&
    canonical.enableCompletedDownloadHandling === false && canonical.downloadClientWorkingFolders === original.downloadClientWorkingFolders &&
    !await checkbox.isChecked() && await clean.isDisabled());

  step('clean_reload');
  let unexpectedPrompt = false;
  const dismissUnexpected = async dialog => { unexpectedPrompt = true; await dialog.accept(); };
  page.on('dialog', dismissUnexpected);
  const refetched = responseFor(page, base, configResource);
  await page.reload();
  await refetched;
  await showOptions(page);
  page.off('dialog', dismissUnexpected);
  check('successful_save_survives_reload_without_an_unsaved_warning', !unexpectedPrompt &&
    !await checkbox.isChecked() && await clean.isDisabled());
}

async function verifyPageGuard(page, { check, step, output }) {
  const indexers = page.getByRole('link', { name: 'Indexers', exact: true });
  const clients = page.getByRole('link', { name: 'Download Clients', exact: true });
  const indexerToolbar = page.getByRole('button', { name: 'Manage Indexers', exact: true });
  const dialog = page.getByRole('dialog', { name: 'Unsaved Changes', exact: true });
  const stay = dialog.getByRole('button', { name: 'Stay and review changes', exact: true });
  const leave = dialog.getByRole('button', { name: 'Discard changes and leave', exact: true });
  const checkbox = options(page);
  const current = new URL(page.url());
  const destination = new URL(await indexers.getAttribute('href'), current);
  destination.search = '?fixture=one';
  destination.hash = '#preserve-this';

  step('establish_forward_history');
  await indexers.click();
  await page.waitForURL(url => url.pathname === destination.pathname);
  await page.evaluate(url => history.replaceState(history.state, '', url), destination.href);
  await page.evaluate(() => history.back());
  await page.waitForURL(current.href);
  await showOptions(page);

  step('cancel_link');
  await setCheckbox(checkbox, true);
  await indexers.click();
  await dialog.waitFor();
  await stay.click();
  await dialog.waitFor({ state: 'hidden' });
  check('cancelled_link_keeps_draft_and_position', page.url() === current.href && await checkbox.isChecked());

  step('cancel_dirty_reload');
  const prompt = page.waitForEvent('dialog');
  const reload = page.reload().catch(() => null);
  const unload = await prompt;
  await unload.dismiss();
  await reload;
  check('dismissed_reload_keeps_the_draft', unload.type() === 'beforeunload' &&
    page.url() === current.href && await checkbox.isChecked());

  step('cancel_and_confirm_forward');
  await page.evaluate(() => history.forward());
  await dialog.waitFor();
  await stay.click();
  await page.waitForURL(current.href);
  await dialog.waitFor({ state: 'hidden' });
  check('cancelled_forward_keeps_draft_and_position', page.url() === current.href && await checkbox.isChecked());
  await page.evaluate(() => history.forward());
  await dialog.waitFor();
  await page.screenshot({ path: path.join(output, 'owned-unsaved-navigation.png'), fullPage: true });
  await leave.click();
  await page.waitForURL(destination.href);
  await indexerToolbar.waitFor();
  check('confirmed_forward_visits_exact_history_entry', page.url() === destination.href);

  step('confirm_back');
  await clients.click();
  await page.waitForURL(current.href);
  await showOptions(page);
  check('discarded_draft_does_not_reappear', !await checkbox.isChecked());
  await setCheckbox(checkbox, true);
  await page.evaluate(() => history.back());
  await dialog.waitFor();
  await leave.click();
  await page.waitForURL(destination.href);
  await indexerToolbar.waitFor();
  check('confirmed_back_visits_exact_history_entry', page.url() === destination.href);

  step('cancel_back_after_reloading_the_current_entry');
  await clients.click();
  await page.waitForURL(current.href);
  await page.reload();
  await showOptions(page);
  await setCheckbox(checkbox, true);
  await page.evaluate(() => history.back());
  await dialog.waitFor();
  await stay.click();
  await page.waitForURL(current.href);
  await dialog.waitFor({ state: 'hidden' });
  check('cancelled_back_after_reload_restores_the_exact_edited_entry', page.url() === current.href && await checkbox.isChecked());

  await page.evaluate(() => history.back());
  await dialog.waitFor();
  await leave.click();
  await page.waitForURL(destination.href);
  await indexerToolbar.waitFor();
  check('confirmed_back_after_reload_opens_the_previous_entry', page.url() === destination.href);

  step('confirm_same_route_with_different_query_and_hash');
  const earlierSettings = new URL('?fixture=earlier#options', current);
  await clients.click();
  await page.waitForURL(current.href);
  await page.evaluate(url => history.replaceState(history.state, '', url), earlierSettings.href);
  await indexers.click();
  await indexerToolbar.waitFor();
  await clients.click();
  await page.waitForURL(current.href);
  await showOptions(page);
  await setCheckbox(checkbox, true);
  await page.evaluate(() => history.go(-2));
  await dialog.waitFor();
  await leave.click();
  await page.waitForURL(earlierSettings.href);
  const clean = page.getByRole('button', { name: 'No Changes', exact: true });
  await clean.waitFor();
  check('confirming_same_route_discards_the_draft_and_keeps_the_destination',
    !await checkbox.isChecked() && await clean.isDisabled());
}

async function verifyDialogGuard(page, config, { check, step, output }) {
  step('open_child_with_an_unsaved_page_draft');
  await page.getByRole('link', { name: 'Download Clients', exact: true }).click();
  await showOptions(page);
  await setCheckbox(options(page), true);
  const open = editClient(page, config.downloadClientName);
  const editor = page.getByRole('dialog', { name: /^Edit Download Client/ });
  const name = editor.locator('input[name="name"]');
  const guard = page.getByRole('dialog', { name: 'Unsaved Changes', exact: true });
  const keep = guard.getByRole('button', { name: 'Keep editing', exact: true });
  const discard = guard.getByRole('button', { name: 'Discard changes and close', exact: true });
  await open.click();
  await name.fill('Synthetic unsaved client name');

  for (const [label, close] of [
    ['escape', () => page.keyboard.press('Escape')],
    ['backdrop', () => page.mouse.click(12, 12)],
    ['close_button', () => editor.getByRole('button', { name: 'Close', exact: true }).click()],
    ['cancel_button', () => editor.getByRole('button', { name: 'Cancel', exact: true }).click()]
  ]) {
    step('child_close_' + label);
    await close();
    await guard.waitFor();
    await keep.click();
    await guard.waitFor({ state: 'hidden' });
    check(label + '_uses_the_same_guard_and_retains_the_child_draft', await editor.isVisible() &&
      await name.inputValue() === 'Synthetic unsaved client name');
  }

  step('discard_only_the_child_draft');
  await page.keyboard.press('Escape');
  await guard.waitFor();
  await page.screenshot({ path: path.join(output, 'owned-unsaved-dialog.png'), fullPage: true });
  await discard.click();
  await editor.waitFor({ state: 'hidden' });
  check('discarding_child_keeps_parent_unsaved', await options(page).isChecked() &&
    await page.getByRole('button', { name: 'Save Changes', exact: true }).isEnabled());
  await open.click();
  await name.waitFor();
  check('reopened_child_has_the_original_name', await name.inputValue() === config.downloadClientName);
  await editor.getByRole('button', { name: 'Close', exact: true }).click();
  await editor.waitFor({ state: 'hidden' });
  check('pristine_child_closes_without_a_prompt', !await guard.isVisible());
  await setCheckbox(options(page), false);
}

async function verifyDesktop(page, base, config, result) {
  await verifyDraft(page, base, config, result);
  await verifyPageGuard(page, result);
  await verifyDialogGuard(page, config, result);
}

async function verifyPhone(page, _base, config, { check, step }) {
  step('phone_draft_and_dialog');
  await signIn(page, config, settingsPath);
  await showOptions(page);
  const checkbox = options(page);
  check('desktop_save_is_visible_in_an_independent_phone_session', !await checkbox.isChecked());
  await setCheckbox(checkbox, true);
  await editClient(page, config.downloadClientName).click();
  const editor = page.getByRole('dialog', { name: /^Edit Download Client/ });
  await editor.locator('input[name="name"]').fill('Synthetic phone draft');
  await editor.getByRole('button', { name: 'Close', exact: true }).click();
  const guard = page.getByRole('dialog', { name: 'Unsaved Changes', exact: true });
  await guard.getByRole('button', { name: 'Discard changes and close', exact: true }).click();
  await editor.waitFor({ state: 'hidden' });
  check('child_discard_preserves_the_phone_page_draft', await checkbox.isChecked());
  await setCheckbox(checkbox, false);
}

report(config => runBrowser(config, verifyDesktop, verifyPhone));
