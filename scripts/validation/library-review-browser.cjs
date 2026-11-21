const fs = require('fs');
const path = require('path');

(async () => {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 16384) throw new Error('InvalidInput');
  }
  const config = JSON.parse(input);
  const base = new URL(config.url);
  const folder = path.resolve(config.output);
  const directory = fs.lstatSync(folder);
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port || base.username || base.password ||
      base.search || base.hash || !/^synthetic-/.test(config.username) ||
      !directory.isDirectory() || directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(folder).length) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }

  const { chromium, devices } = require(config.playwrightModule);
  const browser = await chromium.launch({ headless: true });
  const checks = {};
  let completed = false;
  let failureType;
  let failureCheck;
  let failureStep;
  try {
    const phone = { ...devices['iPhone 13'], defaultBrowserType: undefined };
    // Review and cancel on the phone before committing each change on desktop.
    for (const [name, options] of [['phone', phone], ['desktop', { viewport: { width: 1280, height: 960 } }]]) {
      const context = await browser.newContext(options);
      const check = (label, valid) => {
        const key = name + '_' + label;
        checks[key] = Boolean(valid);
        if (!valid) {
          failureCheck = key;
          throw new Error('CheckFailed');
        }
      };
      let page;
      try {
        let unexpectedRequests = 0;
        await context.route('**/*', route => {
          const request = route.request();
          const destination = new URL(request.url());
          if ((destination.origin !== base.origin && !['data:', 'blob:'].includes(destination.protocol)) || request.method() === 'DELETE') {
            unexpectedRequests++;
            return route.abort();
          }
          return route.continue();
        });
        page = await context.newPage();
        page.setDefaultTimeout(12000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        const button = (label, scope = page) => scope.getByRole('button', { name: label, exact: true });
        const dialog = label => page.getByRole('dialog', { name: label, exact: true });
        const ownedApi = route => context.request.get(config.url + '/api/v1/' + route, {
          headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': config.apiKey }, maxRedirects: 0
        });

        failureStep = name + '_sign_in';
        await page.goto(config.url + '/unmapped');
        await page.locator('input[name="username"]').fill(config.username);
        await page.locator('input[name="password"]').fill(config.password);
        await Promise.all([
          page.waitForURL(url => !url.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);

        failureStep = name + '_recording_review';
        const fileCell = page.getByText(/Synthetic Side Story \[B077777777\]\.m4b/);
        await button('Review Recording', fileCell.locator('..')).click();
        const recordingDialog = dialog('Recording Match Review');
        await recordingDialog.waitFor();
        await recordingDialog.getByText(/B077777777/).first().waitFor();
        const confirmFile = button('Associate File', recordingDialog);
        check('opening_recording_review_does_not_offer_immediate_association',
          await confirmFile.count() === 0 || await confirmFile.isDisabled());
        failureStep = name + '_preview_recording';
        await button('Preview Association', recordingDialog).click();
        const confirmation = page.getByRole('dialog').last();
        const associated = button('Associate File', confirmation);
        await associated.waitFor();
        check('association_preview_names_the_exact_recording',
          (await confirmation.innerText()).includes('Synthetic Side Story') &&
          (await confirmation.innerText()).includes('B077777777'));
        await page.screenshot({ path: path.join(folder, name + '-recording-review.png'), fullPage: true });
        if (name === 'desktop') {
          failureStep = name + '_stale_recording_response';
          const reviewPath = /\/bookfile\/9101\/recordingreview$/;
          await page.route(reviewPath, route => route.request().method() === 'PUT'
            ? route.fulfill({ status: 409, json: { message: 'The file or recording changed. Review the current evidence and preview again.' } })
            : route.fallback());
          await associated.click();
          await recordingDialog.getByRole('alert').getByText(/file or recording changed/).waitFor();
          const refresh = button('Refresh evidence', recordingDialog);
          check('stale_association_keeps_the_review_open', await refresh.isVisible());
          await page.unroute(reviewPath);
          await refresh.click();
          await button('Preview Association', recordingDialog).click();
          const before = await ownedApi('book/107');
          check('target_starts_with_no_associated_files', before.ok() &&
            ((await before.json()).statistics?.bookFileCount ?? 0) === 0);
          failureStep = name + '_associate_file';
          const [result] = await Promise.all([
            page.waitForResponse(response => response.request().method() === 'PUT' &&
              new URL(response.url()).pathname.endsWith('/bookfile/9101/recordingreview')),
            associated.click()
          ]);
          const value = await result.json();
          check('associate_file_saves_reviewed_edition', result.status() === 200 && value.currentRecording?.editionId === 1107);
          const after = await ownedApi('book/107');
          check('book_file_count_refreshes_after_association', after.ok() && (await after.json()).statistics?.bookFileCount === 1);
        } else {
          await page.keyboard.press('Escape');
          await associated.waitFor({ state: 'hidden' });
          const checked = await ownedApi('bookfile/9101/recordingreview');
          check('recording_review_can_be_cancelled_without_mapping', checked.status() === 200 &&
            (await checked.json()).currentRecording == null);
        }

        failureStep = name + '_author_reconciliation';
        await page.goto(config.url + '/author/synthetic-name-variant');
        const openAuthorReview = button('Reconcile Author');
        const moreActions = button('More').first();
        failureStep = name + '_author_toolbar';
        await openAuthorReview.or(moreActions).first().waitFor();
        if (name === 'phone' || !await openAuthorReview.isVisible()) {
          failureStep = name + '_overflow_actions';
          const overflowMenu = page.getByRole('menu', { name: 'More', exact: true });
          await moreActions.focus();
          await page.keyboard.press('Space');
          await overflowMenu.waitFor();
          const reviewItem = overflowMenu.getByRole('menuitem', { name: 'Reconcile Author', exact: true });
          await reviewItem.waitFor();
          check('overflow_menu_links_its_trigger_and_actions', await moreActions.getAttribute('aria-expanded') === 'true' &&
            await moreActions.getAttribute('aria-controls') === await overflowMenu.getAttribute('id'));
          await page.keyboard.press('Escape');
          await overflowMenu.waitFor({ state: 'hidden' });
          check('escape_restores_overflow_trigger_focus', await moreActions.evaluate(node => document.activeElement === node));

          failureStep = name + '_overflow_select';
          await page.keyboard.press('ArrowDown');
          await overflowMenu.waitFor();
          await page.waitForFunction(node => node.contains(document.activeElement),
            await overflowMenu.elementHandle(), { timeout: 5000 });
          const actions = overflowMenu.getByRole('menuitem', { disabled: false });
          const actionCount = await actions.count();
          for (let index = 0; index < actionCount && !await reviewItem.evaluate(node => document.activeElement === node); index++) {
            await page.keyboard.press('ArrowDown');
          }
          check('overflow_action_is_reachable_with_arrows', await reviewItem.evaluate(node => document.activeElement === node));
          await page.keyboard.press('Enter');
          await overflowMenu.waitFor({ state: 'hidden' });
        } else {
          failureStep = name + '_author_review_opener';
          await openAuthorReview.click();
        }
        const authorDialog = dialog('Reconcile Author');
        await authorDialog.waitFor();
        failureStep = name + '_author_evidence_form';
        await authorDialog.getByLabel('Canonical author', { exact: true }).selectOption(String(config.authorReview.targetAuthorId));
        await authorDialog.getByLabel('Evidence URL', { exact: true }).fill(config.authorReview.evidenceUrl);
        await authorDialog.getByLabel('Reason', { exact: true }).fill(config.authorReview.reason);
        failureStep = name + '_preview_author';
        const [preview] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'POST' &&
            new URL(response.url()).pathname.endsWith('/authorreconciliation/preview')),
          button('Review reconciliation', authorDialog).click()
        ]);
        const value = await preview.json();
        check('author_preview_is_bound_to_source_and_canonical_target', preview.status() === 200 && value.canApply &&
          value.source.id === config.authorReview.sourceAuthorId && value.target.id === config.authorReview.targetAuthorId &&
          value.recordings.some(recording => recording.foreignEditionId === 'audible:us:B033333333'));
        const apply = button('Apply reconciliation', authorDialog);
        await apply.waitFor();
        check('author_preview_exposes_affected_recording',
          await authorDialog.getByText('Synthetic Joint Work', { exact: false }).first().isVisible());
        check('profile_difference_requires_explicit_acceptance', await apply.isDisabled());
        const keep = authorDialog.getByRole('checkbox', { name: 'Keep canonical author settings', exact: true });
        await keep.focus();
        await page.keyboard.press('Space');
        check('keyboard_can_acknowledge_canonical_settings', await keep.isChecked());
        await page.screenshot({ path: path.join(folder, name + '-author-reconciliation.png'), fullPage: true });
        if (name === 'desktop') {
          failureStep = name + '_apply_author';
          const [result] = await Promise.all([
            page.waitForResponse(response => response.request().method() === 'POST' &&
              new URL(response.url()).pathname.endsWith('/authorreconciliation')),
            apply.click()
          ]);
          check('author_reconciliation_queues_reviewed_operation', result.status() === 202 && (await result.json()).commandId > 0);
          await page.waitForURL(url => url.pathname.endsWith('/author/synthetic-restore-author'), { timeout: 25000 });
          check('successful_reconciliation_opens_canonical_profile', true);
        } else {
          await page.keyboard.press('Escape');
          await authorDialog.waitFor({ state: 'hidden' });
          await page.waitForFunction(node => document.activeElement === node,
            await moreActions.elementHandle(), { timeout: 5000 });
          check('author_preview_can_be_cancelled', await moreActions.getAttribute('aria-expanded') === 'false');
        }
        check('only_owned_browser_requests', unexpectedRequests === 0);
        check('no_page_errors', errors.length === 0);
      } catch (error) {
        if (page) {
          await page.screenshot({ path: path.join(folder, name + '-failure.png'), timeout: 3000 }).catch(() => {});
        }
        throw error;
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
  process.stdout.write(JSON.stringify({ checks, completed, failureType, failureCheck, failureStep: completed ? undefined : failureStep }));
  process.exitCode = completed ? 0 : 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: { browser_started: false }, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
