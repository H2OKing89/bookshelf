// Run only against the caller's new, owned synthetic server. Secrets use stdin.
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
      base.username || base.password || base.search || base.hash ||
      !/^synthetic-/.test(cfg.username) || !cfg.password || !cfg.apiKey) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }
  const url = cfg.url.replace(/\/$/, '');
  const output = path.resolve(cfg.output);
  if (!fs.existsSync(output)) fs.mkdirSync(output, { mode: 0o700 });
  const directory = fs.lstatSync(output);
  if (!directory.isDirectory() || directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(output).length) {
    throw new Error('NewPrivateOutputRequired');
  }
  const { chromium } = require(cfg.playwrightModule);
  const checks = {};
  let failureCheck;
  let failureType;
  let browser;
  let completed = false;
  const check = (name, condition) => {
    checks[name] = Boolean(condition);
    if (!condition) {
      failureCheck = name;
      throw new Error('CheckFailed');
    }
  };
  const comparable = value => JSON.stringify(Object.fromEntries(Object.keys(value).sort()
    .filter(key => key !== 'userRejectedExtensions').map(key => [key, value[key]])));
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
        const headers = { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': cfg.apiKey };
        const api = async endpoint => {
          const response = await context.request.get(url + '/api/v1/' + endpoint, { headers, maxRedirects: 0 });
          if (response.status() !== 200) throw new Error('OwnedApiUnavailable');
          return response.json();
        };
        const original = await api('config/mediamanagement');
        check(name + '_initial_policy_is_empty', original.userRejectedExtensions === '');
        check(name + '_fresh_library_has_no_books_authors_or_roots',
          [await api('book'), await api('author'), await api('rootfolder')].every(items => Array.isArray(items) && items.length === 0));
        const page = await context.newPage();
        page.setDefaultTimeout(15000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        const anonymous = await context.request.get(url + '/api/v1/config/mediamanagement', { maxRedirects: 0 });
        check(name + '_anonymous_config_is_denied', [401, 302, 303].includes(anonymous.status()));
        await page.goto(url + '/settings/mediamanagement');
        await page.locator('input[name="username"]').fill(cfg.username);
        await page.locator('input[name="password"]').fill(cfg.password);
        await Promise.all([
          page.waitForURL(current => !current.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);
        check(name + '_forms_login_has_httponly_cookie', (await context.cookies()).some(cookie => cookie.httpOnly));
        const field = page.locator('input[name="userRejectedExtensions"]');
        const showField = async () => {
          await page.locator('#mediaManagementSettings').waitFor();
          if (!(await field.isVisible())) await page.getByTitle('Hidden, click to show', { exact: true }).click();
          await field.waitFor();
          await field.scrollIntoViewIfNeeded();
        };
        await showField();
        check(name + '_advanced_field_and_label_are_visible', await field.isVisible() &&
          await page.getByText('Rejected File Extensions', { exact: true }).isVisible());
        const save = async value => {
          await field.fill(value);
          const [response] = await Promise.all([
            page.waitForResponse(response => response.request().method() === 'PUT' &&
              new URL(response.url()).pathname.replace(/\/\d+$/, '') === base.pathname.replace(/\/$/, '') + '/api/v1/config/mediamanagement'),
            page.getByRole('button', { name: 'Save Changes', exact: true }).click()
          ]);
          return response;
        };
        const valid = await save(' .MP3, m4b, .mp3 ');
        check(name + '_valid_save_accepted', valid.status() === 202 || valid.status() === 200);
        await page.waitForFunction(() => document.querySelector('input[name="userRejectedExtensions"]')?.value === 'mp3, m4b');
        let saved = await api('config/mediamanagement');
        check(name + '_valid_policy_is_normalized_in_api_and_ui', saved.userRejectedExtensions === 'mp3, m4b' && await field.inputValue() === 'mp3, m4b');
        check(name + '_valid_save_keeps_other_settings', comparable(saved) === comparable(original));
        await page.screenshot({ path: path.join(output, name + '-rejected-extensions.png') });
        await page.reload();
        await showField();
        check(name + '_valid_policy_survives_reload', await field.inputValue() === 'mp3, m4b');
        const invalid = await save('../mp3');
        check(name + '_invalid_save_is_rejected', invalid.status() === 400);
        await page.getByText('Use comma-separated file extensions without paths or wildcards', { exact: true }).waitFor();
        check(name + '_validation_feedback_is_visible', await page.getByText('Use comma-separated file extensions without paths or wildcards', { exact: true }).isVisible());
        saved = await api('config/mediamanagement');
        check(name + '_invalid_save_keeps_prior_policy_and_settings', saved.userRejectedExtensions === 'mp3, m4b' && comparable(saved) === comparable(original));
        await page.screenshot({ path: path.join(output, name + '-rejected-extensions-invalid.png') });
        const cleared = await save('');
        check(name + '_explicit_clear_is_accepted', cleared.status() === 202 || cleared.status() === 200);
        saved = await api('config/mediamanagement');
        check(name + '_explicit_clear_keeps_other_settings', saved.userRejectedExtensions === '' && comparable(saved) === comparable(original));
        await page.reload();
        await showField();
        check(name + '_empty_policy_survives_reload', await field.inputValue() === '' && (await api('config/mediamanagement')).userRejectedExtensions === '');
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
  process.stdout.write(JSON.stringify({ checks, completed, ...(failureType ? { failureType } : {}), ...(failureCheck ? { failureCheck } : {}) }));
  process.exitCode = completed && Object.values(checks).every(Boolean) ? 0 : 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: { owned_browser_started: false }, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
