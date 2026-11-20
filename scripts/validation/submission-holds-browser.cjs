// Uses a new owned loopback home. Synthetic credentials arrive only on stdin.
const fs = require('fs');
const path = require('path');

(async () => {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 16384) throw new Error('InvalidInput');
  }
  const cfg = JSON.parse(input);
  const base = new URL(cfg.url);
  if (base.protocol !== 'http:' || !['127.0.0.1', 'localhost'].includes(base.hostname) || !base.port ||
      base.username || base.password || base.search || base.hash || !/^synthetic-/.test(cfg.username)) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }
  const output = path.resolve(cfg.output);
  const directory = fs.lstatSync(output);
  if (!directory.isDirectory() || directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(output).length) {
    throw new Error('NewPrivateOutputRequired');
  }
  const { chromium } = require(cfg.playwrightModule);
  const url = cfg.url.replace(/\/$/, '');
  const checks = {};
  let completed = false;
  let failureCheck;
  let failureType;
  let step = 'launch';
  let browser;
  const check = (name, value) => {
    checks[name] = Boolean(value);
    if (!value) {
      failureCheck = name;
      throw new Error('CheckFailed');
    }
  };
  try {
    browser = await chromium.launch({ headless: true });
    for (const [name, viewport] of [['desktop', { width: 1280, height: 960 }], ['phone', { width: 390, height: 844 }]]) {
      const context = await browser.newContext({ viewport });
      try {
        let externalRequests = 0;
        await context.route('**/*', route => {
          const requested = new URL(route.request().url());
          if (requested.origin === base.origin || ['data:', 'blob:'].includes(requested.protocol)) return route.continue();
          externalRequests++;
          return route.abort();
        });
        const endpoint = url + '/api/v1/downloadsubmission';
        const headers = { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': cfg.apiKey };
        const pending = async () => {
          const response = await context.request.get(endpoint, { headers });
          if (response.status() !== 200) throw new Error('OwnedApiUnavailable');
          return response.json();
        };
        const ownTitle = `Synthetic ${name} review`;
        const hold = (await pending()).find(item => item.title === ownTitle);
        check(name + '_hold_is_persisted_and_reviewable', hold?.canAcknowledge && hold.downloadClientId === 12);
        const anonymous = await context.request.get(endpoint, { maxRedirects: 0 });
        check(name + '_anonymous_holds_are_denied', [401, 302, 303].includes(anonymous.status()));
        const body = { attemptId: hold.attemptId, downloadClientId: 13, clientReviewed: true };
        const mismatch = await context.request.post(endpoint + '/acknowledge', { headers, data: body });
        check(name + '_wrong_client_cannot_clear_hold', mismatch.status() === 409 && (await pending()).some(item => item.attemptId === hold.attemptId));
        const unreviewed = await context.request.post(endpoint + '/acknowledge', {
          headers, data: { ...body, downloadClientId: 12, clientReviewed: false }
        });
        check(name + '_review_confirmation_is_required', unreviewed.status() === 400);
        const page = await context.newPage();
        page.setDefaultTimeout(15000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        step = name + '_login';
        await page.goto(url + '/activity/queue');
        await page.locator('input[name="username"]').fill(cfg.username);
        await page.locator('input[name="password"]').fill(cfg.password);
        await Promise.all([
          page.waitForURL(current => !current.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);
        await page.goto(url + '/activity/queue');
        step = name + '_review';
        const alert = page.locator('div').filter({ has: page.getByText('Submission needs review: ' + ownTitle, { exact: true }) })
          .filter({ has: page.getByRole('button', { name: 'Review submission', exact: true }) }).last();
        await alert.getByRole('button', { name: 'Review submission', exact: true }).click();
        const confirm = page.getByRole('button', { name: 'I checked the client; clear hold', exact: true });
        await confirm.waitFor({ state: 'visible' });
        check(name + '_confirmation_explains_effect', await page.getByText(/It does not remove or restart any download/).isVisible());
        await page.screenshot({ path: path.join(output, name + '-review.png'), fullPage: true });
        await page.getByRole('button', { name: 'Cancel', exact: true }).click();
        check(name + '_cancel_keeps_hold', (await pending()).some(item => item.attemptId === hold.attemptId));
        await alert.getByRole('button', { name: 'Review submission', exact: true }).click();
        step = name + '_acknowledge';
        const [response] = await Promise.all([
          page.waitForResponse(response => response.url() === endpoint + '/acknowledge' && response.request().method() === 'POST'),
          confirm.click()
        ]);
        const sent = response.request().postDataJSON();
        check(name + '_browser_sends_exact_scope_and_review', response.status() === 204 && sent.attemptId === hold.attemptId &&
          sent.downloadClientId === 12 && sent.clientReviewed === true);
        await page.getByText('Submission needs review: ' + ownTitle, { exact: true }).waitFor({ state: 'hidden' });
        await page.reload();
        await page.getByText('Submission needs review: Synthetic other client', { exact: true }).waitFor({ state: 'visible' });
        check(name + '_cleared_hold_stays_cleared_after_reload', !(await pending()).some(item => item.attemptId === hold.attemptId));
        check(name + '_other_client_hold_remains', (await pending()).some(item => item.title === 'Synthetic other client' && item.downloadClientId === 13));
        check(name + '_no_horizontal_overflow', await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
        check(name + '_no_browser_errors', errors.length === 0);
        check(name + '_no_external_requests', externalRequests === 0);
      } finally {
        await context.close();
      }
    }
    completed = true;
  } catch (error) {
    failureType = error.name;
  } finally {
    if (browser) await browser.close();
  }
  process.stdout.write(JSON.stringify({ checks, completed, failureCheck, failureType, failureStep: completed ? undefined : step }));
  process.exitCode = completed && Object.values(checks).every(Boolean) ? 0 : 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: { owned_browser_started: false }, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
