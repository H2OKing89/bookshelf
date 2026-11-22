import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import queryClient from 'App/queryClient';
import fetchJson from 'Utilities/Fetch/fetchJson';

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

function parseQueueItem(value: unknown) {
  if (!isRecord(value) || !isInteger(value.id) || value.id < 1 ||
      typeof value.title !== 'string' || typeof value.status !== 'string') {
    throw new TypeError('Invalid queue record');
  }

  return {
    id: value.id,
    title: value.title,
    status: value.status,
    authorId: optionalId(value.authorId),
    bookId: optionalId(value.bookId),
    downloadClientId: optionalId(value.downloadClientId),
    downloadId: optionalString(value.downloadId),
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
type QueueTarget = Pick<QueueItem, 'id' | 'authorId' | 'bookId' | 'downloadClientId' | 'downloadId'>;

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

export function queueTarget({ id, authorId, bookId, downloadClientId, downloadId }: QueueItem): QueueTarget {
  return { id, authorId, bookId, downloadClientId, downloadId };
}

export function queueTargetsAreCurrent(items: readonly QueueItem[], targets: readonly QueueTarget[]) {
  const current = new Map(items.map((item) => [item.id, item]));
  return targets.length > 0 && targets.every((target) => {
    const item = current.get(target.id);
    return item !== undefined && item.authorId === target.authorId && item.bookId === target.bookId &&
      item.downloadClientId === target.downloadClientId && item.downloadId === target.downloadId;
  });
}

export function submitQueueAction(action: QueueAction) {
  const ids = action.targets.map(({ id }) => id);
  if (!ids.length || new Set(ids).size !== ids.length || (!action.bulk && ids.length !== 1) ||
      ids.some((id) => !isInteger(id) || id < 1)) {
    throw new TypeError('Invalid queue action targets');
  }

  const selection = action.bulk ? 'bulk' : String(ids[0]);
  const body = action.bulk ? { ids } : undefined;

  if (action.kind === 'grab') {
    return fetchJson({ path: `/queue/grab/${selection}`, method: 'POST', body, timeout: actionTimeout, parse: () => null });
  }

  const { remove, blocklist, skipRedownload, changeCategory } = action.options;
  const params = new URLSearchParams({
    removeFromClient: String(remove),
    blocklist: String(blocklist),
    skipRedownload: String(skipRedownload),
    changeCategory: String(changeCategory)
  });

  return fetchJson({ path: `/queue/${selection}?${params}`, method: 'DELETE', body, timeout: actionTimeout, parse: () => null });
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
  includeUnknownAuthorItems
}: QueuePageRequest) {
  const search = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    sortKey: sortAliases[sortKey] ?? sortKey,
    sortDirection,
    includeUnknownAuthorItems: String(includeUnknownAuthorItems)
  }).toString();

  return queryOptions({
    queryKey: [...queuePageFamilyKey(), search] as const,
    queryFn: ({ signal }) => fetchJson({ path: `/queue?${search}`, signal, timeout: 30_000, parse: parseQueuePage }),
    placeholderData: keepPreviousData,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}
