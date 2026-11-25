import { InvalidateQueryFilters, keepPreviousData, queryOptions } from '@tanstack/react-query';
import queryClient from 'App/queryClient';
import { type BookId, parseAssociationId } from 'Book/BookIdentity';
import fetchJson from 'Utilities/Fetch/fetchJson';
import { normalizeHistoryEventTypes, parseHistoryBookId } from './historyFilters';

const historyRefreshDelay = 100;
let historyRefreshTimer: ReturnType<typeof setTimeout> | null = null;

interface HistoryPageRequest {
  page: number;
  pageSize: number;
  sortKey: string;
  sortDirection: 'ascending' | 'descending';
  eventTypes: readonly string[];
  bookId?: BookId;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

function parseQuality(value: unknown) {
  if (value == null) {
    return null;
  }

  if (!isRecord(value) ||
      !isRecord(value.quality) ||
      typeof value.quality.name !== 'string' ||
      !isRecord(value.revision)) {
    throw new TypeError('Invalid history quality');
  }

  const { real, version, isRepack } = value.revision;
  if (!isInteger(real) || !isInteger(version) || typeof isRepack !== 'boolean') {
    throw new TypeError('Invalid history quality revision');
  }

  return { quality: { name: value.quality.name }, revision: { real, version, isRepack } };
}

function parseCustomFormats(value: unknown) {
  if (value == null) {
    return [];
  }

  if (!Array.isArray(value)) {
    throw new TypeError('Invalid history custom formats');
  }

  return value.map((format: unknown) => {
    if (!isRecord(format) || !isInteger(format.id) || typeof format.name !== 'string') {
      throw new TypeError('Invalid history custom format');
    }

    return { id: format.id, name: format.name };
  });
}

function parseEventData(value: unknown) {
  const data: Record<string, string | null> = Object.create(null);

  if (value != null) {
    if (!isRecord(value)) {
      throw new TypeError('Invalid history event data');
    }

    for (const [key, field] of Object.entries(value)) {
      if (typeof field === 'string') {
        data[key] = field;
      } else if (field === null) {
        data[key] = null;
      } else {
        throw new TypeError('Invalid history event data field');
      }
    }
  }

  return data;
}

function parseHistoryRecord(value: unknown) {
  if (!isRecord(value)) {
    throw new TypeError('Invalid history record');
  }

  const { id, bookId, authorId, date, eventType, sourceTitle, customFormatScore, qualityCutoffNotMet } = value;

  if (!isInteger(id) || id < 1 ||
      !isInteger(bookId) || bookId < 0 ||
      !isInteger(authorId) || authorId < 0 ||
      typeof date !== 'string' || !Number.isFinite(Date.parse(date)) ||
      (typeof eventType !== 'string' && !isInteger(eventType)) ||
      (sourceTitle != null && typeof sourceTitle !== 'string') ||
      !isInteger(customFormatScore) ||
      typeof qualityCutoffNotMet !== 'boolean') {
    throw new TypeError('Invalid history record fields');
  }

  return {
    id,
    bookId: parseAssociationId(bookId, 'book'),
    authorId: parseAssociationId(authorId, 'author'),
    date,
    // The JSON enum serializer emits a number for unknown legacy events.
    eventType: typeof eventType === 'string' ? eventType : 'unknown',
    sourceTitle: sourceTitle ?? '',
    customFormatScore,
    qualityCutoffNotMet,
    quality: parseQuality(value.quality),
    customFormats: parseCustomFormats(value.customFormats),
    data: parseEventData(value.data)
  };
}

function parseHistoryPage(value: unknown) {
  if (!isRecord(value) ||
      !isInteger(value.page) || value.page < 1 ||
      !isInteger(value.pageSize) || value.pageSize < 1 ||
      !isInteger(value.totalRecords) || value.totalRecords < 0 ||
      !Array.isArray(value.records)) {
    throw new TypeError('Invalid history page');
  }

  const { page, pageSize, totalRecords, records } = value;
  return { page, pageSize, totalRecords, records: records.map(parseHistoryRecord) };
}

export function historyPageFamilyKey() {
  return ['history', window.Readarr.apiRoot] as const;
}

export function invalidateHistoryPages({ refetchType }: Pick<InvalidateQueryFilters, 'refetchType'> = {}) {
  return queryClient.invalidateQueries({ queryKey: historyPageFamilyKey(), refetchType });
}

export function clearHistoryRefresh() {
  if (historyRefreshTimer !== null) {
    clearTimeout(historyRefreshTimer);
    historyRefreshTimer = null;
  }
}

export async function refreshHistoryPages() {
  clearHistoryRefresh();
  // Invalidation alone can reuse an in-flight first read with no cached result.
  await queryClient.cancelQueries({ queryKey: historyPageFamilyKey(), type: 'active' });
  await invalidateHistoryPages();
}

export function scheduleHistoryRefresh() {
  if (historyRefreshTimer === null) {
    historyRefreshTimer = setTimeout(() => {
      historyRefreshTimer = null;
      refreshHistoryPages();
    }, historyRefreshDelay);
  }
}

export function markHistoryAsFailed(id: number) {
  return fetchJson({
    path: `/history/failed/${id}`,
    method: 'POST',
    parse: () => null,
    timeout: 30_000
  });
}

export default function historyPageQuery(request: HistoryPageRequest) {
  const { page, pageSize, sortKey, sortDirection } = request;
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize),
    sortKey,
    sortDirection
  });

  for (const eventType of normalizeHistoryEventTypes(request.eventTypes)) {
    params.append('eventType', eventType);
  }

  const bookId = parseHistoryBookId(request.bookId);
  if (bookId !== undefined) {
    params.set('bookId', String(bookId));
  }

  const search = params.toString();
  return queryOptions({
    queryKey: [...historyPageFamilyKey(), search] as const,
    queryFn: ({ signal }) => fetchJson({
      path: `/history?${search}`,
      parse: parseHistoryPage,
      signal,
      timeout: 30_000
    }),
    placeholderData: keepPreviousData,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}
