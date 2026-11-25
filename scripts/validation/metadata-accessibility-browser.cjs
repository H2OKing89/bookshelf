const fs = require('fs');
const path = require('path');
const { verifyMetadataStatusTransport } = require('./metadata-status-transport-browser.cjs');
const { observeReactWarnings, observeSignalR, verifySignalRResync, verifyTableOptionsReorder } = require('./react-compatibility-browser.cjs');

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
  const reactWarnings = process.env.BOOKSHELF_TRACK_REACT_WARNINGS === '1' ? {} : undefined;
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
        if (reactWarnings) {
          reactWarnings[layout] = observeReactWarnings(page);
        }
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
        const descriptions = control => control.evaluate(node =>
          (node.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean).map(id =>
            document.getElementById(id)?.textContent.trim() || ''));

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
        if (layout === 'desktop') {
          await verifyMetadataStatusTransport(context, base, check, step => { failureStep = layout + '_' + step; });
        }

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

        failureStep = layout + '_sort_menu_keyboard';
        await go('/books');
        const sort = button('Sort');
        const sortMenu = page.getByRole('menu', { name: 'Sort', exact: true });
        const sortItems = sortMenu.getByRole('menuitemradio');
        const firstSort = sortItems.first();
        const lastSort = sortItems.last();
        await sort.locator('xpath=self::*[not(@disabled)]').waitFor();
        await sort.focus();
        await page.keyboard.press('ArrowDown');
        await waitForFocus(firstSort);
        check('sort_exposes_popup_and_selected_choice', await sort.getAttribute('aria-expanded') === 'true' &&
          await sort.getAttribute('aria-controls') === await sortMenu.getAttribute('id') &&
          await sortMenu.getByRole('menuitemradio', { name: 'Title', exact: true }).getAttribute('aria-checked') === 'true');
        await page.keyboard.press('ArrowUp');
        await waitForFocus(lastSort);
        await page.keyboard.press('ArrowDown');
        await waitForFocus(firstSort);
        await page.keyboard.press('End');
        await waitForFocus(lastSort);
        await page.keyboard.press('Home');
        await waitForFocus(firstSort);
        await page.keyboard.press('Enter');
        await sortMenu.waitFor({ state: 'hidden' });
        await waitForFocus(sort);
        await page.keyboard.press('Enter');
        await waitForFocus(firstSort);
        check('keyboard_selects_sort_and_restores_focus', await firstSort.getAttribute('aria-checked') === 'true');

        failureStep = layout + '_menu_dismissal';
        await button('Filter').click();
        await sortMenu.waitFor({ state: 'hidden' });
        const filterMenu = page.getByRole('menu', { name: 'Filter', exact: true });
        await filterMenu.waitFor();
        check('opening_filter_dismisses_sort', await sort.getAttribute('aria-expanded') === 'false' &&
          await filterMenu.getByRole('menuitemradio', { checked: true }).count() === 1);
        await page.mouse.click(1, page.viewportSize().height - 2);
        await filterMenu.waitFor({ state: 'hidden' });
        check('outside_pointer_dismisses_filter', await button('Filter').getAttribute('aria-expanded') === 'false');

        failureStep = layout + '_menu_viewport';
        const originalViewport = page.viewportSize();
        if (layout === 'desktop') {
          await page.setViewportSize({ width: 640, height: 480 });
          await go('/books');
          await sort.locator('xpath=self::*[not(@disabled)]').waitFor();
        }
        await sort.focus();
        await page.keyboard.press('ArrowUp');
        await waitForFocus(lastSort);
        await page.waitForFunction(node => {
          const rect = node.getBoundingClientRect();
          const viewport = window.visualViewport;
          const left = viewport?.offsetLeft ?? 0;
          const top = viewport?.offsetTop ?? 0;
          return rect.width > 0 && rect.height > 0 && rect.left >= left - 1 && rect.top >= top - 1 &&
            rect.right <= left + (viewport?.width ?? window.innerWidth) + 1 &&
            rect.bottom <= top + (viewport?.height ?? window.innerHeight) + 1;
        }, await sortMenu.elementHandle(), { timeout: 5000 });
        check('sort_fits_the_available_viewport', true);
        await page.keyboard.press('Escape');
        await sortMenu.waitFor({ state: 'hidden' });
        await waitForFocus(sort);
        check('escape_collapses_sort_and_restores_focus', await sort.getAttribute('aria-expanded') === 'false');

        failureStep = layout + '_menu_tab_order';
        await sort.press('ArrowDown');
        await waitForFocus(firstSort);
        await page.keyboard.press('Tab');
        await sortMenu.waitFor({ state: 'hidden' });
        await waitForFocus(button('Filter'));
        await sort.press('ArrowDown');
        await waitForFocus(firstSort);
        await page.keyboard.press('Shift+Tab');
        await sortMenu.waitFor({ state: 'hidden' });
        await waitForFocus(button('View'));
        check('tab_and_shift_tab_continue_in_toolbar_order', true);
        if (layout === 'desktop') {
          await page.setViewportSize(originalViewport);
        }

        failureStep = layout + '_file_actions';
        const signalR = await observeSignalR(page, base);
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
        failureStep = layout + '_signalr_reconnect';
        await verifySignalRResync(page, base, signalR, check);
        if (layout === 'desktop') {
          failureStep = layout + '_table_options_reorder';
          await verifyTableOptionsReorder(page, tableOptions.first(), check);
        }
        failureStep = layout + '_file_delete_confirmation';
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
        check('local_recording_author_help_is_associated', (await descriptions(create.getByLabel('Author', { exact: true })))
          .some(text => text.includes('File author')));
        await page.screenshot({ path: path.join(output, layout + '-local-recording-dialog.png'), fullPage: true });
        await page.keyboard.press('Escape');
        await create.waitFor({ state: 'hidden' });
        await waitForFocus(local);
        check('local_dialog_restores_its_opener', await create.count() === 0);

        if (layout === 'phone') {
          failureStep = layout + '_save_local_recording';
          await local.click();
          await create.getByLabel('Title', { exact: true }).waitFor();
          const [saved] = await Promise.all([
            page.waitForResponse(response => response.request().method() === 'POST' &&
              new URL(response.url()).pathname.toLowerCase().endsWith('/localrecording')),
            button('Create recording', create).click()
          ]);
          check('creates_local_recording', saved.ok() && /^local:recording:[a-f0-9]{32}$/.test((await saved.json()).foreignBookId));
          await create.waitFor({ state: 'hidden' });
          await page.getByRole('link', { name: 'Synthetic Unmapped Recording', exact: true }).waitFor();
          check('local_recording_links_to_created_book', true);
        }

        failureStep = layout + '_enhanced_select_geometry';
        await go('/settings/ui');
        const weekLabel = page.locator('label').filter({ hasText: /^First Day of Week$/ });
        await weekLabel.waitFor();
        const weekId = await weekLabel.getAttribute('for');
        const week = page.locator(`button[id="${weekId}"]`);
        await week.waitFor();
        const originalWeek = await week.innerText();
        if (layout === 'desktop') {
          await week.press('ArrowDown');
          const popupId = await week.getAttribute('aria-controls');
          const popup = page.locator(`[id="${popupId}"]`);
          await popup.waitFor();
          await page.waitForFunction(({ popupId, weekId }) => {
            const popup = document.getElementById(popupId)?.getBoundingClientRect();
            const anchor = document.getElementById(weekId)?.getBoundingClientRect();
            const width = window.visualViewport?.width ?? window.innerWidth;
            const height = window.visualViewport?.height ?? window.innerHeight;
            return popup && anchor && popup.width >= anchor.width - 2 &&
              popup.left >= -1 && popup.right <= width + 1 &&
              popup.top >= -1 && popup.bottom <= height + 1;
          }, { popupId, weekId });
          check('enhanced_select_anchors_and_fits_viewport', true);
          await page.mouse.click(1, page.viewportSize().height - 2);
          await popup.waitFor({ state: 'hidden' });
          check('keyboard_opened_select_dismisses_on_outside_pointer', await week.getAttribute('aria-expanded') === 'false');
          await week.press('ArrowDown');
          await page.locator(`[id="${await week.getAttribute('aria-controls')}"]`).waitFor();
          await week.press('Escape');
        } else {
          await week.tap();
          const options = dialog('Options');
          await options.waitFor();
          check('touch_select_retains_options_dialog', await week.getAttribute('aria-expanded') === 'true');
          await button('Close', options).click();
          await options.waitFor({ state: 'hidden' });
        }
        check('cancelled_select_preserves_current_value', await week.innerText() === originalWeek &&
          await week.getAttribute('aria-expanded') === 'false');

        failureStep = layout + '_folder_picker';
        await go('/settings/mediamanagement');
        await button('Add Root Folder').click();
        const rootFolder = dialog('Add Root Folder');
        await rootFolder.waitFor();
        const browse = button('Browse Folders', rootFolder);
        check('folder_picker_has_an_action_name', await browse.isVisible());
        const name = rootFolder.getByLabel('Name', { exact: true });
        const folderPath = rootFolder.getByLabel('Path', { exact: true });
        const profile = rootFolder.getByLabel('Quality Profile', { exact: true });
        await rootFolder.locator('label').filter({ hasText: /^Name$/ }).click();
        await waitForFocus(name);
        check('root_folder_label_focuses_associated_input', await name.getAttribute('name') === 'name');
        check('profile_label_and_help_reach_the_select',
          await profile.getAttribute('name') === 'defaultQualityProfileId' &&
          (await descriptions(profile)).some(text => text.includes('authors detected')));
        check('path_help_reaches_the_autosuggest_input',
          await folderPath.getAttribute('name') === 'path' &&
          (await descriptions(folderPath)).some(text => text.includes('your book library')));
        failureStep = layout + '_autosuggest_geometry';
        await folderPath.fill(config.suggestionPrefix);
        const suggestion = page.getByRole('option').filter({ hasText: 'Synthetic Choices' });
        await suggestion.waitFor();
        const suggestionList = page.locator(`[id="${await folderPath.getAttribute('aria-controls')}"]`);
        const inputRect = await folderPath.boundingBox();
        const listRect = await suggestionList.boundingBox();
        check('owned_autosuggest_matches_path_input_width', !!inputRect && !!listRect &&
          Math.abs(inputRect.width - listRect.width) <= 2 && listRect.x >= -1 &&
          listRect.x + listRect.width <= page.viewportSize().width + 1 &&
          (await suggestion.innerText()).includes(config.suggestionPath));
        await name.focus();
        await suggestion.waitFor({ state: 'hidden' });
        check('autosuggest_blur_dismisses_popup', true);

        failureStep = layout + '_tooltip_geometry';
        const info = rootFolder.locator('label').filter({ hasText: /^Monitor$/ }).locator('svg');
        await info.waitFor();
        if (layout === 'desktop') {
          await info.hover();
        } else {
          await info.tap();
        }
        const tooltip = page.getByText('Monitoring Options', { exact: true });
        await tooltip.waitFor();
        const box = await tooltip.boundingBox();
        check('monitoring_tooltip_fits_viewport', !!box && box.width > 0 &&
          box.x >= -1 && box.x + box.width <= page.viewportSize().width + 1);
        if (layout === 'desktop') {
          await name.hover();
        } else {
          await info.tap();
        }
        await tooltip.waitFor({ state: 'hidden' });
        check('monitoring_tooltip_closes_after_interaction', true);
        await name.fill('Synthetic Invalid Folder');
        await folderPath.fill('synthetic-invalid-relative-path');
        await folderPath.blur();
        failureStep = layout + '_server_validates_relative_root';
        const [invalidFolder] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'POST' &&
            new URL(response.url()).pathname.toLowerCase().endsWith('/rootfolder')),
          button('Save', rootFolder).click()
        ]);
        check('invalid_root_folder_request_is_rejected', invalidFolder.status() === 400);
        failureStep = layout + '_path_receives_server_error';
        await folderPath.locator('xpath=self::*[@aria-invalid="true"]').waitFor();
        const invalidDescriptions = await descriptions(folderPath);
        check('path_describes_help_and_server_error',
          invalidDescriptions.some(text => text.includes('your book library')) &&
          invalidDescriptions.length > 1 && invalidDescriptions.every(Boolean));
        await page.keyboard.press('Escape');
        await rootFolder.waitFor({ state: 'hidden' });

        failureStep = layout + '_nested_confirmation';
        await go('/settings/downloadclients');
        const clientCard = page.locator('div:has(> button[class*="underlay"])')
          .filter({ has: page.getByText(config.downloadClientName, { exact: true }) });
        await clientCard.locator(':scope > button[class*="underlay"]').click();
        const providerEditor = page.getByRole('dialog', { name: /Edit Download Client/ });
        await providerEditor.waitFor();
        const password = providerEditor.getByLabel('Password', { exact: true });
        check('provider_password_label_reaches_password_control',
          await password.getAttribute('type') === 'password' && await password.getAttribute('name') === 'password');
        await page.keyboard.press('Escape');
        await providerEditor.waitFor({ state: 'hidden' });
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
  return { checks, completed, failureType, failureCheck, failureStep: completed ? undefined : failureStep, reactWarnings };
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
