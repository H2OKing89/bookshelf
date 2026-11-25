const fs = require('fs');
const path = require('path');
const { setCheckbox } = require('./operational-paging-browser.cjs');

const rowSelector = '[class*="VirtualTableRow-row-"]';

async function scrollPosition(page) {
  return page.evaluate(() => {
    const grid = document.querySelector('.ReactVirtualized__Grid');
    let scroller = grid?.parentElement;
    while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
    if (!scroller || scroller.scrollHeight <= scroller.clientHeight + 100) scroller = document.scrollingElement;
    return scroller.scrollTop;
  });
}

async function scrollToFraction(page, fraction) {
  await page.evaluate(fraction => {
    const grid = document.querySelector('.ReactVirtualized__Grid');
    let scroller = grid?.parentElement;
    while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
    if (!scroller || scroller.scrollHeight <= scroller.clientHeight + 100) scroller = document.scrollingElement;
    scroller.scrollTop = Math.round((scroller.scrollHeight - scroller.clientHeight) * fraction);
  }, fraction);
  await page.waitForTimeout(150);
}

async function benchmark(page) {
  return page.evaluate(async selector => {
    const grid = document.querySelector('.ReactVirtualized__Grid');
    let scroller = grid?.parentElement;
    while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
    if (!scroller || scroller.scrollHeight <= scroller.clientHeight + 100) scroller = document.scrollingElement;
    const frame = () => new Promise(resolve => requestAnimationFrame(resolve));
    const samples = [];
    let peakRows = document.querySelectorAll(selector).length;
    const distance = scroller.scrollHeight - scroller.clientHeight;
    let previous = await frame();
    for (let index = 0; index < 36; index++) {
      scroller.scrollTop = Math.round(distance * ((index + 1) / 36));
      const now = await frame();
      samples.push(now - previous);
      previous = now;
      peakRows = Math.max(peakRows, document.querySelectorAll(selector).length);
    }
    await frame();
    const endRows = document.querySelectorAll(selector).length;
    const endVisible = [...document.querySelectorAll(selector)].some(row => {
      const rect = row.getBoundingClientRect();
      return rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight;
    });
    scroller.scrollTop = 0;
    await frame();
    await frame();
    samples.sort((left, right) => left - right);
    return {
      peakRows, endRows, endVisible, scrollDistance: distance,
      frames: samples.length, medianFrameMs: Number(samples[Math.floor(samples.length / 2)].toFixed(1)),
      p95FrameMs: Number(samples[Math.ceil(samples.length * 0.95) - 1].toFixed(1)),
      maxFrameMs: Number(samples.at(-1).toFixed(1))
    };
  }, rowSelector);
}

async function visibleLink(page, route, selector = rowSelector) {
  const href = await page.evaluate(({ selector, route }) => {
    const grid = document.querySelector('.ReactVirtualized__Grid');
    let scroller = grid?.parentElement;
    while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
    const clip = scroller?.getBoundingClientRect();
    const top = Math.max(0, clip?.top ?? 0);
    const bottom = Math.min(window.innerHeight, clip?.bottom ?? window.innerHeight);
    const links = [...document.querySelectorAll(selector + ' a[href*="/' + route + '/"]')];
    const candidates = links.map(link => {
      const rect = link.getBoundingClientRect();
      return { href: link.getAttribute('href'), distance: Math.abs((rect.top + rect.bottom - top - bottom) / 2) };
    });
    return candidates.sort((left, right) => left.distance - right.distance)[0]?.href;
  }, { selector, route });
  return href && page.locator(selector + ' a[href="' + href + '"]').first();
}

async function verifyAdditionalView(page, config, layout, section, route, view, check, stage) {
  const preference = section === 'authors' ? 'authorIndex' : 'bookIndex';
  const cssPrefix = (section === 'authors' ? 'Author' : 'Book') + 'Index' +
    (view === 'posters' ? 'Posters' : 'Overviews') + '-grid-';
  const gridSelector = `.ReactVirtualized__Grid[class*="${cssPrefix}"]`;
  const grid = page.locator(gridSelector);
  // Apply the desired layout before the next application bootstrap, after
  // the outgoing page has finished its ordinary preference persistence.
  await page.evaluate(({ preference, view }) => {
    sessionStorage.setItem('owned-library-next-view', JSON.stringify({ preference, view }));
  }, { preference, view });
  stage('load');
  await page.goto(config.url + '/' + section);
  await grid.locator(`a[href*="/${route}/"]`).first().waitFor();
  await scrollToFraction(page, 0);
  const distance = await page.evaluate(() => {
    const grid = document.querySelector('.ReactVirtualized__Grid');
    let scroller = grid?.parentElement;
    while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
    if (!scroller || scroller.scrollHeight <= scroller.clientHeight + 100) scroller = document.scrollingElement;
    return scroller.scrollHeight - scroller.clientHeight;
  });
  const name = section + '_' + view;
  const endPrefix = section === 'authors' ? '/owned-author-15' : '/owned-recording-14';
  if (layout === 'desktop') {
    const jumpBar = page.locator('[class*="PageJumpBar-jumpBar-"]');
    const z = jumpBar.getByRole('button', { name: 'Z', exact: true });
    const a = jumpBar.getByRole('button', { name: 'A', exact: true });
    stage('end_jump');
    await z.click();
    await grid.locator(`a[href*="${endPrefix}"]`).first().waitFor();
    check(name + '_jump_to_end', await scrollPosition(page) > distance * 0.8);
    stage('top_jump');
    await a.click();
    const startPrefix = section === 'authors' ? '/owned-author-100' : '/owned-recording-1000';
    await grid.locator(`a[href*="${startPrefix}"]`).first().waitFor();
    check(name + '_jump_to_top', await scrollPosition(page) <= 76);
    await z.click();
    await grid.locator(`a[href*="${endPrefix}"]`).first().waitFor();
  } else {
    stage('native_scroll');
    await scrollToFraction(page, 0.95);
    const firstNearEnd = section === 'authors' ? 1450 : 13800;
    await page.waitForFunction(({ gridSelector, route, firstNearEnd }) =>
      [...document.querySelectorAll(gridSelector + ' a[href*="/' + route + '/"]')].some(link => {
        const id = Number(link.getAttribute('href').split('-').at(-1));
        const rect = link.getBoundingClientRect();
        return id >= firstNearEnd && rect.height > 0 && rect.bottom > 0 && rect.top < innerHeight;
      }), { gridSelector, route, firstNearEnd });
    check(name + '_native_phone_scroll_reaches_near_end', await scrollPosition(page) > distance * 0.8);
  }

  stage('navigate');
  const link = await visibleLink(page, route, gridSelector);
  if (!link) throw new Error('OwnedVisibleLibraryLinkMissing');
  const href = await link.getAttribute('href');
  await page.evaluate(href => {
    history.scrollRestoration = 'manual';
    window.ownedLibraryScrollBefore = undefined;
    document.addEventListener('click', function record(event) {
      if (event.target.closest('a')?.getAttribute('href') !== href) return;
      const grid = document.querySelector('.ReactVirtualized__Grid');
      let scroller = grid?.parentElement;
      while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
      if (!scroller || scroller.scrollHeight <= scroller.clientHeight + 100) scroller = document.scrollingElement;
      window.ownedLibraryScrollBefore = scroller.scrollTop;
      document.removeEventListener('click', record, true);
    }, true);
  }, href);
  await link.click();
  await page.waitForURL(url => url.pathname === href);
  const before = await page.evaluate(() => window.ownedLibraryScrollBefore);
  await grid.waitFor({ state: 'detached' });
  stage('back');
  await page.goBack();
  await page.waitForURL(url => url.pathname.endsWith('/' + section));
  await grid.locator(`a[href*="/${route}/"]`).first().waitFor();
  await page.waitForTimeout(350);
  const after = await scrollPosition(page);
  check(name + '_back_restores_scroll', Number.isFinite(before) && Math.abs(before - after) <= 76);
}

async function run(config) {
  const base = new URL(config.url);
  const output = path.resolve(config.output);
  const directory = fs.lstatSync(output);
  if (base.protocol !== 'http:' || base.hostname !== '127.0.0.1' || !base.port || base.username || base.password ||
      base.search || base.hash || !/^synthetic-/.test(config.username) || !directory.isDirectory() ||
      directory.isSymbolicLink() || (directory.mode & 0o077) || fs.readdirSync(output).length) {
    throw new Error('OwnedLoopbackFixtureRequired');
  }

  const { chromium, devices } = require(config.playwrightModule);
  const browser = await chromium.launch({ headless: true });
  const checks = {};
  const metrics = {};
  let completed = false;
  let failureType;
  let failureStep;
  let failureCheck;
  try {
    const { defaultBrowserType, ...phone } = devices['iPhone 13'];
    for (const [layout, options] of [['desktop', { viewport: { width: 1280, height: 960 } }], ['phone', phone]]) {
      const context = await browser.newContext(options);
      try {
        let unexpectedRequests = 0;
        await context.route('**/*', route => {
          const request = route.request();
          const destination = new URL(request.url());
          if ((destination.origin !== base.origin && !['data:', 'blob:'].includes(destination.protocol)) ||
              (destination.pathname.includes('/api/v1/') && !['GET', 'HEAD'].includes(request.method()))) {
            unexpectedRequests++;
            return route.abort();
          }
          return route.continue();
        });
        await context.addInitScript(() => {
          const nextView = sessionStorage.getItem('owned-library-next-view');
          if (nextView) {
            const { preference, view } = JSON.parse(nextView);
            const state = JSON.parse(localStorage.getItem('readarr') || '{}');
            state[preference] = { ...state[preference], view };
            localStorage.setItem('readarr', JSON.stringify(state));
            sessionStorage.removeItem('owned-library-next-view');
            return;
          }
          if (sessionStorage.getItem('owned-library-viewport-seeded')) return;
          sessionStorage.setItem('owned-library-viewport-seeded', '1');
          localStorage.setItem('readarr', JSON.stringify({
            authorIndex: { view: 'table' }, bookIndex: { view: 'table' }
          }));
        });
        const page = await context.newPage();
        page.setDefaultTimeout(12000);
        const errors = [];
        page.on('pageerror', error => errors.push(error.name));
        failureStep = layout + '_login';
        await page.goto(config.url + '/authors');
        await page.locator('input[name="username"]').fill(config.username);
        await page.locator('input[name="password"]').fill(config.password);
        await Promise.all([page.waitForURL(url => url.pathname.endsWith('/authors')), page.locator('#login-button').click()]);

        const check = (key, condition) => {
          const label = layout + '_' + key;
          checks[label] = Boolean(condition);
          if (!condition) failureCheck = label;
        };

        for (const [section, route, total] of [
          ['authors', 'author', config.sizes.authors], ['books', 'book', config.sizes.books]
        ]) {
          failureStep = layout + '_' + section + '_load';
          await page.goto(config.url + '/' + section);
          const rows = page.locator(rowSelector);
          await rows.first().waitFor({ state: 'visible' });
          await page.locator('.ReactVirtualized__Grid').waitFor();
          check(section + '_initial_rows_bounded', (await rows.count()) > 0 && (await rows.count()) < 80);
          if (section === 'books') {
            const first = rows.locator('a[href$="/book/owned-recording-10000"]');
            const second = rows.locator('a[href$="/book/owned-recording-10001"]');
            await first.waitFor();
            check('alternate_recordings_are_distinct_links', await second.count() === 1 &&
              await first.getAttribute('href') !== await second.getAttribute('href'));
          }
          const measurement = await benchmark(page);
          metrics[layout + '_' + section] = { total, ...measurement };
          check(section + '_scroll_keeps_bounded_nonempty_dom',
            measurement.peakRows < 80 && measurement.endRows > 0 && measurement.endVisible && measurement.scrollDistance > 1000);

          failureStep = layout + '_' + section + '_jump';
          const jump = page.locator('[class*="PageJumpBar-jumpBar-"]').getByRole('button', { name: 'Z', exact: true });
          if (layout === 'desktop') {
            await jump.waitFor();
            await jump.click();
            await page.waitForFunction(({ selector }) =>
              [...document.querySelectorAll(selector)].some(row => row.innerText.includes('Z Recording') || row.innerText.includes('Z Author')),
            { selector: rowSelector });
            check(section + '_jump_to_end_preserves_virtual_rows', await scrollPosition(page) > measurement.scrollDistance * 0.8);
          } else {
            await scrollToFraction(page, 0.95);
            check(section + '_native_phone_scroll_reaches_near_end', await scrollPosition(page) > measurement.scrollDistance * 0.8);
          }

          failureStep = layout + '_' + section + '_back';
          const link = await visibleLink(page, route);
          if (!link) throw new Error('OwnedVisibleLibraryLinkMissing');
          const href = await link.getAttribute('href');
          // Capture the offset on click, after any browser scroll needed to bring
          // the anchor into view. Test the app's own restoration independently
          // of the browser's history restoration.
          await page.evaluate(href => {
            history.scrollRestoration = 'manual';
            document.addEventListener('click', function record(event) {
              if (event.target.closest('a')?.getAttribute('href') !== href) return;
              const grid = document.querySelector('.ReactVirtualized__Grid');
              let scroller = grid?.parentElement;
              while (scroller && !['auto', 'scroll'].includes(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement;
              if (!scroller || scroller.scrollHeight <= scroller.clientHeight + 100) scroller = document.scrollingElement;
              window.ownedLibraryScrollBefore = scroller.scrollTop;
              document.removeEventListener('click', record, true);
            }, true);
          }, href);
          await link.click();
          await page.waitForURL(url => url.pathname === href);
          const before = await page.evaluate(() => window.ownedLibraryScrollBefore);
          await rows.first().waitFor({ state: 'detached' });
          await page.waitForTimeout(150);
          await page.goBack();
          await page.waitForURL(url => url.pathname.endsWith('/' + section));
          await rows.first().waitFor({ state: 'visible' });
          await page.waitForTimeout(350);
          const after = await scrollPosition(page);
          metrics[layout + '_' + section].back = { before, after };
          check(section + '_back_restores_scroll', Number.isFinite(before) && Math.abs(before - after) <= 76);

          if (layout === 'desktop') {
            failureStep = layout + '_' + section + '_editor';
            const editor = page.getByRole('button', { name: route === 'author' ? 'Author Editor' : 'Book Editor', exact: true });
            await editor.click();
            const selectAll = page.getByRole('button', { name: 'Select All', exact: true });
            await selectAll.click();
            const selected = section === 'authors' ? 'Author(s) Selected' : 'Book(s) Selected';
            await page.getByText(total + ' ' + selected, { exact: true }).waitFor();
            const selectedBefore = await rows.locator('input[type="checkbox"]').first().isChecked();
            await jump.click();
            await page.waitForTimeout(150);
            check(section + '_select_all_keeps_offscreen_items_selected', selectedBefore &&
              await rows.locator('input[type="checkbox"]').first().isChecked());
            await page.getByRole('button', { name: 'Unselect All', exact: true }).click();
            await page.getByRole('button', { name: route === 'author' ? 'Author Index' : 'Book Index', exact: true }).click();
            if (section === 'books') {
              failureStep = layout + '_' + section + '_columns_sort';
              await scrollToFraction(page, 0);
              const header = page.locator('[class*="BookIndexHeader-title-"]');
              await header.click();
              await page.waitForFunction(selector => document.querySelector(selector)?.innerText.includes('Z Recording'), rowSelector);
              await header.click();
              await rows.locator('a[href$="/book/owned-recording-10000"]').waitFor();
              check('sorting_recovers_exact_recording_rows', await rows.count() < 80);
              await page.getByRole('button', { name: 'Table Options', exact: true }).first().click();
              const options = page.getByRole('dialog', { name: 'Table Options', exact: true });
              await setCheckbox(options.locator('input[name="runtimeLengthMinutes"]'), true);
              await page.keyboard.press('Escape');
              check('column_change_keeps_virtualized_rows', await page.locator('[class*="BookIndexHeader-runtimeLengthMinutes-"]').isVisible() &&
                await rows.count() < 80);
            }
          }

          failureStep = layout + '_' + section + '_resize';
          const width = (await page.viewportSize()).width;
          // A phone context has a mobile media query even when the viewport changes.
          await page.setViewportSize({ width: width - 40, height: (await page.viewportSize()).height });
          await page.waitForTimeout(200);
          check(section + '_resized_rows_bounded', (await rows.count()) > 0 && (await rows.count()) < 80);
          await page.setViewportSize({ width, height: (await page.viewportSize()).height });
        }
        for (const [section, route] of [['authors', 'author'], ['books', 'book']]) {
          for (const view of ['posters', 'overview']) {
            failureStep = layout + '_' + section + '_' + view;
            await verifyAdditionalView(page, config, layout, section, route, view, check, phase => {
              failureStep = layout + '_' + section + '_' + view + '_' + phase;
            });
          }
        }
        failureStep = layout + '_ebook_details';
        await page.goto(config.url + '/book/owned-recording-10007');
        await page.getByText('321 pages', { exact: true }).waitFor();
        check('ebook_detail_retains_pages', await page.getByText('321 pages', { exact: true }).isVisible());
        check('no_unexpected_requests_or_page_errors', unexpectedRequests === 0 && errors.length === 0);
        await page.screenshot({ path: path.join(output, layout + '-library-table.png') });
      } finally {
        await context.close();
      }
    }
    completed = Object.values(checks).every(Boolean);
  } catch (error) {
    failureType = error.name;
  } finally {
    await browser.close();
  }
  return { checks, metrics, completed, failureCheck, failureType, failureStep: completed ? undefined : failureStep };
}

(async () => {
  try {
    const config = JSON.parse(fs.readFileSync(process.stdin.fd, 'utf8'));
    const result = await run(config);
    process.stdout.write(JSON.stringify(result));
    process.exitCode = result.completed ? 0 : 1;
  } catch (error) {
    process.stdout.write(JSON.stringify({ checks: { browser_started: false }, completed: false, failureType: error.name }));
    process.exitCode = 1;
  }
})();
