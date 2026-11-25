import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import queryClient from 'App/queryClient';
import fetchJson from 'Utilities/Fetch/fetchJson';
import type { QueueFilters } from './queueFilters';

const refreshDelay = 100;
const actionTimeout = 120_000;
let refreshTimer: ReturnType<typeof setTimeout> | null = null;
let refreshPending = false;
let refreshInFlight = false;
const refreshHolds = new Set<symbol>();

const sortAliases: Record<string, string> = {
  'authorMetadata.sortName': 'authors.sortName',
  'books.title': 'book.title',
  'books.releaseDate': 'book.releaseDate'
};

interface QueuePageRequest {
  page: number;
  pageSize: number;
  sortKey: string;
  sortDirection: 'ascending' | 'descending';
  includeUnknownAuthorItems: boolean;
  filters?: QueueFilters;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

function optionalId(value: unknown) {
  if (value == null) {
    return undefined;
  }

  if (!isInteger(value) || value < 0) {
    throw new TypeError('Invalid queue record association');
  }

  return value;
}

function optionalString(value: unknown) {
  if (value == null || typeof value === 'string') {
    return value ?? undefined;
  }

  throw new TypeError('Invalid queue record text');
}

function optionalBoolean(value: unknown) {
  if (value == null || typeof value === 'boolean') {
    return value ?? undefined;
  }

  throw new TypeError('Invalid queue record flag');
}

function optionalNumber(value: unknown) {
  if (value == null) {
    return undefined;
  }

  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new TypeError('Invalid queue record quantity');
  }

  return value;
}

function parseQuality(value: unknown) {
  if (value == null) {
    return null;
  }

  if (!isRecord(value) || !isRecord(value.quality) || typeof value.quality.name !== 'string' ||
      !isRecord(value.revision)) {
    throw new TypeError('Invalid queue quality');
  }

  return {
    quality: { name: value.quality.name },
    revision: {
      real: optionalNumber(value.revision.real),
      version: optionalNumber(value.revision.version),
      isRepack: optionalBoolean(value.revision.isRepack)
    }
  };
}

function parseFormats(value: unknown) {
  if (value == null) {
    return [];
  }

  if (!Array.isArray(value)) {
    throw new TypeError('Invalid queue custom formats');
  }

  return value.map((format: unknown) => {
    if (!isRecord(format) || !isInteger(format.id) || typeof format.name !== 'string') {
      throw new TypeError('Invalid queue custom format');
    }

    return { id: format.id, name: format.name };
  });
}

function parseStatusMessages(value: unknown) {
  if (value == null) {
    return [];
  }

  if (!Array.isArray(value)) {
    throw new TypeError('Invalid queue status messages');
  }

  return value.map((entry: unknown) => {
    if (!isRecord(entry) || !Array.isArray(entry.messages)) {
      throw new TypeError('Invalid queue status message');
    }

    return {
      title: optionalString(entry.title) ?? '',
      messages: entry.messages.map(optionalString).filter((message) => message !== undefined)
    };
  });
}

function requiredBoolean(value: unknown): boolean {
  if (typeof value !== 'boolean') {
    throw new TypeError('Invalid queue action capability');
  }
  return value;
}

function parseAuthor(value: unknown) {
  if (value == null) {
    return undefined;
  }
  if (!isRecord(value) || typeof value.authorName !== 'string') {
    throw new TypeError('Invalid queue author');
  }
  return { authorName: value.authorName, titleSlug: optionalString(value.titleSlug) };
}

function parseRecordings(value: unknown) {
  if (!Array.isArray(value)) {
    throw new TypeError('Missing queue recording coverage');
  }
  const records = value.map((recording: unknown) => {
    if (!isRecord(recording) || !isInteger(recording.bookId) || recording.bookId < 1) {
      throw new TypeError('Invalid queue recording');
    }
    return {
      bookId: recording.bookId,
      recordingId: optionalString(recording.recordingId),
      title: optionalString(recording.title),
      titleSlug: optionalString(recording.titleSlug),
      releaseDate: optionalString(recording.releaseDate)
    };
  });
  if (new Set(records.map(({ bookId }) => bookId)).size !== records.length) {
    throw new TypeError('Duplicate queue recording');
  }
  return records;
}

function parseQueueItem(value: unknown) {
  if (!isRecord(value) || typeof value.id !== 'string' || !value.id ||
      typeof value.revision !== 'string' || !value.revision ||
      typeof value.title !== 'string' || typeof value.status !== 'string' ||
      (value.kind !== 'download' && value.kind !== 'pending' && value.kind !== 'unaddressable') ||
      (value.coverageSource !== 'recordedGrab' && value.coverageSource !== 'pendingRelease' &&
       value.coverageSource !== 'mappedCandidates' && value.coverageSource !== 'unknown')) {
    throw new TypeError('Invalid queue record');
  }

  const downloadClientId = optionalId(value.downloadClientId);
  const downloadId = optionalString(value.downloadId);
  if (value.kind === 'download' && (!downloadClientId || !downloadId ||
      value.id !== `download:${downloadClientId}:${downloadId}`)) {
    throw new TypeError('Invalid download identity');
  }
  if (value.kind === 'pending' && !/^pending:[1-9]\d*$/.test(value.id)) {
    throw new TypeError('Invalid pending release identity');
  }

  return {
    id: value.id,
    revision: value.revision,
    kind: value.kind,
    coverageSource: value.coverageSource,
    recordings: parseRecordings(value.recordings),
    author: parseAuthor(value.author),
    title: value.title,
    status: value.status,
    authorId: optionalId(value.authorId),
    downloadClientId,
    downloadId,
    canRemove: requiredBoolean(value.canRemove),
    canRemoveFromClient: requiredBoolean(value.canRemoveFromClient),
    canChangeCategory: requiredBoolean(value.canChangeCategory),
    canIgnore: requiredBoolean(value.canIgnore),
    canBlocklist: requiredBoolean(value.canBlocklist),
    canGrab: requiredBoolean(value.canGrab),
    canImport: requiredBoolean(value.canImport),
    quality: parseQuality(value.quality),
    customFormats: parseFormats(value.customFormats),
    customFormatScore: optionalNumber(value.customFormatScore) ?? 0,
    estimatedCompletionTime: optionalString(value.estimatedCompletionTime),
    timeleft: optionalString(value.timeleft),
    size: optionalNumber(value.size) ?? 0,
    sizeleft: optionalNumber(value.sizeleft) ?? 0,
    trackedDownloadStatus: optionalString(value.trackedDownloadStatus),
    trackedDownloadState: optionalString(value.trackedDownloadState),
    downloadClientAvailable: optionalBoolean(value.downloadClientAvailable),
    statusMessages: parseStatusMessages(value.statusMessages),
    errorMessage: optionalString(value.errorMessage),
    protocol: optionalString(value.protocol) ?? 'unknown',
    indexer: optionalString(value.indexer),
    outputPath: optionalString(value.outputPath),
    downloadClient: optionalString(value.downloadClient),
    downloadClientHasPostImportCategory: optionalBoolean(value.downloadClientHasPostImportCategory) ?? false,
    downloadForced: optionalBoolean(value.downloadForced) ?? false
  };
}

type QueueItem = ReturnType<typeof parseQueueItem>;
type QueueTarget = Pick<QueueItem, 'id' | 'revision'>;

interface RemovalOptions {
  remove: boolean;
  blocklist: boolean;
  skipRedownload: boolean;
  changeCategory: boolean;
}

interface ActionTargets {
  targets: readonly QueueTarget[];
  bulk: boolean;
  pageKey: string;
}

type QueueAction = ActionTargets & (
  | { kind: 'grab' }
  | { kind: 'remove'; options: RemovalOptions }
);

function parseQueuePage(value: unknown) {
  if (!isRecord(value) || !isInteger(value.page) || value.page < 1 ||
      !isInteger(value.pageSize) || value.pageSize < 1 ||
      !isInteger(value.totalRecords) || value.totalRecords < 0 || !Array.isArray(value.records)) {
    throw new TypeError('Invalid queue page');
  }

  const records = value.records.map(parseQueueItem);
  if (new Set(records.map((item) => item.id)).size !== records.length) {
    throw new TypeError('Duplicate queue record');
  }

  return { page: value.page, pageSize: value.pageSize, totalRecords: value.totalRecords, records };
}

export function queueTarget({ id, revision }: QueueItem): QueueTarget {
  return { id, revision };
}

export function queueTargetsAreCurrent(items: readonly QueueItem[], targets: readonly QueueTarget[]) {
  const current = new Map(items.map((item) => [item.id, item]));
  return targets.length > 0 && targets.every((target) => current.get(target.id)?.revision === target.revision);
}

export function submitQueueAction(action: QueueAction) {
  const targets = action.targets;
  const ids = targets.map(({ id }) => id);
  if (!ids.length || new Set(ids).size !== ids.length || (!action.bulk && ids.length !== 1) ||
      targets.some(({ id, revision }) => typeof id !== 'string' || !id || typeof revision !== 'string' || !revision)) {
    throw new TypeError('Invalid queue action targets');
  }

  if (action.kind === 'grab') {
    return fetchJson({ apiVersion: 2, path: '/queue/grab', method: 'POST', body: { targets }, timeout: actionTimeout, parse: () => null });
  }

  const { remove, blocklist, skipRedownload, changeCategory } = action.options;
  return fetchJson({
    apiVersion: 2, path: '/queue/remove', method: 'POST',
    body: { targets, removeFromClient: remove, blocklist, skipRedownload, changeCategory },
    timeout: actionTimeout, parse: () => null
  });
}

function parseQueueStatus(value: unknown) {
  if (!isRecord(value) || !isInteger(value.count) || !isInteger(value.totalCount) ||
      !isInteger(value.unknownCount) || value.count < 0 || value.unknownCount < 0 ||
      value.totalCount !== value.count + value.unknownCount) {
    throw new TypeError('Invalid queue status');
  }
  return {
    count: value.count,
    totalCount: value.totalCount,
    unknownCount: value.unknownCount,
    errors: requiredBoolean(value.errors),
    warnings: requiredBoolean(value.warnings),
    unknownErrors: requiredBoolean(value.unknownErrors),
    unknownWarnings: requiredBoolean(value.unknownWarnings)
  };
}

export function queueStatusQuery() {
  return queryOptions({
    queryKey: [...queuePageFamilyKey(), 'status'] as const,
    queryFn: ({ signal }) => fetchJson({ apiVersion: 2, path: '/queue/status', signal, timeout: 30_000, parse: parseQueueStatus }),
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    networkMode: 'always',
    retry: false
  });
}

export function queuePageFamilyKey() {
  return ['activityQueue', window.Readarr.apiRoot] as const;
}

function clearTimer() {
  if (refreshTimer !== null) {
    clearTimeout(refreshTimer);
    refreshTimer = null;
  }
}

export function clearQueueRefresh() {
  clearTimer();
  refreshPending = false;
}

function schedulePendingRefresh(refresh: () => void) {
  if (refreshPending && !refreshHolds.size && !refreshInFlight && refreshTimer === null) {
    refreshTimer = setTimeout(refresh, refreshDelay);
  }
}

export async function refreshQueuePages() {
  clearTimer();
  if (refreshHolds.size || refreshInFlight) {
    refreshPending = true;
    return;
  }

  refreshPending = false;
  refreshInFlight = true;
  const queryKey = queuePageFamilyKey();

  try {
    await queryClient.cancelQueries({ queryKey, type: 'active' });
    if (refreshHolds.size) {
      refreshPending = true;
    } else {
      await queryClient.invalidateQueries({ queryKey });
    }
  } finally {
    refreshInFlight = false;
    schedulePendingRefresh(refreshQueuePages);
  }
}

export function scheduleQueueRefresh() {
  refreshPending = true;
  schedulePendingRefresh(refreshQueuePages);
}

export function holdQueueRefresh() {
  const hold = Symbol();
  refreshHolds.add(hold);
  clearTimer();
  const queryKey = queuePageFamilyKey();

  if (queryClient.isFetching({ queryKey, type: 'active' })) {
    refreshPending = true;
    queryClient.cancelQueries({ queryKey, type: 'active' });
  }

  return () => {
    if (refreshHolds.delete(hold) && !refreshHolds.size && refreshPending) {
      scheduleQueueRefresh();
    }
  };
}

export default function queuePageQuery({
  page,
  pageSize,
  sortKey,
  sortDirection,
  includeUnknownAuthorItems,
  filters
}: QueuePageRequest) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    sortKey: sortAliases[sortKey] ?? sortKey,
    sortDirection,
    includeUnknownAuthorItems: String(includeUnknownAuthorItems)
  });

  if (filters?.status) {
    params.set('status', filters.status);
  }
  if (filters?.protocol) {
    params.set('protocol', filters.protocol);
  }

  const search = params.toString();

  return queryOptions({
    queryKey: [...queuePageFamilyKey(), search] as const,
    queryFn: ({ signal }) => fetchJson({ apiVersion: 2, path: `/queue?${search}`, signal, timeout: 30_000, parse: parseQueuePage }),
    placeholderData: keepPreviousData,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}
