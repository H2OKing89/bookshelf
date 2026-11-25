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
  const source = page.getByLabel('Source', { exact: true });
  const media = page.getByLabel('Media', { exact: true });
  await source.waitFor();
  const local = result('local', 'all');
  await source.selectOption('local');
  const localStats = await (await local).json();
  await metric('Audiobook recordings').filter({ hasText: /^1$/ }).waitFor();
  check('local_source_filter_selects_exact_recording_and_duration', localStats.audioRecordingCount === 1 && localStats.knownAudioDurationSeconds === 120);
  await source.selectOption('all');
  const ebook = result('all', 'ebook');
  await media.selectOption('ebook');
  const ebookStats = await (await ebook).json();
  await metric('Known audio duration').filter({ hasText: /^Unknown$/ }).waitFor();
  check('ebook_filter_retains_ebook_without_fake_audio_duration', ebookStats.ebookEditionCount === 1 && ebookStats.fileCount === 1 && ebookStats.knownAudioDurationSeconds == null);
  await media.selectOption('all');
  await page.screenshot({ path: path.join(output, `owned-statistics-${device}.png`), fullPage: false });

  step('list_membership_panel');
  await page.goto(`${config.url}/settings/importlists`);
  await page.getByText(config.firstList.name, { exact: true }).click();
  const dialog = page.getByRole('dialog').first();
  await dialog.waitFor();
  const membership = dialog.getByText('List membership', { exact: true });
  await membership.waitFor();
  // The membership may be a collapsed disclosure within the edit modal.
  if (!await dialog.getByText('audible:us:B011111111', { exact: false }).first().isVisible()) {
    await membership.click();
  }
  await dialog.getByText('audible:us:B011111111', { exact: false }).first().waitFor();
  check('list_panel_distinguishes_same_title_marketplaces', await dialog.getByText('audible:uk:B011111111', { exact: false }).first().isVisible());
  check('list_panel_exposes_exclusion_without_erasing_row', await dialog.getByText('Excluded', { exact: true }).isVisible());
  await page.screenshot({ path: path.join(output, `owned-list-membership-${device}.png`), fullPage: false });
  await page.keyboard.press('Escape');
  await dialog.waitFor({ state: 'hidden' });

  step('release_group_evidence');
  await page.goto(`${config.url}/book/synthetic-identity-us`);
  await page.getByRole('tab', { name: /^Files/ }).click();
  const file = page.getByRole('row').filter({ hasText: 'OwnedManualGroup' });
  await file.getByRole('button', { name: 'File Details', exact: true }).click();
  const details = page.getByRole('dialog', { name: 'Details', exact: true });
  await details.waitFor();
  await details.getByText('Explicit manual choice', { exact: true }).waitFor();
  check('file_details_explain_manual_choice_and_original_pack_uploader', await details.getByText('Explicit manual choice', { exact: true }).isVisible() &&
    await details.getByText('Pack uploader', { exact: true }).isVisible() && await details.getByText('OwnedPackUploader', { exact: true }).first().isVisible());
  await page.screenshot({ path: path.join(output, `owned-group-evidence-${device}.png`), fullPage: false });
  await page.keyboard.press('Escape');
  await details.waitFor({ state: 'hidden' });
}

report(config => runBrowser(config, verifyCollection, verifyCollection));
