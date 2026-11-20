// Owned fixture only. Configuration and synthetic credentials arrive on stdin.
const fs = require('fs');

(async () => {
  let input = '';
  for await (const chunk of process.stdin) input += chunk;
  const cfg = JSON.parse(input);
  const { chromium } = require(cfg.playwrightModule);
  const origin = new URL(cfg.url).origin;
  if (!['http://127.0.0.1', 'http://localhost'].some(prefix => origin.startsWith(prefix + ':'))) {
    throw new Error('Loopback fixture required');
  }
  const checks = {};
  const check = (name, condition) => {
    checks[name] = Boolean(condition);
    if (!condition) throw new Error(name);
  };
  let browser;
  let completed = false;
  let failureType;
  try {
    browser = await chromium.launch({ headless: true });
    for (const [name, viewport] of [['desktop', { width: 1280, height: 960 }], ['phone', { width: 390, height: 844 }]]) {
      const context = await browser.newContext({ viewport });
      try {
        let externalRequests = 0;
        await context.route('**/*', route => {
          const url = new URL(route.request().url());
          if (url.origin === origin || ['data:', 'blob:'].includes(url.protocol)) return route.continue();
          externalRequests++;
          return route.abort();
        });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        const anonymous = await context.request.get(cfg.url + '/api/v1/book', { maxRedirects: 0 });
        check(name + '_anonymous_api_denied', [401, 302, 303].includes(anonymous.status()));
        await page.goto(cfg.url + '/books');
        await page.locator('input[name="username"]').fill(cfg.username);
        await page.locator('input[name="password"]').fill(cfg.password);
        await Promise.all([
          page.waitForURL(url => !url.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);
        check(name + '_forms_cookie_present', (await context.cookies()).some(cookie => cookie.httpOnly));
        const cookieOnly = await context.request.get(cfg.url + '/api/v1/book', { maxRedirects: 0 });
        check(name + '_cookie_cannot_bypass_api_key', [401, 302, 303].includes(cookieOnly.status()));
        const response = await context.request.get(cfg.url + '/api/v1/book', { headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': cfg.apiKey } });
        const books = await response.json();
        check(name + '_key_authenticated_api', response.status() === 200 && books.length === cfg.cases.length);
        for (const item of cfg.cases) {
          const prefix = name + '_' + item.key.replace(/-/g, '_');
          const book = books.find(value => value.id === item.id);
          check(prefix + '_exact_recording', book && book.foreignBookId === item.recordingId && book.foreignEditionId === item.recordingId && book.narrators.join('|') === item.narrator);
          await page.goto(cfg.url + '/book/synthetic-identity-' + item.key);
          await page.getByText(item.title, { exact: true }).first().waitFor();
          check(prefix + '_correct_detail_route', new URL(page.url()).pathname.endsWith('/book/synthetic-identity-' + item.key));
          check(prefix + '_narration_visible', (await page.locator('body').innerText()).includes(item.narrator));
          if (item.key === 'local') {
            check(prefix + '_local_identity_visible', (await page.locator('body').innerText()).includes('Local recording'));
            await page.screenshot({ path: cfg.output + '/' + name + '-local-recording.png' });
          }
        }
        check(name + '_no_external_requests', externalRequests === 0);
        check(name + '_no_javascript_errors', errors.length === 0);
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
  process.stdout.write(JSON.stringify({ checks, completed, ...(failureType ? { failureType } : {}) }));
  process.exitCode = completed && Object.values(checks).every(Boolean) ? 0 : 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: { browser_started: false }, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
