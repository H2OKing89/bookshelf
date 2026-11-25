function appPath(base, route) {
  return base.pathname.replace(/\/$/, '') + route;
}

async function observeSignalR(page, base) {
  const connections = [];
  const hubPath = appPath(base, '/signalr/messages');
  await page.routeWebSocket(url => url.protocol === 'ws:' && url.host === base.host && url.pathname === hubPath,
    socket => connections.push({ socket, server: socket.connectToServer() }));

  const started = page.waitForEvent('console', {
    predicate: message => message.text() === '[signalR] connected'
  }).then(() => true, () => false);

  return { connections, started };
}

async function verifySignalRResync(page, base, signalR, check) {
  const { connections, started } = signalR;
  check('signalr_initial_connection', (await started) && connections.length === 1);

  const apiPath = appPath(base, '/api/v1/').toLowerCase();
  const requests = new Map([['author', 0], ['command', 0], ['bookfile', 0]]);
  const endpoint = request => {
    const url = new URL(request.url());
    const pathname = url.pathname.toLowerCase();
    if (request.method() !== 'GET' || url.origin !== base.origin || !pathname.startsWith(apiPath)) {
      return;
    }

    const name = pathname.slice(apiPath.length);
    if (requests.has(name) && (name !== 'bookfile' || url.searchParams.get('unmapped') === 'true')) {
      return name;
    }
  };
  const countRequest = request => {
    const name = endpoint(request);
    if (name) {
      requests.set(name, requests.get(name) + 1);
    }
  };

  page.on('request', countRequest);
  try {
    const responses = [...requests.keys()].map(name => page.waitForResponse(response =>
      response.ok() && endpoint(response.request()) === name));
    const { socket, server } = connections[0];
    const close = { code: 4000, reason: 'Owned fixture reconnect' };
    await Promise.all([...responses, socket.close(close), server.close(close)]);

    check('signalr_reconnects_once', connections.length === 2);
    check('signalr_resyncs_authors_and_commands_once', requests.get('author') === 1 && requests.get('command') === 1);
    check('signalr_resyncs_active_page_once', requests.get('bookfile') === 1 &&
      new URL(page.url()).pathname === appPath(base, '/unmapped'));
  } finally {
    page.off('request', countRequest);
  }
}

async function reorderByPointer(page, source, target, touch, position = 'below') {
  await source.scrollIntoViewIfNeeded();
  await target.scrollIntoViewIfNeeded();
  const from = await source.boundingBox();
  const to = await target.boundingBox();
  if (!from || !to) {
    throw new Error('DragTargetNotVisible');
  }

  const x = to.x + to.width / 2;
  const y = to.y + (position === 'below' ? to.height - 7 : 7);
  if (!touch) {
    await source.dragTo(target, {
      steps: 12,
      targetPosition: { x: to.width / 2, y: y - to.y }
    });
    return;
  }

  // Chromium's CDP produces real touchstart/move/end events. Synthetic
  // DragEvents would only exercise the HTML5 backend on a phone viewport.
  const session = await page.context().newCDPSession(page);
  const startX = from.x + from.width / 2;
  const startY = from.y + from.height / 2;
  try {
    await session.send('Input.dispatchTouchEvent', {
      type: 'touchStart', touchPoints: [{ x: startX, y: startY, id: 0 }]
    });
    for (let step = 1; step <= 15; step++) {
      await session.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: [{ x: startX + (x - startX) * step / 15, y: startY + (y - startY) * step / 15, id: 0 }]
      });
    }
    await session.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  } finally {
    await session.detach();
  }
}

async function verifyTableOptionsReorder(page, opener, check, touch = false) {
  await opener.click();
  const dialog = page.getByRole('dialog', { name: 'Table Options', exact: true });
  await dialog.waitFor();

  const fieldSelector = 'input[name="size"], input[name="dateAdded"]';
  const fields = dialog.locator(fieldSelector);
  const order = () => fields.evaluateAll(inputs => inputs.map(input => input.name).join(','));
  check('table_options_fixture_order', await order() === 'size,dateAdded');

  const rows = dialog.locator('[class*="TableOptionsColumnDragSource-columnDragSource"]');
  const source = rows.filter({ has: page.locator('input[name="size"]') }).locator('[class*="TableOptionsColumn-dragHandle"]');
  const target = rows.filter({ has: page.locator('input[name="dateAdded"]') });
  const box = await target.boundingBox();
  check('table_options_drop_target_is_visible', Boolean(box));

  await reorderByPointer(page, source, target, touch);
  await page.waitForFunction(({ element, fieldSelector }) =>
    Array.from(element.querySelectorAll(fieldSelector), input => input.name).join(',') === 'dateAdded,size',
  { element: await dialog.elementHandle(), fieldSelector });
  check('table_options_reorders_columns', await order() === 'dateAdded,size');
  const nativeDraggable = await source.getAttribute('draggable');
  check('table_options_uses_expected_drag_backend', touch ? nativeDraggable !== 'true' : nativeDraggable === 'true');

  await page.keyboard.press('Escape');
  await dialog.waitFor({ state: 'hidden' });
  const header = page.locator('[class*="VirtualTableHeader-header"]');
  const columns = await header.locator(':scope > *').allTextContents();
  const date = columns.indexOf('Date Added');
  const size = columns.indexOf('Size');
  check('table_uses_reordered_columns', date !== -1 && date < size);
}

function observeReactWarnings(page) {
  const counts = { legacyRender: 0, functionDefaultProps: 0, findDOMNode: 0, findDOMNodeComponents: {} };
  page.on('console', message => {
    if (message.type() !== 'warning' && message.type() !== 'error') {
      return;
    }

    const text = message.text();
    if (text.includes('ReactDOM.render is no longer supported in React 18')) {
      counts.legacyRender++;
    } else if (text.includes('Support for defaultProps will be removed from function components')) {
      counts.functionDefaultProps++;
    } else if (text.includes('findDOMNode is deprecated')) {
      counts.findDOMNode++;
      const component = text.match(/^\s+at ([A-Za-z][A-Za-z\d_$]*)[ (]/m)?.[1];
      if (component) {
        const components = counts.findDOMNodeComponents;
        components[component] = (components[component] || 0) + 1;
      }
    }
  });
  return counts;
}

module.exports = { observeReactWarnings, observeSignalR, verifySignalRResync, verifyTableOptionsReorder, reorderByPointer };
