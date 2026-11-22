import { useSyncExternalStore } from 'react';
import mergeColumnPreferences from 'Store/Middleware/mergeColumnPreferences';
import { getPreferenceStorage, isPreferenceRecord, readPreferenceStorage, writePreferenceStorage } from 'Store/Middleware/preferenceStorage';
import queueColumns from './queueColumns';
import { normalizeQueueFilters } from './queueFilters';

type SortDirection = 'ascending' | 'descending';

type QueueTableOptions = {
  pageSize?: number;
  columns?: typeof queueColumns;
};

const version = 1;
const sortKeys = new Set(['timeleft', ...queueColumns.filter(({ isSortable }) => isSortable).map(({ name }) => name)]);
const legacySortKeys = new Map([
  ['authors.sortName', 'authorMetadata.sortName'],
  ['book.title', 'books.title'],
  ['book.releaseDate', 'books.releaseDate']
]);

const defaults = {
  page: 1,
  pageSize: 20,
  sortKey: 'timeleft',
  sortDirection: 'ascending' as SortDirection,
  includeUnknownAuthorItems: true,
  columns: queueColumns
};

function validPageSize(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 5 && value <= 250;
}

function asSortKey(value: unknown) {
  if (typeof value !== 'string') {
    return defaults.sortKey;
  }

  const key = legacySortKeys.get(value) ?? value;
  return sortKeys.has(key) ? key : defaults.sortKey;
}

function migrateLegacyQueue(legacy: unknown): Record<string, unknown> {
  const queue = isPreferenceRecord(legacy) ? legacy : {};
  const options = isPreferenceRecord(queue.options) ? queue.options : {};
  const paged = isPreferenceRecord(queue.paged) ? queue.paged : {};

  return { ...paged, includeUnknownAuthorItems: options.includeUnknownAuthorItems };
}

export function createQueuePreferencesStore(storage: Storage | undefined, apiRoot: string, legacy: unknown) {
  const key = `bookshelf:queue-preferences:${apiRoot}`;
  const stored = readPreferenceStorage(storage, key);
  const supported = isPreferenceRecord(stored) && stored.version === version;
  const futureVersion = isPreferenceRecord(stored) && typeof stored.version === 'number' && stored.version > version;
  const preferences = supported ? stored : migrateLegacyQueue(legacy);
  let current = {
    ...defaults,
    pageSize: validPageSize(preferences.pageSize) ? preferences.pageSize : defaults.pageSize,
    sortKey: asSortKey(preferences.sortKey),
    sortDirection: preferences.sortDirection === 'descending' ? 'descending' as const : defaults.sortDirection,
    includeUnknownAuthorItems: typeof preferences.includeUnknownAuthorItems === 'boolean' ? preferences.includeUnknownAuthorItems : defaults.includeUnknownAuthorItems,
    columns: mergeColumnPreferences(queueColumns, preferences.columns),
    filters: normalizeQueueFilters(preferences.filters)
  };
  const listeners = new Set<() => void>();

  function save() {
    if (!futureVersion) {
      const { pageSize, sortKey, sortDirection, includeUnknownAuthorItems, columns, filters } = current;
      writePreferenceStorage(storage, key, {
        version,
        pageSize,
        sortKey,
        sortDirection,
        includeUnknownAuthorItems,
        filters,
        columns: columns.map(({ name, isVisible }) => ({ name, isVisible }))
      });
    }
  }

  if (!supported) {
    save();
  }

  function update(next: Partial<typeof current>, persist = true) {
    current = { ...current, ...next };
    if (persist) {
      save();
    }
    listeners.forEach((listener) => listener());
  }

  return {
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    getSnapshot: () => current,
    getIncludeUnknownAuthorItems: () => current.includeUnknownAuthorItems,
    setPage(page: number) {
      if (page !== current.page) {
        update({ page }, false);
      }
    },
    setSort(sortKey: string, sortDirection?: SortDirection) {
      const key = asSortKey(sortKey);
      const toggled = current.sortDirection === 'ascending' ? 'descending' : 'ascending';
      const direction = sortDirection ?? (key === current.sortKey ? toggled : current.sortDirection);
      update({ sortKey: key, sortDirection: direction });
    },
    setTableOptions({ pageSize, columns }: QueueTableOptions) {
      const size = validPageSize(pageSize) ? pageSize : current.pageSize;
      if (size !== current.pageSize || columns) {
        update({
          page: size === current.pageSize ? current.page : 1,
          pageSize: size,
          columns: columns ? mergeColumnPreferences(queueColumns, columns) : current.columns
        });
      }
    },
    setIncludeUnknownAuthorItems(includeUnknownAuthorItems: boolean) {
      if (includeUnknownAuthorItems !== current.includeUnknownAuthorItems) {
        update({ page: 1, includeUnknownAuthorItems });
      }
    },
    setFilters(value: unknown) {
      const filters = normalizeQueueFilters(value);

      if (filters.status !== current.filters.status || filters.protocol !== current.filters.protocol) {
        update({ page: 1, filters });
      }
    }
  };
}

let store = createQueuePreferencesStore(undefined, '', undefined);

export function initializeQueuePreferences(legacy: unknown) {
  store = createQueuePreferencesStore(getPreferenceStorage(), window.Readarr.apiRoot, legacy);
}

export function useQueuePreferences() {
  return useSyncExternalStore(store.subscribe, store.getSnapshot);
}

export function useIncludeUnknownAuthorItems() {
  return useSyncExternalStore(store.subscribe, store.getIncludeUnknownAuthorItems);
}

export function setQueuePage(page: number) {
  store.setPage(page);
}

export function setQueueSort(sortKey: string, sortDirection?: SortDirection) {
  store.setSort(sortKey, sortDirection);
}

export function setQueueTableOptions(options: QueueTableOptions) {
  store.setTableOptions(options);
}

export function setQueueIncludeUnknownAuthorItems(includeUnknownAuthorItems: boolean) {
  store.setIncludeUnknownAuthorItems(includeUnknownAuthorItems);
}

export function setQueueFilters(value: unknown) {
  store.setFilters(value);
}
