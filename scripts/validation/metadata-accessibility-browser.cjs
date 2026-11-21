const fs = require('fs');
const path = require('path');

async function verify(config) {
  const base = new URL(config.url);
  const output = path.resolve(config.output);
  const directory = fs.lstatSync(output);
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port || base.username || base.password ||
      base.search || base.hash || !/^synthetic-/.test(config.username) ||
      !directory.isDirectory() || directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(output).length) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }

  const { chromium, devices } = require(config.playwrightModule);
  const browser = await chromium.launch({ headless: true });
  const checks = {};
  let failureCheck;
  let failureStep;
  let failureType;
  let completed = false;

  try {
    const phones = { ...devices['iPhone 13'], defaultBrowserType: undefined };
    for (const [layout, options] of [['desktop', { viewport: { width: 1280, height: 960 } }], ['phone', phones]]) {
      const context = await browser.newContext(options);
      const check = (name, accepted) => {
        checks[layout + '_' + name] = Boolean(accepted);
        if (!accepted) {
          failureCheck = layout + '_' + name;
          throw new Error('CheckFailed');
        }
      };
      try {
        let externalRequests = 0;
        let destructiveRequests = 0;
        await context.route('**/*', route => {
          const request = route.request();
          const destination = new URL(request.url());
          if (destination.origin !== base.origin && !['data:', 'blob:'].includes(destination.protocol)) {
            externalRequests++;
            return route.abort();
          }
          if (request.method() === 'DELETE') {
            destructiveRequests++;
            return route.abort();
          }
          return route.continue();
        });
        const page = await context.newPage();
        page.setDefaultTimeout(12000);
        const pageErrors = [];
        page.on('pageerror', error => pageErrors.push(error.name));
        const go = route => page.goto(config.url + route);
        const button = (name, scope = page) => scope.getByRole('button', { name, exact: true });
        const dialog = name => page.getByRole('dialog', { name, exact: true });
        const waitForFocus = async locator => {
          const element = await locator.elementHandle();
          await page.waitForFunction(target => document.activeElement === target, element, { timeout: 5000 });
        };
        const inspectDialog = async element => element.evaluate(node => {
          const headingId = node.getAttribute('aria-labelledby');
          const heading = headingId && document.getElementById(headingId);
          return {
            headingId,
            named: !!heading && node.contains(heading) && heading.textContent.trim().length > 0,
            modal: node.getAttribute('aria-modal') === 'true'
          };
        });
        const inDialog = element => element.evaluate(node => node.contains(document.activeElement));

        failureStep = layout + '_sign_in';
        await go('/system/status');
        await page.locator('input[name="username"]').fill(config.username);
        await page.locator('input[name="password"]').fill(config.password);
        await Promise.all([
          page.waitForURL(url => !url.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);
        failureStep = layout + '_provider_status';
        await page.getByText('Metadata Providers', { exact: true }).waitFor();
        await page.getByText('Rate limited', { exact: true }).waitFor();
        await page.getByText('Missing', { exact: true }).first().waitFor();
        check('provider_diagnostics_show_latest_outcomes', (await page.getByText('Audnexus', { exact: true }).count()) >= 2);
        await page.screenshot({ path: path.join(output, layout + '-metadata-status.png'), fullPage: true });

        failureStep = layout + '_monitor';
        await go('/book/synthetic-identity-us');
        const monitor = button('Monitor Book').first();
        await monitor.waitFor();
        check('monitor_reports_unpressed_state', await monitor.getAttribute('aria-pressed') === 'false');
        check('monitor_icon_is_decorative', await monitor.locator('svg[aria-hidden="true"]').count() === 1);
        await monitor.focus();
        await page.keyboard.press('Enter');
        await page.waitForFunction(() => {
          const button = document.querySelector('button[aria-label="Monitor Book"]');
          return button?.getAttribute('aria-pressed') === 'true' && !button.disabled;
        });
        check('enter_changes_monitoring', await monitor.getAttribute('aria-pressed') === 'true');
        await monitor.focus();
        await page.keyboard.press('Space');
        await page.waitForFunction(() => {
          const button = document.querySelector('button[aria-label="Monitor Book"]');
          return button?.getAttribute('aria-pressed') === 'false' && !button.disabled;
        });
        check('space_restores_monitoring', await monitor.getAttribute('aria-pressed') === 'false');

        failureStep = layout + '_file_actions';
        await go('/unmapped');
        const local = button('Create local recording');
        await local.waitFor();
        check('create_requires_a_file', await local.isDisabled());
        const fileDetails = button('File Details').first();
        const deleteFile = button('Delete Book File').first();
        await fileDetails.waitFor();
        check('unmapped_file_actions_have_distinct_names',
          await deleteFile.isVisible() && await button('Manual Import').first().isVisible());
        check('file_action_icons_are_decorative', await fileDetails.locator('svg[aria-hidden="true"]').count() === 1);
        const tableOptions = button('Table Options');
        check('table_options_has_an_action_name',
          await tableOptions.count() > 0 && await page.locator('[aria-label="Table Options Button"]').count() === 0);
        await deleteFile.focus();
        await page.keyboard.press('Enter');
        const deletion = dialog('Delete Book File');
        await deletion.waitFor();
        const deletionSemantics = await inspectDialog(deletion);
        check('file_delete_dialog_has_title', deletionSemantics.named && deletionSemantics.modal);
        const cancelDeletion = button('Cancel', deletion);
        failureStep = layout + '_focus_defaults_to_cancel';
        await waitForFocus(cancelDeletion);
        failureStep = layout + '_enter_cancels_delete';
        await page.keyboard.press('Enter');
        await deletion.waitFor({ state: 'hidden' });
        failureStep = layout + '_delete_opener_focus_returns';
        await waitForFocus(deleteFile);
        check('enter_on_cancel_does_not_delete', destructiveRequests === 0);

        failureStep = layout + '_local_recording';
        await page.locator('input[type="checkbox"]').last().focus();
        await page.keyboard.press('Space');
        await local.click();
        const create = dialog('Create local recording');
        await create.waitFor();
        const creationSemantics = await inspectDialog(create);
        check('local_recording_dialog_has_unique_title',
          creationSemantics.named && creationSemantics.modal && creationSemantics.headingId !== deletionSemantics.headingId);
        await create.getByLabel('Title', { exact: true }).waitFor();
        check('local_recording_reads_owned_tags',
          await create.getByLabel('Title', { exact: true }).inputValue() === 'Synthetic Unmapped Recording');
        await page.screenshot({ path: path.join(output, layout + '-local-recording-dialog.png'), fullPage: true });
        await page.keyboard.press('Escape');
        await create.waitFor({ state: 'hidden' });
        await waitForFocus(local);
        check('local_dialog_restores_its_opener', await create.count() === 0);

        failureStep = layout + '_folder_picker';
        await go('/settings/mediamanagement');
        await button('Add Root Folder').click();
        const rootFolder = dialog('Add Root Folder');
        await rootFolder.waitFor();
        const browse = button('Browse Folders', rootFolder);
        check('folder_picker_has_an_action_name', await browse.isVisible());
        await page.keyboard.press('Escape');
        await rootFolder.waitFor({ state: 'hidden' });

        failureStep = layout + '_nested_confirmation';
        await go('/settings/downloadclients');
        const opener = button('Manage Clients');
        await opener.click();
        const manager = dialog('Manage Download Clients');
        await manager.waitFor();
        const parent = await inspectDialog(manager);
        const row = manager.getByRole('row').filter({ hasText: config.downloadClientName });
        await row.locator('input[type="checkbox"]').focus();
        await page.keyboard.press('Space');
        const removeClients = button('Delete', manager);
        await removeClients.click();
        const confirmation = dialog('Delete Download Client(s)');
        await confirmation.waitFor();
        const child = await inspectDialog(confirmation);
        check('nested_dialog_titles_are_distinct', child.named && child.modal && parent.named && child.headingId !== parent.headingId);
        check('only_top_dialog_is_exposed', await page.getByRole('dialog').count() === 1 && await manager.count() === 0);
        const cancelConfirmation = button('Cancel', confirmation);
        await waitForFocus(cancelConfirmation);
        await page.keyboard.press('Shift+Tab');
        check('reverse_tab_stays_inside_top_dialog', await inDialog(confirmation));
        await page.keyboard.press('Tab');
        await page.keyboard.press('Tab');
        await page.keyboard.press('Tab');
        check('tab_stays_inside_top_dialog', await inDialog(confirmation));
        await page.keyboard.press('Escape');
        await confirmation.waitFor({ state: 'hidden' });
        await manager.waitFor();
        await waitForFocus(removeClients);
        check('first_escape_restores_parent_focus', await manager.isVisible());

        if (layout === 'desktop') {
          failureStep = layout + '_backdrop_drag';
          const content = await manager.boundingBox();
          if (content && content.x > 0 && content.y > 0) {
            await page.mouse.move(content.x + 25, content.y + 20);
            await page.mouse.down();
            await page.mouse.move(5, 5);
            await page.mouse.up();
            check('drag_from_dialog_to_backdrop_does_not_close', await manager.isVisible());
          } else {
            check('desktop_dialog_has_backdrop', false);
          }
        }
        await page.keyboard.press('Escape');
        await manager.waitFor({ state: 'hidden' });
        await waitForFocus(opener);
        check('second_escape_restores_original_focus', await manager.count() === 0);
        check('browser_uses_only_owned_requests', externalRequests === 0 && destructiveRequests === 0);
        check('no_page_errors', pageErrors.length === 0);
      } finally {
        await context.close();
      }
    }
    completed = true;
  } catch (error) {
    failureType = error.name;
  } finally {
    await browser.close();
  }
  return { checks, completed, failureType, failureCheck, failureStep: completed ? undefined : failureStep };
}

(async () => {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 16384) throw new Error('InvalidInput');
  }
  const result = await verify(JSON.parse(input));
  process.stdout.write(JSON.stringify(result));
  process.exitCode = result.completed ? 0 : 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: { browser_started: false }, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
