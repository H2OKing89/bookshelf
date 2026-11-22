const { historyApi, historyRequest, historyResponse, report, runBrowser, selectFilter, signIn } = require('./history-paging-browser.cjs');

const failedFilter = args => args.get('eventType') === '4';
const allHistory = args => !args.has('eventType');

function historyHubMessage(packet) {
  if (!packet) {
    return null;
  }
  try {
    const value = JSON.parse(packet);
    return value.type === 1 && value.target.toLowerCase() === 'receivemessage' && value.arguments[0]?.name === 'history'
      ? value.arguments[0]
      : null;
  } catch {
    return null;
  }
}

async function observeHistoryHub(page, base) {
  const connections = [];
  const changes = [];
  let dropNextHistoryMessage = false;
  let notify;
  const endpoint = base.pathname.replace(/\/$/, '') + '/signalr/messages';
  await page.routeWebSocket(url => url.protocol === 'ws:' && url.host === base.host && url.pathname === endpoint, socket => {
    const server = socket.connectToServer();
    connections.push({ socket, server });
    server.onMessage(frame => {
      if (typeof frame !== 'string') {
        socket.send(frame);
        return;
      }
      const parts = frame.split('\x1e');
      const forwarded = parts.filter(packet => {
        const message = historyHubMessage(packet);
        if (!message) {
          return true;
        }
        const forwardedToPage = !dropNextHistoryMessage;
        dropNextHistoryMessage = false;
        const observation = {
          payloadFree: message.body?.action === 'sync' && message.body?.resource == null,
          forwardedToPage
        };
        changes.push(observation);
        notify?.(observation);
        return forwardedToPage;
      });
      if (forwarded.join('\x1e')) {
        socket.send(forwarded.join('\x1e'));
      }
    });
  });
  const started = page.waitForEvent('console', {
    predicate: message => message.text() === '[signalR] connected'
  });
  async function nextChange({ drop = false } = {}) {
    dropNextHistoryMessage = drop;
    const observed = new Promise(resolve => { notify = resolve; });
    try {
      return await Promise.race([
        observed,
        page.waitForTimeout(8000).then(() => { throw new Error('HistoryHubMessageMissing'); })
      ]);
    } finally {
      notify = undefined;
    }
  }
  return { started, connections, changes, nextChange };
}

async function markOwnedGrabAsFailed(page, base, id) {
  if (!Number.isInteger(id) || ![104, 125].includes(id)) {
    throw new Error('OwnedGrabRequired');
  }
  const apiKey = await page.evaluate(() => window.Readarr.apiKey);
  if (!apiKey) {
    throw new Error('OwnedHistoryFixtureAuthenticationMissing');
  }
  const response = await page.context().request.post(historyApi(base) + '/failed/' + id, {
    headers: { 'X-Requested-With': 'XMLHttpRequest', 'X-Api-Key': apiKey },
    data: {},
    maxRedirects: 0
  });
  if (!response.ok()) {
    throw new Error('OwnedHistoryWriteFailed');
  }
}

async function verifySync(page, base, config, { check, step }) {
  step('signin_and_warm_filter');
  const hub = await observeHistoryHub(page, base);
  const pageOne = historyResponse(page, base, allHistory);
  await signIn(page, config);
  await Promise.all([pageOne, hub.started]);
  check('global_history_connects_to_real_signalr_hub', hub.connections.length === 1);

  const [empty] = await Promise.all([historyResponse(page, base, failedFilter), selectFilter(page, 'Download Failed')]);
  check('inactive_filter_begins_with_cached_empty_result', (await empty.json()).totalRecords === 0);
  await page.getByText('No history.', { exact: true }).waitFor();
  await Promise.all([historyResponse(page, base, allHistory), selectFilter(page, 'All')]);
  await page.getByRole('button', { name: '1 / 2', exact: true }).waitFor();

  const requests = [];
  page.on('request', request => {
    const args = historyRequest(base, request);
    if (args) {
      requests.push(args.get('eventType'));
    }
  });
  const rows = page.getByRole('table').locator('tbody tr');
  const failedHistory = () => rows.filter({ has: page.getByRole('button', { name: 'History Details', exact: true }) });

  step('real_history_change_and_inactive_invalidation');
  const [liveEvent, refreshed] = await Promise.all([
    hub.nextChange(),
    historyResponse(page, base, allHistory),
    markOwnedGrabAsFailed(page, base, config.firstGrab)
  ]);
  const liveResult = await refreshed.json();
  check('mutation_emits_payload_free_signalr_and_refreshes_active_history', liveEvent.payloadFree && liveEvent.forwardedToPage &&
    liveResult.totalRecords === 26 && liveResult.records[0].eventType === 'downloadFailed' &&
    liveResult.records[0].sourceTitle === 'Owned history row ' + config.firstGrab);
  check('live_event_refetches_only_the_active_filter', requests.length === 1 && requests[0] === null);

  const [filtered] = await Promise.all([historyResponse(page, base, failedFilter), selectFilter(page, 'Download Failed')]);
  await failedHistory().first().waitFor();
  check('returning_to_previous_filter_shows_live_change', (await filtered.json()).totalRecords === 1 &&
    await failedHistory().count() === 1 && await page.getByText('No history.', { exact: true }).count() === 0);

  step('missed_history_event');
  const startingCalls = requests.length;
  const [missed] = await Promise.all([
    hub.nextChange({ drop: true }),
    markOwnedGrabAsFailed(page, base, config.secondGrab)
  ]);
  check('fixture_drops_one_real_event_before_reconnect', missed.payloadFree && !missed.forwardedToPage &&
    requests.length === startingCalls && await failedHistory().count() === 1);

  step('reconnect_and_refresh_current_history');
  const freshPage = historyResponse(page, base, failedFilter);
  const { socket, server } = hub.connections[0];
  const close = { code: 4000, reason: 'Owned fixture reconnect' };
  const [reconnected] = await Promise.all([freshPage, socket.close(close), server.close(close)]);
  const fresh = await reconnected.json();
  await page.getByRole('table').getByRole('link', { name: 'Synthetic Joint Work', exact: true }).waitFor();
  check('reconnect_recovers_missed_history_without_duplicate_refresh', hub.connections.length === 2 &&
    requests.length === startingCalls + 1 && requests.at(-1) === '4' &&
    fresh.totalRecords === 2 && fresh.records[0].sourceTitle === 'Owned history row ' + config.secondGrab &&
    await failedHistory().count() === 2 && await page.getByText('No history.', { exact: true }).count() === 0);
}

report(config => runBrowser(config, verifySync));
