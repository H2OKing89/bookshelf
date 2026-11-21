// Run against a new, owned loopback home. Synthetic credentials arrive on stdin.
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
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port ||
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
  const apiUrl = url + '/api/v1/';
  const headers = { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': cfg.apiKey };
  const checks = {};
  let failureCheck;
  let failureType;
  let step = 'launch';
  let browser;
  let completed = false;
  const check = (name, condition) => {
    checks[name] = Boolean(condition);
    if (!condition) {
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
        const request = async (endpoint, method = 'GET', data, status = 200) => {
          const response = await context.request.fetch(apiUrl + endpoint, { headers, method, data, maxRedirects: 0 });
          check(name + '_' + step + '_' + method + '_' + endpoint.replaceAll('/', '_') + '_' + status, response.status() === status);
          const body = await response.text();
          return body ? JSON.parse(body) : null;
        };
        step = 'schema';
        const formats = await request('customformat/schema');
        const language = formats.find(item => item.implementation === 'LanguageSpecification');
        const choices = language?.fields.find(field => field.name === 'value')?.selectOptions ?? [];
        check(name + '_language_schema_offers_english_french_unknown_and_selected_edition',
          [-2, 0, 1, 2].every(value => choices.some(option => Number(option.value) === value)) &&
          choices.some(option => Number(option.value) === -2 && /selected edition/i.test(option.name)));
        step = 'create_language_format';
        const formatName = `Synthetic ${name} language format`;
        const conditionName = `Synthetic ${name} English`;
        const created = await request('customformat', 'POST', {
          name: formatName, includeCustomFormatWhenRenaming: false,
          specifications: [{ ...language, name: conditionName, required: true, fields: language.fields.map(field =>
            field.name === 'value' ? { ...field, value: 1 } : field) }]
        }, 201);
        check(name + '_saved_language_round_trips', (await request('customformat/' + created.id)).specifications.some(spec =>
          spec.implementation === 'LanguageSpecification' && spec.fields.some(field => field.name === 'value' && Number(field.value) === 1)));
        const page = await context.newPage();
        page.setDefaultTimeout(15000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        step = 'login';
        await page.goto(url + '/settings/profiles');
        await page.locator('input[name="username"]').fill(cfg.username);
        await page.locator('input[name="password"]').fill(cfg.password);
        await Promise.all([
          page.waitForURL(current => !current.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);
        const openCard = async (title, scope = page) => {
          const card = scope.locator('div:has(> [class*="underlay"])').filter({ has: page.getByText(title, { exact: true }) });
          await card.locator(':scope > [class*="underlay"]').click();
        };
        step = 'profile_setup';
        const profile = (await request('qualityprofile')).find(item => item.id === cfg.profileId);
        const profilePath = 'qualityprofile/' + profile.id;
        check(name + '_profile_seeded_before_upgrade_has_zero_threshold', profile.minUpgradeFormatScore === 0);
        await request(profilePath, 'PUT', { ...profile, upgradeAllowed: true }, 202);
        const threshold = page.locator('input[name="minUpgradeFormatScore"]');
        const openProfile = async () => {
          await page.goto(url + '/settings/profiles');
          step = 'profile_open_card';
          await openCard(profile.name);
          step = 'profile_threshold_visible';
          await threshold.waitFor({ state: 'visible' });
        };
        step = 'profile_browser_save';
        await openProfile();
        step = 'profile_fill_and_save';
        await threshold.fill('10');
        await threshold.scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, name + '-score.png'), fullPage: true });
        const [saveScore] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'PUT' && new URL(response.url()).pathname.endsWith('/' + profilePath)),
          page.getByRole('button', { name: 'Save', exact: true }).click()
        ]);
        check(name + '_profile_browser_saves_threshold', saveScore.status() === 202 && saveScore.request().postDataJSON().minUpgradeFormatScore === 10);
        step = 'profile_legacy_and_clear';
        const current = await request(profilePath);
        const { minUpgradeFormatScore, ...legacyProfile } = current;
        check(name + '_threshold_persists_after_browser_save', minUpgradeFormatScore === 10);
        await request(profilePath, 'PUT', legacyProfile, 202);
        check(name + '_omitted_threshold_preserves_existing_value', (await request(profilePath)).minUpgradeFormatScore === 10);
        await request(profilePath, 'PUT', { ...legacyProfile, minUpgradeFormatScore: -1 }, 400);
        check(name + '_invalid_threshold_preserves_existing_value', (await request(profilePath)).minUpgradeFormatScore === 10);
        await request(profilePath, 'PUT', { ...legacyProfile, minUpgradeFormatScore: 0 }, 202);
        check(name + '_explicit_zero_clears_threshold', (await request(profilePath)).minUpgradeFormatScore === 0);
        step = 'language_browser';
        await page.goto(url + '/settings/customformats');
        await openCard(formatName);
        step = 'language_condition';
        const formatModal = page.locator('div[class*="modalContent"]').filter({ has: page.getByText('Edit Custom Format', { exact: true }) });
        await openCard(conditionName, formatModal);
        const english = page.getByText('English', { exact: true }).first();
        await english.waitFor({ state: 'visible' });
        check(name + '_saved_language_condition_opens_with_english', await english.isVisible());
        await page.screenshot({ path: path.join(output, name + '-language.png'), fullPage: true });
        step = 'space_browser_save';
        const spacePath = 'config/mediamanagement';
        const original = await request(spacePath);
        check(name + '_pre_grab_space_check_defaults_off', original.checkFreeSpaceWhenGrabbing === false);
        await page.goto(url + '/settings/mediamanagement');
        const advanced = page.locator('button[title="Hidden, click to show"], button[title="Shown, click to hide"]').first();
        await advanced.waitFor({ state: 'visible' });
        if (await advanced.getAttribute('title') === 'Hidden, click to show') await advanced.click();
        const space = page.locator('input[name="checkFreeSpaceWhenGrabbing"]');
        await space.waitFor({ state: 'visible' });
        await space.scrollIntoViewIfNeeded();
        await space.locator('..').click();
        await page.screenshot({ path: path.join(output, name + '-space.png'), fullPage: true });
        const [saveSpace] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'PUT' &&
            new URL(response.url()).pathname.replace(/\/\d+$/, '').endsWith('/' + spacePath)),
          page.getByRole('button', { name: 'Save Changes', exact: true }).click()
        ]);
        check(name + '_browser_enables_pre_grab_check', saveSpace.status() === 202 && saveSpace.request().postDataJSON().checkFreeSpaceWhenGrabbing === true);
        step = 'space_legacy_and_clear';
        const updated = await request(spacePath);
        const { checkFreeSpaceWhenGrabbing, ...legacySettings } = updated;
        check(name + '_pre_grab_saved_independently_of_import_skip', checkFreeSpaceWhenGrabbing === true &&
          updated.skipFreeSpaceCheckWhenImporting === original.skipFreeSpaceCheckWhenImporting &&
          updated.minimumFreeSpaceWhenImporting === original.minimumFreeSpaceWhenImporting);
        await request(spacePath + '/1', 'PUT', legacySettings, 202);
        check(name + '_omitted_pre_grab_policy_preserves_value', (await request(spacePath)).checkFreeSpaceWhenGrabbing === true);
        await request(spacePath + '/1', 'PUT', { ...legacySettings, checkFreeSpaceWhenGrabbing: false }, 202);
        check(name + '_explicit_false_disables_check', (await request(spacePath)).checkFreeSpaceWhenGrabbing === false);
        check(name + '_no_browser_errors_or_external_requests', errors.length === 0 && externalRequests === 0);
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
