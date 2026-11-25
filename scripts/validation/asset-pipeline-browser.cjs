const path = require('path');
const { report, runBrowser } = require('./history-paging-browser.cjs');

async function linkedAssets(page, base, signedIn) {
  const prefix = base.pathname.replace(/\/$/, '');
  const links = await page.locator('script[src], link[href]').evaluateAll(elements => elements.map(element => ({
    url: element.src || element.href,
    kind: element.tagName === 'SCRIPT' ? 'script' : element.rel
  })));
  if (!links.length) return false;

  for (const link of links) {
    const url = new URL(link.url);
    if (url.origin !== base.origin || !url.pathname.startsWith(prefix + '/')) {
      console.error('Asset outside owned deployment base:', url.pathname);
      return false;
    }
    const response = await page.request.get(url.href, { maxRedirects: 0 });
    // The existing unauthenticated favicon route redirects to login. Its
    // actual file is verified again after authentication, alongside the
    // public PNG icons and other assets.
    if (!signedIn && url.pathname === `${prefix}/favicon.ico` && response.status() === 302) {
      const location = new URL(response.headers().location, base);
      if (location.origin === base.origin && location.pathname === `${prefix}/login`) continue;
    }
    const type = response.headers()['content-type'] || '';
    const validType = !type.includes('text/html') &&
      (link.kind !== 'script' || /javascript|ecmascript/.test(type)) &&
      (link.kind !== 'stylesheet' || type.includes('text/css')) &&
      (link.kind !== 'manifest' || type.includes('json'));
    if (!response.ok() || !validType) {
      console.error('Asset response:', url.pathname, response.status(), type);
      return false;
    }
  }
  return true;
}

async function verifyAssets(page, base, config, { check, step, output }) {
  const prefix = base.pathname.replace(/\/$/, '');
  const resources = [];
  page.on('response', response => {
    const request = response.request();
    const url = new URL(request.url());
    const type = request.resourceType();
    if (['script', 'stylesheet', 'image', 'font'].includes(type) && url.protocol === 'http:') {
      resources.push({ url, type, ok: response.ok() || response.status() === 304 });
    }
  });

  step('login_assets');
  const requested = `${config.url}/book/${config.recording.slug}`;
  await page.goto(requested);
  await page.waitForURL(url => url.pathname === `${prefix}/login`);
  check('login_links_resolve_under_base', await linkedAssets(page, base, false));
  await page.locator('input[name="username"]').fill(config.username);
  await page.locator('input[name="password"]').fill(config.password);
  await Promise.all([page.waitForURL(requested), page.locator('#login-button').click()]);

  step('packaged_entry_and_lazy_bootstrap');
  await page.getByText(config.recording.title, { exact: true }).first().waitFor();
  check('index_links_resolve_under_base', await linkedAssets(page, base, true));
  const entry = await page.locator('script[src]').evaluateAll(elements => elements.map(element => new URL(element.src).pathname));
  check('entry_script_is_content_hashed', entry.some(name => /[-.][A-Za-z0-9_-]{8,}\.js$/.test(name)));
  check('bootstrap_and_entry_scripts_loaded', new Set(resources.filter(item => item.type === 'script' && item.ok).map(item => item.url.pathname)).size >= 2);
  check('runtime_base_initialized', await page.evaluate(value => window.Readarr.urlBase === value, prefix));

  step('fonts_and_images');
  const logo = page.getByRole('img', { name: 'Readarr Logo', exact: true });
  await logo.waitFor({ state: 'attached' });
  await page.waitForFunction(() => {
    const image = document.querySelector('img[alt="Readarr Logo"]');
    return image?.complete && image.naturalWidth > 0;
  });
  check('packaged_image_decodes', await logo.evaluate(element => element.naturalWidth > 0));
  check('packaged_font_loads', await page.evaluate(async () => (await document.fonts.load('400 16px Roboto')).some(face => face.status === 'loaded')));

  step('search_worker');
  const search = page.locator('input[name="authorSearch"]');
  const worker = page.waitForEvent('worker');
  await search.fill(config.author.name);
  const instance = await worker;
  const suggestion = page.getByRole('option').filter({ hasText: config.author.name }).first();
  await suggestion.waitFor();
  check('search_worker_returns_owned_catalog_result', await suggestion.isVisible());
  const workerUrl = new URL(instance.url());
  check('worker_loads_under_the_deployment_base', workerUrl.protocol === 'blob:' || (workerUrl.origin === base.origin && workerUrl.pathname.startsWith(prefix + '/')));
  await search.press('Escape');

  step('oauth_callback_asset');
  // The callback is local test data, not an external provider handshake.
  await page.evaluate(url => {
    window.onCompleteOauth = (query, close) => {
      window.ownedOauthCallback = query;
      close();
    };
    window.open(url, '_blank');
  }, `${config.url}/oauth.html?owned=asset-proof`);
  await page.waitForFunction(() => window.ownedOauthCallback === '?owned=asset-proof');
  check('oauth_html_executes_at_deployment_base', true);

  step('static_request_results');
  check('styles_scripts_fonts_and_images_loaded', ['script', 'stylesheet', 'font', 'image'].every(type => resources.some(item => item.type === type)));
  check('static_resources_are_successful_and_prefixed', resources.every(item => item.ok && item.url.origin === base.origin && item.url.pathname.startsWith(prefix + '/')));
  await page.screenshot({ path: path.join(output, 'owned-assets.png'), fullPage: false });
}

report(config => runBrowser(config, verifyAssets));
