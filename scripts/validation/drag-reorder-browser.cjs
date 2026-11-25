// Run only against a private, synthetic loopback application.
const fs = require('fs');
const path = require('path');
const { reorderByPointer } = require('./react-compatibility-browser.cjs');

async function verify(config) {
  const base = new URL(config.url);
  const output = path.resolve(config.output);
  const directory = fs.lstatSync(output);
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port ||
      base.username || base.password || base.search || base.hash || !/^synthetic-/.test(config.username) ||
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
  const orders = {
    quality: config.qualityItems.map(item => item.id),
    delay: [...config.delayProfiles]
  };

  try {
    const phone = { ...devices['iPhone 13'], defaultBrowserType: undefined };
    for (const [layout, options] of [['desktop', { viewport: { width: 1280, height: 960 } }], ['touch', phone]]) {
      const context = await browser.newContext(options);
      const touch = layout === 'touch';
      let externalRequests = 0;
      const check = (name, accepted) => {
        checks[layout + '_' + name] = Boolean(accepted);
        if (!accepted) {
          failureCheck = layout + '_' + name;
          throw new Error('CheckFailed');
        }
      };
      try {
        await context.route('**/*', route => {
          const target = new URL(route.request().url());
          if (target.origin !== base.origin && !['data:', 'blob:'].includes(target.protocol)) {
            externalRequests++;
            return route.abort();
          }
          return route.continue();
        });
        const page = await context.newPage();
        page.setDefaultTimeout(12000);
        const pageErrors = [];
        page.on('pageerror', error => pageErrors.push(error.name));
        const api = async route => {
          const response = await context.request.get(config.url + '/api/v1/' + route, {
            headers: { 'X-Api-Key': config.apiKey, 'X-Requested-With': 'XMLHttpRequest' }
          });
          if (!response.ok()) throw new Error('FixtureApiReadFailed');
          return response.json();
        };
        const profileRoute = 'qualityprofile/' + config.qualityProfile.id;
        failureStep = layout + '_login';
        await page.goto(config.url + '/settings/profiles');
        await page.locator('input[name="username"]').fill(config.username);
        await page.locator('input[name="password"]').fill(config.password);
        await Promise.all([
          page.waitForURL(url => !url.pathname.endsWith('/login')),
          page.locator('#login-button').click()
        ]);

        failureStep = layout + '_open_quality';
        const card = page.locator('div:has(> [class*="underlay"])').filter({ has: page.getByText(config.qualityProfile.name, { exact: true }) });
        await card.locator(':scope > [class*="underlay"]').click();
        const modal = page.getByRole('dialog', { name: 'Edit Quality Profile' });
        await modal.waitFor();
        const rows = modal.locator('[class*="QualityProfileItemDragSource-qualityProfileItemDragSource"]');
        const two = orders.quality.slice(-2).reverse().map(id => config.qualityItems.find(item => item.id === id));
        const row = item => rows.filter({ has: page.getByText(item.name, { exact: true }) });
        const first = row(two[0]);
        const second = row(two[1]);
        await first.waitFor();
        await second.waitFor();
        const handle = first.locator('[class*="-dragHandle-"]');
        check('quality_source_has_one_dom_handle', await handle.count() === 1);

        failureStep = layout + '_quality_drag';
        await reorderByPointer(page, handle, second, touch);
        // The saved ascending profile list swaps the two highest items when
        // the highest visual row is moved one row lower.
        const expected = [...orders.quality.slice(0, -2), ...orders.quality.slice(-2).reverse()];
        // The real check is persisted API order; also prove that the visual
        // placement moved before save, without relying on any preview tree.
        await page.waitForFunction(({ first, second }) =>
          Boolean(first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_PRECEDING),
        { first: await first.elementHandle(), second: await second.elementHandle() });
        check('quality_visual_reorder', await first.evaluate((node, other) =>
          Boolean(node.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_PRECEDING), await second.elementHandle()));

        failureStep = layout + '_quality_save';
        const [saved] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'PUT' && new URL(response.url()).pathname.endsWith('/' + profileRoute)),
          modal.getByRole('button', { name: 'Save', exact: true }).click()
        ]);
        check('quality_save_sends_order', saved.status() === 202 &&
          JSON.stringify(saved.request().postDataJSON().items.map(item => item.quality?.id ?? item.id)) === JSON.stringify(expected));
        await modal.waitFor({ state: 'hidden' });
        check('quality_order_persists', JSON.stringify((await api(profileRoute)).items.map(item => item.quality?.id ?? item.id)) === JSON.stringify(expected));
        orders.quality = expected;

        failureStep = layout + '_delay_reorder';
        // The existing delay grid overflows narrow phones. Test native touch
        // input using a landscape viewport in the touch context.
        if (touch) await page.setViewportSize({ width: 1280, height: 960 });
        const delayRows = page.locator('[class*="DelayProfileDragSource-delayProfileDragSource"]');
        const delayRow = profile => delayRows.filter({ has: page.getByText(profile.label, { exact: true }) });
        const source = delayRow(orders.delay[0]);
        const target = delayRow(orders.delay[1]);
        await source.waitFor();
        await target.waitFor();
        const delayHandle = source.locator('[class*="DelayProfile-dragHandle-"]');
        const route = '/delayprofile/reorder/' + orders.delay[0].id;
        const [response] = await Promise.all([
          page.waitForResponse(response => response.request().method() === 'PUT' && new URL(response.url()).pathname.endsWith(route)),
          reorderByPointer(page, delayHandle, target, touch)
        ]);
        check('delay_reorder_sends_after_id', response.ok() && new URL(response.url()).searchParams.get('afterId') === String(orders.delay[1].id));
        const reversed = [...orders.delay].reverse();
        await page.waitForFunction(({ first, second }) =>
          Boolean(first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_PRECEDING),
        { first: await source.elementHandle(), second: await target.elementHandle() });
        const persisted = (await api('delayprofile')).filter(profile => profile.id !== 1).sort((a, b) => a.order - b.order).map(profile => profile.id);
        check('delay_order_persists', JSON.stringify(persisted) === JSON.stringify(reversed.map(profile => profile.id)));
        orders.delay = reversed;
        check('no_external_requests_or_page_errors', externalRequests === 0 && pageErrors.length === 0);
        await page.screenshot({ path: path.join(output, layout + '-delay-reorder.png'), fullPage: true });
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
  return { checks, completed, failureStep, failureCheck, failureType };
}

(async () => {
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (input.length > 16384) throw new Error('InvalidInput');
  }
  const result = await verify(JSON.parse(input));
  process.stdout.write(JSON.stringify(result));
  if (!result.completed) process.exitCode = 1;
})().catch(error => {
  process.stdout.write(JSON.stringify({ checks: {}, completed: false, failureType: error.name }));
  process.exitCode = 1;
});
