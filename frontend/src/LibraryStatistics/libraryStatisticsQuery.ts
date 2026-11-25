import { keepPreviousData, queryOptions } from '@tanstack/react-query';
import queryClient from 'App/queryClient';
import fetchJson from 'Utilities/Fetch/fetchJson';

export interface LibraryStatisticsFilters {
  authorId?: number;
  media: 'all' | 'audiobook' | 'ebook';
  source: 'all' | 'audible' | 'local' | 'other';
}

interface AuthorOption {
  id: number;
  name: string;
}

export interface StatisticsGroup {
  label: string;
  count: number;
}

const countFields = [
  'authorCount', 'bookCount', 'audioRecordingCount', 'ownedAudioRecordingCount',
  'ebookEditionCount', 'ownedEbookEditionCount', 'confirmedWorkCount', 'ownedConfirmedWorkCount',
  'fileCount', 'mappedFileCount', 'unresolvedFileCount', 'audioFileCount', 'ebookFileCount',
  'otherFileCount', 'sizeOnDisk', 'mappedSizeOnDisk', 'unresolvedSizeOnDisk', 'audioFilesWithKnownDuration'
] as const;
const groupFields = ['codecs', 'bitrates', 'languages', 'narrators', 'sources', 'mediaInfoStatus'] as const;

type CountField = typeof countFields[number];
type GroupField = typeof groupFields[number];
export type LibraryStatistics = Record<CountField, number> & Record<GroupField, StatisticsGroup[]> & {
  authors: AuthorOption[];
  knownAudioDurationSeconds: number | null;
};

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('Invalid library statistics object');
  }
  return value as Record<string, unknown>;
}

function count(value: unknown, minimum = 0): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum) {
    throw new Error('Invalid library statistics count');
  }
  return value;
}

function string(value: unknown): string {
  if (typeof value !== 'string') {
    throw new Error('Invalid library statistics label');
  }
  return value;
}

function array<T>(value: unknown, parse: (item: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw new Error('Invalid library statistics list');
  }
  return value.map(parse);
}

export function parseLibraryStatistics(value: unknown): LibraryStatistics {
  const model = object(value);
  const duration = model.knownAudioDurationSeconds;
  if (duration != null && (typeof duration !== 'number' || !Number.isFinite(duration) || duration <= 0)) {
    throw new Error('Invalid library statistics duration');
  }
  const counts = Object.fromEntries(countFields.map(key => [key, count(model[key])])) as Record<CountField, number>;
  const groups = Object.fromEntries(groupFields.map(key => [key, array(model[key], value => {
    const group = object(value);
    return { label: string(group.label), count: count(group.count, 1) };
  })])) as Record<GroupField, StatisticsGroup[]>;
  const authors = array(model.authors, (value) => {
    const author = object(value);
    return { id: count(author.id, 1), name: string(author.name) };
  });
  return { ...counts, ...groups, authors, knownAudioDurationSeconds: typeof duration === 'number' ? duration : null };
}

export function libraryStatisticsFamily() {
  return ['libraryStatistics', window.Readarr.apiRoot] as const;
}

export function libraryStatisticsQuery(filters: LibraryStatisticsFilters) {
  const search = new URLSearchParams({ media: filters.media, source: filters.source });
  if (filters.authorId != null) {
    search.set('authorId', String(filters.authorId));
  }
  return queryOptions({
    queryKey: [...libraryStatisticsFamily(), filters],
    queryFn: ({ signal }) => fetchJson({ path: `/librarystatistics?${search}`, parse: parseLibraryStatistics, signal, timeout: 30000 }),
    gcTime: 60000,
    placeholderData: keepPreviousData,
    networkMode: 'always',
    retry: false
  });
}

let timer: ReturnType<typeof setTimeout> | undefined = undefined;

export function clearLibraryStatisticsRefresh() {
  if (timer !== undefined) {
    clearTimeout(timer);
    timer = undefined;
  }
}

export async function refreshLibraryStatistics() {
  clearLibraryStatisticsRefresh();
  const queryKey = libraryStatisticsFamily();
  // A response already running when a change arrived cannot be treated as
  // fresh. Inactive filter scopes are marked stale without performing reads.
  await queryClient.cancelQueries({ queryKey, type: 'active' });
  await queryClient.invalidateQueries({ queryKey, refetchType: 'active' });
}

export function scheduleLibraryStatisticsRefresh() {
  if (timer === undefined) {
    timer = setTimeout(() => {
      timer = undefined;
      refreshLibraryStatistics();
    }, 250);
  }
}
