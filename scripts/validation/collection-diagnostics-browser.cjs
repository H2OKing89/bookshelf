const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verifyCollection(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  const prefix = base.pathname.replace(/\/$/, '');
  const statistics = `${base.origin}${prefix}/api/v1/librarystatistics`;
  const result = (source, media) => page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.origin + url.pathname === statistics && response.ok() &&
      (url.searchParams.get('source') || 'all') === source && (url.searchParams.get('media') || 'all') === media;
  });
  step('statistics_page');
  const initial = result('all', 'all');
  await signIn(page, config, '/statistics');
  await page.getByRole('heading', { name: /library statistics/i }).first().waitFor();
  const first = await (await initial).json();
  const metric = label => page.locator('dt').filter({ hasText: new RegExp(`^${label}$`) }).locator('..').locator('dd').first();
  await metric('Audiobook recordings').filter({ hasText: /^8$/ }).waitFor();
  check('statistics_page_loads_owned_recording_and_work_counts', first.audioRecordingCount === 8 && first.confirmedWorkCount === 2 && first.unresolvedFileCount === 1 &&
    await metric('Confirmed works').textContent() === '2' && await metric('Unresolved files').textContent() === '1');
  step('local_statistics_filter');
  const source = page.getByRole('combobox', { name: /^Source/ });
  const media = page.getByRole('combobox', { name: /^Media/ });
  await source.waitFor();
  const local = result('local', 'all');
  await source.selectOption('local');
  const localStats = await (await local).json();
  await metric('Audiobook recordings').filter({ hasText: /^1$/ }).waitFor();
  check('local_source_filter_selects_exact_recording_and_duration', localStats.audioRecordingCount === 1 && localStats.knownAudioDurationSeconds === 120);
  await source.selectOption('all');
  step('ebook_statistics_filter');
  const ebook = result('all', 'ebook');
  await media.selectOption('ebook');
  const ebookStats = await (await ebook).json();
  await metric('Known audio duration').filter({ hasText: /^Unknown$/ }).waitFor();
  check('ebook_filter_retains_ebook_without_fake_audio_duration', ebookStats.ebookEditionCount === 1 && ebookStats.fileCount === 1 && ebookStats.knownAudioDurationSeconds == null);
  await media.selectOption('all');
  step('explicit_statistics_refresh');
  await metric('Audiobook recordings').filter({ hasText: /^8$/ }).waitFor();
  const refreshed = result('all', 'all');
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  const refreshedStats = await (await refreshed).json();
  check('statistics_refresh_returns_current_totals', refreshedStats.audioRecordingCount === 8 && refreshedStats.knownAudioDurationSeconds === 340);
  await page.screenshot({ path: path.join(output, `owned-statistics-${device}.png`), fullPage: false });

  step('list_membership_panel');
  // Existing provider option lookups use POST despite being read-only. Permit
  // only the three known option actions against the owned list fixture.
  const optionPrefix = `${prefix.toLowerCase()}/api/v1/importlist/action/`;
  const optionActions = new Set(['getprofiles', 'gettags', 'getrootfolders']);
  await page.route(url => url.origin === base.origin && url.pathname.toLowerCase().startsWith(optionPrefix) &&
    optionActions.has(url.pathname.toLowerCase().slice(optionPrefix.length)), route =>
    route.request().method() === 'POST' ? route.continue() : route.fallback());
  await page.goto(`${config.url}/settings/importlists`);
  try {
    // Card text overlays a real underlay button with pointer events disabled.
    const card = page.getByText(config.firstList.name, { exact: true }).locator('../..');
    await card.getByRole('button', { name: '', exact: true }).click();
  } catch (error) {
    await page.screenshot({ path: path.join(output, `owned-list-navigation-${device}.png`) });
    throw error;
  }
  step('list_editor_open');
  const dialog = page.getByRole('dialog').first();
  await dialog.waitFor();
  step('list_membership_disclosure');
  const membership = dialog.getByText('List membership', { exact: true });
  await membership.waitFor();
  const membershipResponse = page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.origin === base.origin && url.pathname === `${prefix}/api/v1/importlistmembership/${config.firstList.id}` && response.ok();
  });
  await membership.click();
  step('list_membership_items');
  const snapshot = await (await membershipResponse).json();
  const usBook = dialog.getByText('Source book: audible:us:B011111111', { exact: true });
  const ukBook = dialog.getByText('Source book: audible:uk:B011111111', { exact: true });
  await usBook.waitFor();
  await ukBook.waitFor();
  check('list_panel_distinguishes_same_title_marketplaces', snapshot.importListId === config.firstList.id &&
    await usBook.isVisible() && await ukBook.isVisible());
  check('list_panel_exposes_exclusion_without_erasing_row', await ukBook.locator('..').getByText('Excluded', { exact: true }).isVisible());
  await usBook.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, `owned-list-membership-${device}.png`), fullPage: false });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await dialog.waitFor({ state: 'hidden' });

  step('release_group_evidence');
  await page.goto(`${config.url}/book/synthetic-identity-us`);
  await page.getByRole('tab', { name: /^Files/ }).click();
  step('release_group_file_action');
  const file = page.getByRole('row').filter({ hasText: 'OwnedManualGroup' });
  await file.getByRole('button', { name: 'File Details', exact: true }).click();
  const details = page.getByRole('dialog', { name: 'Details', exact: true });
  await details.waitFor();
  step('release_group_explanation');
  await details.getByText('Explicit manual choice', { exact: true }).waitFor();
  check('file_details_explain_manual_choice_and_original_pack_uploader', await details.getByText('Explicit manual choice', { exact: true }).isVisible() &&
    await details.getByText('Pack uploader', { exact: true }).isVisible() && await details.getByText('OwnedPackUploader', { exact: true }).first().isVisible());
  await page.screenshot({ path: path.join(output, `owned-group-evidence-${device}.png`), fullPage: false });
  await page.keyboard.press('Escape');
  await details.waitFor({ state: 'hidden' });
}

report(config => runBrowser(config, verifyCollection, verifyCollection));
