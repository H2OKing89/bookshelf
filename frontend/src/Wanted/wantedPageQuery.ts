import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import queryClient from 'App/queryClient';
import { type BookId, parseAssociationId, parseResourceId } from 'Book/BookIdentity';
import { parseLibraryBook, parseLibraryBookFile } from 'Book/LibraryModels';
import fetchJson from 'Utilities/Fetch/fetchJson';

export type WantedKind = 'missing' | 'cutoffUnmet';

interface WantedPageRequest {
  page: number;
  pageSize: number;
  sortKey: string;
  sortDirection: 'ascending' | 'descending';
  monitored: boolean;
}

const routes = {
  missing: '/wanted/missing',
  cutoffUnmet: '/wanted/cutoff'
};

const queryTimeout = 30_000;
const refreshDelay = 100;
const kinds: WantedKind[] = ['missing', 'cutoffUnmet'];
const refreshTimers = new Map<WantedKind | 'queue', ReturnType<typeof setTimeout>>();

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value);
}

function isId(value: unknown): value is number {
  return isInteger(value) && value > 0;
}

function parseWantedBook(value: unknown) {
  const book = parseLibraryBook(value);
  if (!book.authorId || typeof book.title !== 'string' || typeof book.titleSlug !== 'string' ||
      typeof book.monitored !== 'boolean') {
    throw new TypeError('Invalid Wanted book fields');
  }
  return {
    id: book.id,
    authorId: book.authorId,
    title: book.title,
    titleSlug: book.titleSlug,
    monitored: book.monitored,
    releaseDate: book.releaseDate ?? undefined,
    lastSearchTime: book.lastSearchTime ?? undefined,
    disambiguation: book.disambiguation ?? undefined
  };
}

function parseWantedPage(value: unknown) {
  if (!isRecord(value) || !isId(value.page) || !isId(value.pageSize) ||
      !isInteger(value.totalRecords) || value.totalRecords < 0 || !Array.isArray(value.records)) {
    throw new TypeError('Invalid Wanted page');
  }

  const { page, pageSize, totalRecords, records } = value;
  return { page, pageSize, totalRecords, records: records.map(parseWantedBook) };
}

function parseQueueBooks(value: unknown, requested: readonly BookId[]) {
  if (!Array.isArray(value)) {
    throw new TypeError('Invalid Wanted queue details');
  }

  const bookIds = new Set<BookId>();

  const rows: unknown[] = value;
  for (const item of rows) {
    if (!isRecord(item) || !isInteger(item.id)) {
      throw new TypeError('Invalid Wanted queue row');
    }

    const direct = parseAssociationId(item.bookId, 'book');
    const nested = isRecord(item.book) ? parseAssociationId(item.book.id, 'book') : undefined;
    if (direct && nested && direct !== nested) {
      throw new TypeError('Mismatched Wanted queue book identity');
    }
    const bookId = direct || nested;
    if (bookId && requested.includes(bookId)) {
      bookIds.add(bookId);
    }
  }

  return [...bookIds];
}

function parseFileQualities(value: unknown, requested: readonly BookId[]) {
  if (!Array.isArray(value)) {
    throw new TypeError('Invalid Wanted file details');
  }

  const files: Partial<Record<BookId, string[]>> = {};

  const rows: unknown[] = value;
  for (const item of rows) {
    const file = parseLibraryBookFile(item);
    if (file.bookId && requested.includes(file.bookId) && file.quality) {
      const name = file.quality.quality.name;
      const qualities = files[file.bookId] ?? [];
      if (!qualities.includes(name)) {
        (files[file.bookId] ??= qualities).push(name);
      }
    }
  }

  return files;
}

function wantedFamily(kind: WantedKind) {
  return ['wanted', window.Readarr.apiRoot, kind] as const;
}

export function wantedViewKey(request: WantedPageRequest) {
  return new URLSearchParams({
    page: String(request.page),
    pageSize: String(request.pageSize),
    sortKey: request.sortKey,
    sortDirection: request.sortDirection,
    monitored: String(request.monitored)
  }).toString();
}

export function wantedPageQuery(kind: WantedKind, request: WantedPageRequest) {
  const search = wantedViewKey(request);
  return queryOptions({
    queryKey: [...wantedFamily(kind), 'page', search] as const,
    queryFn: ({ signal }) => fetchJson({
      path: `${routes[kind]}?${search}`,
      parse: parseWantedPage,
      signal,
      timeout: queryTimeout
    }),
    placeholderData: keepPreviousData,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}

function wantedDetailsQuery<T>(
  kind: WantedKind,
  type: 'queue' | 'files',
  ids: readonly BookId[],
  path: string,
  parameter: string,
  parse: (value: unknown, requested: readonly BookId[]) => T
) {
  const selected = [...new Set(ids.map((id) => parseResourceId(id, 'book')))].sort((a, b) => a - b);
  const search = new URLSearchParams();
  selected.forEach((id) => search.append(parameter, String(id)));

  return queryOptions({
    queryKey: [...wantedFamily(kind), type, selected] as const,
    queryFn: ({ signal }) => {
      if (!selected.length) {
        return Promise.resolve(parse([], selected));
      }

      return fetchJson({
        path: `${path}?${search}`,
        parse: (value) => parse(value, selected),
        signal,
        timeout: queryTimeout
      });
    },
    enabled: selected.length > 0,
    networkMode: 'always',
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
    retry: false
  });
}

export function wantedQueueDetailsQuery(kind: WantedKind, ids: readonly BookId[]) {
  return wantedDetailsQuery(kind, 'queue', ids, '/queue/details', 'bookIds', parseQueueBooks);
}

export function wantedFileDetailsQuery(kind: WantedKind, ids: readonly BookId[]) {
  return wantedDetailsQuery(kind, 'files', ids, '/bookfile', 'bookId', parseFileQualities);
}

function cancelTimer(key: WantedKind | 'queue') {
  const timer = refreshTimers.get(key);
  if (timer !== undefined) {
    clearTimeout(timer);
    refreshTimers.delete(key);
  }
}

async function refreshFamily(kind: WantedKind, detailsOnly = false) {
  const queryKey = detailsOnly ? [...wantedFamily(kind), 'queue'] : wantedFamily(kind);
  await queryClient.cancelQueries({ queryKey, type: 'active' });
  await queryClient.invalidateQueries({ queryKey, refetchType: 'active' });
}

export function clearWantedRefresh() {
  refreshTimers.forEach((_, key) => cancelTimer(key));
}

export async function refreshWantedPages(kind?: WantedKind) {
  const selected = kind ? [kind] : kinds;
  if (!kind) {
    cancelTimer('queue');
  }
  selected.forEach(cancelTimer);
  await Promise.all(selected.map((item) => refreshFamily(item)));
}

function schedule(key: WantedKind | 'queue', refresh: () => Promise<unknown>) {
  if (!refreshTimers.has(key)) {
    refreshTimers.set(key, setTimeout(() => {
      refreshTimers.delete(key);
      refresh();
    }, refreshDelay));
  }
}

export function scheduleWantedRefresh(kind?: WantedKind) {
  for (const item of kind ? [kind] : kinds) {
    schedule(item, () => refreshFamily(item));
  }
}

export function scheduleWantedQueueDetailsRefresh() {
  schedule('queue', () => Promise.all(kinds
    .filter((kind) => !refreshTimers.has(kind))
    .map((kind) => refreshFamily(kind, true))));
}

export function setWantedMonitoring(bookIds: readonly BookId[], monitored: boolean) {
  return fetchJson({
    path: '/book/monitor',
    method: 'PUT',
    body: { bookIds: bookIds.map((id) => parseResourceId(id, 'book')), monitored },
    parse: () => null,
    timeout: queryTimeout
  });
}
