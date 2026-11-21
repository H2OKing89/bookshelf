const pollInterval = 30_000;
const recoveryTime = '2026-11-20T12:34:56Z';
const recoveredObservation = {
  provider: 'audnexus',
  operation: 'bookSearch',
  state: 'complete',
  reason: null,
  observedAtUtc: recoveryTime,
  lastCompleteAtUtc: recoveryTime,
  retryAfterUtc: null
};

async function verifyMetadataStatusTransport(context, base, check, stage) {
  const page = await context.newPage();
  page.setDefaultTimeout(8_000);

  try {
    const statusPath = base.pathname.replace(/\/$/, '') + '/system/status';
    const endpoint = base.origin + base.pathname.replace(/\/$/, '') + '/api/v1/metadataproviderstatus';
    const panel = page.locator('fieldset').filter({
      has: page.getByRole('heading', { name: 'Metadata Providers', exact: true })
    });
    const limited = panel.getByText('Rate limited', { exact: true });
    const missing = panel.getByText('Missing', { exact: true }).first();
    const alert = panel.getByRole('alert');
    const isMetadataRequest = request => request.url() === endpoint;
    const waitForResponse = () => page.waitForResponse(response => isMetadataRequest(response.request()));
    let reply = null;
    let pageErrors = 0;

    page.on('pageerror', () => pageErrors++);
    await page.route(endpoint, route => {
      if (reply === null) {
        return route.fallback();
      }
      // Leave this interception pending so cancellation comes from the page.
      if (reply === 'hold') {
        return;
      }
      return route.fulfill(reply);
    });

    stage('metadata_fetch_contract');
    await page.clock.install();
    const [initial] = await Promise.all([
      waitForResponse(),
      page.goto(base.origin + statusPath)
    ]);
    const request = initial.request();
    const apiKey = await request.headerValue('X-Api-Key');
    const matchesInitialization = await page.evaluate(key => !!key && key === window.Readarr.apiKey, apiKey);
    check('provider_status_fetch_uses_authenticated_prefixed_api',
      initial.ok() && request.method() === 'GET' && request.resourceType() === 'fetch' &&
      matchesInitialization && await request.headerValue('X-Requested-With') === 'XMLHttpRequest');
    await limited.waitFor();
    await missing.waitFor();

    const refresh = async nextReply => {
      reply = nextReply;
      const [response] = await Promise.all([waitForResponse(), page.clock.fastForward(pollInterval)]);
      return response;
    };

    const cases = [
      ['malformed_json', { status: 200, contentType: 'application/json', body: '{"incomplete":' }],
      ['invalid_observation', { status: 200, json: [{ ...recoveredObservation, observedAtUtc: 'invalid timestamp' }] }],
      ['http_error', { status: 503, contentType: 'text/plain', body: 'Synthetic metadata status service error' }],
      ['expired_authentication', { status: 401, body: '' }]
    ];

    for (const [name, fixtureReply] of cases) {
      stage('metadata_fetch_' + name);
      const failure = await refresh(fixtureReply);
      await alert.waitFor();
      check('provider_status_handles_' + name, failure.status() === fixtureReply.status &&
        await alert.innerText() === 'Could not load metadata provider observations.' &&
        await limited.isVisible() && await missing.isVisible());

      stage('metadata_fetch_recovers_after_' + name);
      const recovery = await refresh(null);
      await alert.waitFor({ state: 'hidden' });
      check('provider_status_recovers_from_' + name, recovery.ok() && await limited.isVisible() && await missing.isVisible());
    }

    stage('metadata_fetch_pending_refresh');
    reply = 'hold';
    const [pending] = await Promise.all([
      page.waitForRequest(isMetadataRequest),
      page.clock.fastForward(pollInterval)
    ]);
    check('provider_status_retains_last_good_observations_during_refresh',
      await limited.isVisible() && await missing.isVisible() && await alert.count() === 0);

    stage('metadata_fetch_aborts_on_unmount');
    const documentStart = await page.evaluate(() => performance.timeOrigin);
    const [aborted] = await Promise.all([
      page.waitForEvent('requestfailed', { predicate: failed => failed === pending }),
      page.getByRole('link', { name: 'Tasks', exact: true }).click()
    ]);
    await panel.waitFor({ state: 'hidden' });
    check('provider_status_aborts_pending_fetch_when_panel_unmounts',
      aborted.failure()?.errorText === 'net::ERR_ABORTED' &&
      await page.evaluate(() => performance.timeOrigin) === documentStart);

    stage('metadata_fetch_remount_after_abort');
    await page.clock.fastForward(pollInterval);
    reply = { status: 200, json: [recoveredObservation] };
    const [reentered] = await Promise.all([
      waitForResponse(),
      page.getByRole('link', { name: /^Status(?:\s+\d+)?$/ }).click()
    ]);
    await panel.locator('time[datetime="' + recoveryTime + '"]').first().waitFor();
    check('provider_status_remount_reads_fresh_observations_after_abort', reentered.ok() &&
      await panel.getByText('Complete', { exact: true }).isVisible() &&
      await limited.count() === 0 && await alert.count() === 0);
    check('provider_status_transport_has_no_page_errors', pageErrors === 0);
  } finally {
    await page.close();
  }
}

module.exports = { verifyMetadataStatusTransport };
