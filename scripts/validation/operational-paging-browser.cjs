function api(base, resource) {
  return base.origin + base.pathname.replace(/\/$/, '') + '/api/v1/' + resource;
}

function requestArgs(base, request, resource, method = 'GET') {
  const url = new URL(request.url());
  return request.method() === method && url.origin + url.pathname === api(base, resource) ? url.searchParams : null;
}

function responseFor(page, base, resource, match = () => true) {
  return page.waitForResponse(response => {
    const args = requestArgs(base, response.request(), resource);
    return response.ok() && args !== null && match(args);
  });
}

async function isAuthenticatedFetch(page, request) {
  const apiKey = await request.headerValue('X-Api-Key');
  return request.resourceType() === 'fetch' &&
    await request.headerValue('X-Requested-With') === 'XMLHttpRequest' &&
    await page.evaluate(key => !!key && key === window.Readarr.apiKey, apiKey);
}

async function observeHub(page, base) {
  const connections = [];
  const messages = [];
  let drop = () => false;
  const endpoint = base.pathname.replace(/\/$/, '') + '/signalr/messages';
  await page.routeWebSocket(url => url.protocol === 'ws:' && url.host === base.host && url.pathname === endpoint, socket => {
    const server = socket.connectToServer();
    connections.push({ socket, server });
    server.onMessage(frame => {
      if (typeof frame !== 'string') {
        return socket.send(frame);
      }
      const forwarded = frame.split('\x1e').filter(packet => {
        if (!packet) return true;
        try {
          const value = JSON.parse(packet);
          const message = value.arguments?.[0];
          if (value.type === 1 && value.target.toLowerCase() === 'receivemessage' && message?.name) {
            const info = { name: message.name, action: message.body?.action, id: message.body?.resource?.id };
            const dropped = drop(info);
            messages.push({ ...info, dropped });
            return !dropped;
          }
        } catch {
          // Leave protocol frames we do not inspect intact.
        }
        return true;
      });
      if (forwarded.join('\x1e')) {
        socket.send(forwarded.join('\x1e'));
      }
    });
  });
  const started = page.waitForEvent('console', { predicate: message => message.text() === '[signalR] connected' });
  return { connections, messages, started, drop: predicate => { drop = predicate; } };
}

async function putOwned(page, base, resource, data) {
  const apiKey = await page.evaluate(() => window.Readarr.apiKey);
  if (!apiKey) throw new Error('OwnedLibraryFixtureAuthenticationMissing');
  const response = await page.context().request.put(api(base, resource), {
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': apiKey },
    data,
    maxRedirects: 0
  });
  if (!response.ok()) throw new Error('OwnedLibraryFixtureWriteFailed');
  return response;
}

function rows(page) {
  return page.getByRole('table').locator('tbody tr');
}

function selection(page, id) {
  return page.getByRole('table').locator(`tbody input[type="checkbox"][name="${id}"]`);
}

async function setCheckbox(box, checked) {
  await box.locator('xpath=self::*[not(@disabled)]').waitFor();
  if (await box.isChecked() !== checked) {
    await box.press('Space');
  }
  if (await box.isChecked() !== checked) {
    throw new Error('OwnedFixtureCheckboxStateDidNotChange');
  }
}

module.exports = { api, isAuthenticatedFetch, observeHub, putOwned, requestArgs, responseFor, rows, selection, setCheckbox };
