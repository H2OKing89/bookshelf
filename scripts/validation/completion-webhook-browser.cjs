// The application and the webhook test receiver are owned loopback fixtures.
const fs = require('fs');
const http = require('http');
const path = require('path');

(async () => {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 16384) throw new Error('InvalidInput');
  }
  const cfg = JSON.parse(input);
  const base = new URL(cfg.url);
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port || base.username || base.password ||
      base.search || base.hash || !/^synthetic-/.test(cfg.username)) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }
  const output = path.resolve(cfg.output);
  const directory = fs.lstatSync(output);
  if (!directory.isDirectory() || directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(output).length) {
    throw new Error('NewPrivateOutputRequired');
  }
  const { chromium } = require(cfg.playwrightModule);
  const url = cfg.url.replace(/\/$/, '');
  const headers = { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': cfg.apiKey };
  const checks = {};
  const received = [];
  let step = 'launch';
  let browser;
  let completed = false;
  let failureType;
  let failureCheck;
  const check = (name, result) => {
    checks[name] = Boolean(result);
    if (!result) {
      failureCheck = name;
      throw new Error('CheckFailed');
    }
  };
  const sink = http.createServer((request, response) => {
    let payload = '';
    request.on('data', chunk => {
      payload += chunk;
      if (payload.length > 65536) request.destroy();
    });
    request.on('end', () => {
      try {
        const body = JSON.parse(payload);
        received.push({ path: request.url, type: String(body.eventType).toLowerCase() });
        response.writeHead(204).end();
      } catch (_) {
        response.writeHead(400).end();
      }
    });
  });
  try {
    await new Promise(resolve => sink.listen(0, '127.0.0.1', resolve));
    const targetPath = '/owned-webhook-test';
    const target = `http://127.0.0.1:${sink.address().port}${targetPath}`;
    browser = await chromium.launch({ headless: true });
    for (const [name, viewport] of [['desktop', { width: 1280, height: 960 }], ['phone', { width: 390, height: 844 }]]) {
      const context = await browser.newContext({ viewport });
      try {
        let externalRequests = 0;
        await context.route('**/*', route => {
          const destination = new URL(route.request().url());
          if (destination.origin === base.origin || ['data:', 'blob:'].includes(destination.protocol)) return route.continue();
          externalRequests++;
          return route.abort();
        });
        const request = async (endpoint, method = 'GET', data, expected = 200) => {
          const response = await context.request.fetch(url + '/api/v1/' + endpoint, { headers, method, data, maxRedirects: 0 });
          if (response.status() !== expected) {
            const failure = new Error('OwnedApiUnexpectedStatus');
            failure.name += response.status();
            throw failure;
          }
          const body = await response.text();
          return body ? JSON.parse(body) : null;
        };
        const optIn = 'onImportComplete';
        const completion = resource => resource.fields.find(field => field.name === optIn)?.value;
        step = name + '_schema';
        const templates = await request('notification/schema');
        const webhook = templates.find(candidate => candidate.implementation === 'Webhook');
        check(name + '_only_webhook_offers_completion_trigger', !!webhook && webhook.fields.some(field => field.name === optIn) &&
          templates.filter(item => item.implementation !== 'Webhook').every(item => !item.fields.some(field => field.name === optIn)));
        check(name + '_completion_is_off_in_schema', completion(webhook) === false);
        step = name + '_legacy_configuration';
        const connectionName = `Synthetic ${name} completion webhook`;
        const legacy = { ...webhook, name: connectionName };
        for (const key of Object.keys(legacy).filter(key => /^on[A-Z]/.test(key))) legacy[key] = false;
        legacy.fields = legacy.fields.filter(field => field.name !== optIn).map(field =>
          field.name === 'url' ? { ...field, value: target } : field);
        const created = await request('notification', 'POST', legacy, 201);
        const endpoint = 'notification/' + created.id;
        check(name + '_omitted_checkbox_defaults_to_false', completion(await request(endpoint)) === false);
        step = name + '_login';
        const page = await context.newPage();
        page.setDefaultTimeout(15000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        await page.goto(url + '/settings/connect');
        await page.locator('input[name="username"]').fill(cfg.username);
        await page.locator('input[name="password"]').fill(cfg.password);
        await Promise.all([
          page.waitForURL(current => !current.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);
        const open = async () => {
          await page.goto(url + '/settings/connect');
          const card = page.locator('div:has(> [class*="underlay"])')
            .filter({ has: page.getByText(connectionName, { exact: true }) });
          await card.locator(':scope > [class*="underlay"]').click();
        };
        step = name + '_opt_in';
        await open();
        const checkbox = page.locator(`input[name="${optIn}"]`);
        await checkbox.waitFor({ state: 'visible' });
        await checkbox.locator('..').click();
        check(name + '_user_can_enable_completion', await checkbox.isChecked());
        await checkbox.locator('..').scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, name + '-completion-toggle.png'), fullPage: true });
        const [saved] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'PUT' && new URL(response.url()).pathname.endsWith('/' + endpoint)),
          page.getByRole('button', { name: 'Save', exact: true }).click()
        ]);
        check(name + '_browser_saves_completion_opt_in', saved.status() === 202 && completion(saved.request().postDataJSON()) === true);
        const opted = await request(endpoint);
        check(name + '_completion_opt_in_is_independent_of_legacy_import', completion(opted) === true && opted.onReleaseImport === false);
        step = name + '_compatibility_and_off';
        const older = { ...opted, fields: opted.fields.filter(field => field.name !== optIn) };
        await request(endpoint + '?forceSave=true', 'PUT', older, 202);
        check(name + '_older_update_preserves_opt_in', completion(await request(endpoint)) === true);
        await request(endpoint + '?forceSave=true', 'PUT', {
          ...opted, fields: opted.fields.map(field => field.name === optIn ? { ...field, value: false } : field)
        }, 202);
        const disabled = await request(endpoint);
        check(name + '_explicit_false_turns_trigger_off', completion(disabled) === false && disabled.onReleaseImport === false);
        check(name + '_no_browser_errors_or_external_requests', errors.length === 0 && externalRequests === 0);
      } finally {
        await context.close();
      }
    }
    check('receiver_only_received_owned_test_events', received.length > 0 && received.every(item =>
      item.path === targetPath && item.type === 'test'));
    completed = true;
  } catch (error) {
    failureType = error.name;
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => sink.close(resolve));
  }
  process.stdout.write(JSON.stringify({ checks, completed, failureCheck, failureType, failureStep: completed ? undefined : step }));
  process.exitCode = completed && Object.values(checks).every(Boolean) ? 0 : 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: { owned_browser_started: false }, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
