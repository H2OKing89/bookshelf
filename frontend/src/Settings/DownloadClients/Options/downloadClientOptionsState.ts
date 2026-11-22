import { queryOptions } from '@tanstack/react-query';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';

const path = '/config/downloadclient';
const timeout = 30_000;
const editableOptions = [
  'enableCompletedDownloadHandling',
  'autoRedownloadFailed',
  'autoRedownloadFailedFromInteractiveSearch'
] as const;

export type DownloadClientOption = typeof editableOptions[number];
export type DownloadClientOptionsDraft = Partial<Record<DownloadClientOption, boolean>>;
export type DownloadClientOptionsResource = Record<DownloadClientOption, boolean> & {
  id: 1;
  downloadClientWorkingFolders: string;
};

export function isDownloadClientOption(name: string): name is DownloadClientOption {
  return editableOptions.some((option) => option === name);
}

export function changeDownloadClientOption(
  saved: DownloadClientOptionsResource,
  draft: DownloadClientOptionsDraft,
  name: DownloadClientOption,
  value: boolean
): DownloadClientOptionsDraft {
  if ((draft[name] ?? saved[name]) === value) {
    return draft;
  }

  const changed = { ...draft };

  if (value === saved[name]) {
    delete changed[name];
  } else {
    changed[name] = value;
  }

  return changed;
}

function parseDownloadClientOptions(value: unknown): DownloadClientOptionsResource {
  if (
    typeof value !== 'object' || value === null ||
    !('id' in value) || value.id !== 1 ||
    !('downloadClientWorkingFolders' in value) || typeof value.downloadClientWorkingFolders !== 'string' ||
    !('enableCompletedDownloadHandling' in value) || typeof value.enableCompletedDownloadHandling !== 'boolean' ||
    !('autoRedownloadFailed' in value) || typeof value.autoRedownloadFailed !== 'boolean' ||
    !('autoRedownloadFailedFromInteractiveSearch' in value) || typeof value.autoRedownloadFailedFromInteractiveSearch !== 'boolean'
  ) {
    throw new Error('Invalid download client options');
  }

  return {
    id: value.id,
    downloadClientWorkingFolders: value.downloadClientWorkingFolders,
    enableCompletedDownloadHandling: value.enableCompletedDownloadHandling,
    autoRedownloadFailed: value.autoRedownloadFailed,
    autoRedownloadFailedFromInteractiveSearch: value.autoRedownloadFailedFromInteractiveSearch
  };
}

export function downloadClientOptionsQuery(apiRoot: string) {
  return queryOptions({
    queryKey: ['downloadClientOptions', apiRoot] as const,
    queryFn: ({ signal }) => fetchJson({ path, parse: parseDownloadClientOptions, signal, timeout }),
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}

export function saveDownloadClientOptions(body: DownloadClientOptionsResource) {
  return fetchJson({ path: `${path}/${body.id}`, method: 'PUT', body, parse: parseDownloadClientOptions, timeout });
}

type ValidationMessage = { message: string; link?: string; detailedMessage?: string };

export function downloadClientOptionValidation(error: Error | null, name: DownloadClientOption) {
  const errors: ValidationMessage[] = [];
  const warnings: ValidationMessage[] = [];

  if (error instanceof ApiError && error.details.kind === 'http' && error.details.status === 400) {
    const body = error.details.body;

    if (body.format === 'json' && Array.isArray(body.value)) {
      const failures: unknown[] = body.value;

      for (const failure of failures) {
        if (
          typeof failure === 'object' && failure !== null &&
          'propertyName' in failure && typeof failure.propertyName === 'string' &&
          failure.propertyName.toLowerCase() === name.toLowerCase() &&
          'errorMessage' in failure && typeof failure.errorMessage === 'string'
        ) {
          const group = 'isWarning' in failure && failure.isWarning === true ? warnings : errors;

          group.push({
            message: failure.errorMessage,
            link: 'infoLink' in failure && typeof failure.infoLink === 'string' ? failure.infoLink : undefined,
            detailedMessage: 'detailedDescription' in failure && typeof failure.detailedDescription === 'string' ? failure.detailedDescription : undefined
          });
        }
      }
    }
  }

  return { errors, warnings };
}
