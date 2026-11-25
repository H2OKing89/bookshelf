const path = require('path');
const { report, runBrowser, signIn } = require('./history-paging-browser.cjs');

async function verifyEditors(page, base, config, { check, step, output }) {
  const device = page.viewportSize().width < 600 ? 'phone' : 'desktop';
  const prefix = base.pathname.replace(/\/$/, '');
  const localRoute = `${base.origin}${prefix}/api/v1/localrecording/${config.local.id}`;
  const packRoute = `${base.origin}${prefix}/api/v1/recordingwork/${config.pack.editionId}`;
  for (const route of [localRoute, packRoute]) {
    // Allow only these two exact synthetic metadata writes over the normal
    // read-only browser fixture; everything else remains blocked.
    await page.route(url => url.href === route, request => request.request().method() === 'PUT' ? request.continue() : request.fallback());
  }

  step('local_editor');
  await signIn(page, config, `/book/${config.local.slug}`);
  await page.getByText(config.local.title, { exact: true }).first().waitFor();
  const editLocal = page.getByRole('button', { name: 'Edit local metadata', exact: true });
  const openLocal = async () => {
    const more = page.getByRole('button', { name: 'More', exact: true }).first();
    await editLocal.or(more).first().waitFor();
    if (device === 'phone' || !await editLocal.isVisible()) {
      await more.click();
      await page.getByRole('menu', { name: 'More', exact: true }).getByRole('menuitem', { name: 'Edit local metadata', exact: true }).click();
    } else {
      await editLocal.click();
    }
  };
  await openLocal();
  const local = page.getByRole('dialog', { name: 'Edit local recording', exact: true });
  await local.waitFor();
  const subtitle = local.getByLabel('Subtitle', { exact: true });
  await subtitle.waitFor();
  check('local_editor_loads_exact_recording', await local.getByLabel('Title', { exact: true }).inputValue() === config.local.title);
  check('local_editor_retains_ordered_narrators', await local.getByLabel('Narrators', { exact: true }).inputValue() === 'Owned Narrator B\nOwned Narrator A');
  await subtitle.fill(`Owned ${device} browser correction`);
  const localSaved = page.waitForResponse(response => response.url() === localRoute && response.request().method() === 'PUT');
  await local.getByRole('button', { name: 'Save metadata', exact: true }).click();
  check('local_editor_saves_to_exact_identity', (await localSaved).ok());
  await local.waitFor({ state: 'hidden' });
  await openLocal();
  await local.waitFor();
  await local.getByLabel('Title', { exact: true }).waitFor();
  check('local_editor_reloads_persisted_value', await local.getByLabel('Subtitle', { exact: true }).inputValue() === `Owned ${device} browser correction`);
  await page.screenshot({ path: path.join(output, `owned-local-${device}.png`), fullPage: false });
  await page.keyboard.press('Escape');
  await local.waitFor({ state: 'hidden' });

  step('pack_coverage');
  await page.goto(`${config.url}/book/${config.pack.slug}`);
  await page.getByRole('tab', { name: 'Work coverage', exact: true }).click();
  const coverage = page.getByRole('region', { name: 'Recording work coverage', exact: true });
  const confirmedCount = coverage.getByText(/Related confirmed coverage: 2 works with associated files, 4 distinct recordings, 5 distinct files/);
  await confirmedCount.waitFor();
  check('pack_displays_distinct_counts_and_uncertainty', await confirmedCount.isVisible() &&
    await coverage.getByText(/Uncertain membership/).isVisible());
  check('pack_has_explicit_related_recording_links', (await coverage.getByRole('link', { name: /audible:(us|uk):B011111111/ }).count()) === 2);
  const editWork = coverage.getByRole('button', { name: 'Edit work coverage', exact: true });
  await editWork.click();
  const editor = page.getByRole('dialog', { name: 'Edit work coverage', exact: true });
  await editor.waitFor();
  const sideMembership = () => editor.getByRole('region', { name: /^Work membership \d+$/ }).filter({ hasText: 'Owned Uncertain Side Story' }).getByLabel('Membership');
  check('uncertain_membership_is_editable', await sideMembership().inputValue() === 'uncertain');
  await sideMembership().selectOption('confirmed');
  const firstSave = page.waitForResponse(response => response.url() === packRoute && response.request().method() === 'PUT');
  await editor.getByRole('button', { name: 'Save coverage', exact: true }).click();
  check('coverage_correction_saves_exact_edition', (await firstSave).ok());
  await editor.waitFor({ state: 'hidden' });
  await coverage.getByText(/Related confirmed coverage: 3 works with associated files, 4 distinct recordings, 5 distinct files/).waitFor();
  await editWork.click();
  await editor.waitFor();
  await sideMembership().selectOption('uncertain');
  const secondSave = page.waitForResponse(response => response.url() === packRoute && response.request().method() === 'PUT');
  await editor.getByRole('button', { name: 'Save coverage', exact: true }).click();
  check('coverage_correction_does_not_duplicate_files', (await secondSave).ok());
  await confirmedCount.waitFor();
  await page.screenshot({ path: path.join(output, `owned-pack-${device}.png`), fullPage: false });

  if (device === 'desktop') {
    step('create_local_author_from_unmapped_file');
    const createRoute = `${base.origin}${prefix}/api/v1/localrecording`;
    await page.route(url => url.href === createRoute, request => {
      const body = request.request().method() === 'POST' ? request.request().postDataJSON() : null;
      if (body && body.fileIds?.length === 1 && body.fileIds[0] === config.create.fileId &&
          body.newAuthorName === config.create.author && body.newAuthorPath === config.create.path) return request.continue();
      return request.fallback();
    });
    await page.goto(`${config.url}/unmapped`);
    await page.getByText(new RegExp(config.create.title + '\\.m4b')).waitFor();
    step('select_unmapped_file');
    const checkbox = page.locator(`input[name="${config.create.fileId}"]`);
    // The styled CheckInput deliberately disables pointer events on its native
    // input; click the containing label the way a user would.
    await checkbox.locator('..').click();
    check('only_owned_unmapped_file_selected', await checkbox.isChecked());
    step('open_create_recording_editor');
    await page.getByRole('button', { name: 'Create local recording', exact: true }).click();
    const create = page.getByRole('dialog', { name: 'Create local recording', exact: true });
    await create.getByLabel('Title', { exact: true }).waitFor();
    check('create_editor_reads_real_tags', await create.getByLabel('Title', { exact: true }).inputValue() === config.create.title);
    await create.getByLabel('Author', { exact: true }).selectOption({ label: 'Create a local author' });
    await create.getByLabel('Local author name', { exact: true }).fill(config.create.author);
    await create.getByLabel('Local author folder', { exact: true }).fill(config.create.path);
    step('save_new_local_author');
    const created = page.waitForResponse(response => response.url() === createRoute && response.request().method() === 'POST');
    await create.getByRole('button', { name: 'Create recording', exact: true }).click();
    check('create_local_author_and_recording_has_committed', (await created).ok());
    await create.waitFor({ state: 'hidden' });
    await page.getByRole('link', { name: config.create.title, exact: true }).waitFor();
  }
}

report(config => runBrowser(config, verifyEditors, verifyEditors));
