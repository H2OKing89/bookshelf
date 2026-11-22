import { changeDownloadClientOption, type DownloadClientOptionsResource } from 'Settings/DownloadClients/Options/downloadClientOptionsState';

export function checkDownloadClientOptionsTypes(saved: DownloadClientOptionsResource) {
  const draft = changeDownloadClientOption(saved, {}, 'autoRedownloadFailed', false);
  // @ts-expect-error The hidden API value is preserved and cannot appear in an editable draft.
  const workingFolders = changeDownloadClientOption(saved, draft, 'downloadClientWorkingFolders', true);
  // @ts-expect-error The API flags accept boolean values only.
  const invalidValue = changeDownloadClientOption(saved, draft, 'autoRedownloadFailed', 0);
  // @ts-expect-error An omitted draft value inherits the saved setting.
  const assumedValue: boolean = draft.enableCompletedDownloadHandling;

  return { draft, workingFolders, invalidValue, assumedValue };
}
