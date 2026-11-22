import type { BookId } from 'Book/BookIdentity';
import { isPreferenceRecord, readPreferenceStorage, writePreferenceStorage } from '../../Store/Middleware/preferenceStorage';

const preferenceVersion = 1;
const maximumBookId = 2_147_483_647;

export const savedHistoryFilterKey = 'savedHistory';

export const historyEventFilters = [
  { key: 'grabbed', value: '1', label: 'Grabbed' },
  { key: 'bookFileImported', value: '3', label: 'Book Imported' },
  { key: 'failed', value: '4', label: 'Download Failed' },
  { key: 'importFailed', value: '7', label: 'Import Failed' },
  { key: 'deleted', value: '5', label: 'Deleted' },
  { key: 'renamed', value: '6', label: 'Renamed' },
  { key: 'retagged', value: '9', label: 'Retagged' },
  { key: 'ignored', value: '10', label: 'Ignored' }
] as const;

export const historyEventChoices = [
  ...historyEventFilters,
  { key: 'downloadImported', value: '8', label: 'Download Imported' }
] as const;

export type HistoryEventType = (typeof historyEventChoices)[number]['value'];

export interface HistoryFilters {
  bookId?: BookId;
  eventTypes: readonly HistoryEventType[];
}

export interface HistoryFilterPreference {
  active: boolean;
  filters: HistoryFilters;
}

const validEventTypes = new Set<string>(historyEventChoices.map(({ value }) => value));

export function parseHistoryBookId(value: unknown): BookId | undefined {
  const id = typeof value === 'string' && /^[1-9]\d*$/.test(value) ? Number(value) : value;

  if (typeof id === 'number' && Number.isSafeInteger(id) && id > 0 && id <= maximumBookId) {
    return id as BookId;
  }

  return undefined;
}

export function normalizeHistoryEventTypes(value: unknown): HistoryEventType[] {
  if (!Array.isArray(value)) {
    return [];
  }

  const supported = value.filter((eventType: unknown): eventType is HistoryEventType => {
    return typeof eventType === 'string' && validEventTypes.has(eventType);
  });

  return [...new Set(supported)].sort((left, right) => Number(left) - Number(right));
}

export function hasHistoryFilters(filters: HistoryFilters) {
  return filters.bookId !== undefined || filters.eventTypes.length > 0;
}

function historyFilterPreferenceKey(apiRoot: string) {
  return `bookshelf:history-filter:${apiRoot}`;
}

export function readHistoryFilterPreference(storage: Storage | undefined, apiRoot: string): HistoryFilterPreference {
  const saved = readPreferenceStorage(storage, historyFilterPreferenceKey(apiRoot));

  if (!isPreferenceRecord(saved) || saved.version !== preferenceVersion || !isPreferenceRecord(saved.filters)) {
    return { active: false, filters: { eventTypes: [] } };
  }

  const filters = {
    bookId: parseHistoryBookId(saved.filters.bookId),
    eventTypes: normalizeHistoryEventTypes(saved.filters.eventTypes)
  };

  return { active: saved.active === true && hasHistoryFilters(filters), filters };
}

export function saveHistoryFilterPreference(storage: Storage | undefined, apiRoot: string, preference: HistoryFilterPreference) {
  writePreferenceStorage(storage, historyFilterPreferenceKey(apiRoot), {
    version: preferenceVersion,
    ...preference
  });
}
