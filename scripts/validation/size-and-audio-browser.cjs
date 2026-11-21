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
  const api = url + '/api/v1/';
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
  const leaves = items => items.flatMap(item => (item.quality ? [item] : leaves(item.items)));
  const m4b = profile => leaves(profile.items).find(item => item.quality.id === 12);
  const limits = ['minSize', 'preferredSize', 'maxSize'];
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
        const request = async (endpoint, method = 'GET', data, expected = 200) => {
          const response = await context.request.fetch(api + endpoint, { headers, method, data, maxRedirects: 0 });
          if (response.status() !== expected) throw new Error(`OwnedApiUnexpectedStatus${response.status()}`);
          const body = await response.text();
          return body ? JSON.parse(body) : null;
        };
        step = name + '_schema';
        const schema = await request('customformat/schema');
        const codec = schema.find(item => item.implementation === 'AudioCodecSpecification');
        const choices = codec?.fields.find(field => field.name === 'value')?.selectOptions ?? [];
        check(name + '_supported_codec_choices_are_distinct', [0, 2, 3, 4, 5, 6, 8, 9, 12, 13, 19, 20].every(value =>
          choices.some(choice => Number(choice.value) === value)));
        check(name + '_other_audio_condition_templates_are_available', ['AudioChannelsSpecification', 'AudioBitrateModeSpecification',
          'AudioSampleRateSpecification', 'NarratorSpecification'].every(type => schema.some(item => item.implementation === type)));
        step = name + '_custom_format';
        const formatName = `Synthetic ${name} audio format`;
        const conditionName = `Synthetic ${name} codec condition`;
        const created = await request('customformat', 'POST', {
          name: formatName, includeCustomFormatWhenRenaming: false,
          specifications: [{ ...codec, name: conditionName, required: true, fields: codec.fields.map(field =>
            field.name === 'value' ? { ...field, value: 5 } : field) }]
        }, 201);
        check(name + '_codec_condition_round_trips', (await request('customformat/' + created.id)).specifications.some(item =>
          item.implementation === 'AudioCodecSpecification' && item.fields.some(field => field.name === 'value' && Number(field.value) === 5)));
        step = name + '_login';
        const page = await context.newPage();
        page.setDefaultTimeout(15000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
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
        const profilePath = 'qualityprofile/' + cfg.profileId;
        const profile = await request(profilePath);
        check(name + '_old_profile_has_no_active_rules', limits.every(field => !m4b(profile)[field]));
        check(name + '_audio_and_ebook_units_are_distinct', m4b(profile).sizeUnit === 'MiB/hour' &&
          leaves(profile.items).find(item => item.quality.id === 3)?.sizeUnit === 'MiB');
        const openSizeRules = async () => {
          await page.goto(url + '/settings/profiles');
          await openCard(profile.name);
          await page.getByText('Release Size Rules', { exact: true }).click();
          await page.locator('select[name="profileSizeQuality"]').selectOption('12');
        };
        const saveProfile = () => Promise.all([
          page.waitForResponse(response => response.request().method() === 'PUT' && new URL(response.url()).pathname.endsWith('/' + profilePath)),
          page.getByRole('button', { name: 'Save', exact: true }).click()
        ]);
        step = name + '_profile_form';
        await openSizeRules();
        await page.getByLabel('Minimum', { exact: true }).fill('10.5');
        await page.getByLabel('Preferred', { exact: true }).fill('55.25');
        await page.getByLabel('Maximum', { exact: true }).fill('90.5');
        await page.locator('input[name="releaseSize-preferredSize-12"]').scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, name + '-size-rules.png'), fullPage: true });
        const [saved] = await saveProfile();
        check(name + '_browser_saves_decimal_rules', saved.status() === 202 && m4b(saved.request().postDataJSON()).preferredSize === 55.25);
        const current = await request(profilePath);
        check(name + '_mapped_size_fields_round_trip', limits.every((field, i) => m4b(current)[field] === [10.5, 55.25, 90.5][i]));
        step = name + '_space_rules_legacy_and_invalid';
        for (const field of limits) delete m4b(current)[field];
        await request(profilePath, 'PUT', current, 202);
        check(name + '_legacy_omission_keeps_rules', m4b(await request(profilePath)).preferredSize === 55.25);
        m4b(current).maxSize = 40;
        await request(profilePath, 'PUT', current, 400);
        check(name + '_invalid_partial_update_preserves_maximum', m4b(await request(profilePath)).maxSize === 90.5);
        step = name + '_clear_rule';
        await openSizeRules();
        for (const label of ['Minimum', 'Preferred', 'Maximum']) await page.getByLabel(label, { exact: true }).fill('');
        const [cleared] = await saveProfile();
        check(name + '_clearing_fields_disables_rules', cleared.status() === 202 && limits.every(field => m4b(cleared.request().postDataJSON())[field] === 0));
        const clearedProfile = m4b(await request(profilePath));
        check(name + '_cleared_rules_stay_disabled', limits.every(field => clearedProfile[field] === 0));
        step = name + '_audio_editor';
        await page.goto(url + '/settings/customformats');
        await openCard(formatName);
        const formatModal = page.locator('div[class*="modalContent"]').filter({ has: page.getByText('Edit Custom Format', { exact: true }) });
        await openCard(conditionName, formatModal);
        await page.getByText('Audio Codec', { exact: true }).last().waitFor({ state: 'visible' });
        const currentCodec = page.getByText('xHE-AAC', { exact: true }).last();
        await currentCodec.waitFor({ state: 'visible' });
        check(name + '_audio_condition_editor_shows_selected_codec', await currentCodec.isVisible());
        await page.screenshot({ path: path.join(output, name + '-audio-codec.png'), fullPage: true });
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
