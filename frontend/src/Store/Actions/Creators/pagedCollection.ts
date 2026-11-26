import { ASCENDING, DESCENDING,type SortDirection } from 'Helpers/Props/sortDirections';
import { isItemId, isStateRecord, type LegacyThunk, type StateRecord } from 'Store/StateTypes';

export type CollectionFetchThunk = (payload?: StateRecord) => LegacyThunk;

export function positiveInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : undefined;
}

export function isSortDirection(value: unknown): value is SortDirection {
  return value === ASCENDING || value === DESCENDING;
}

export function isFilterKey(value: unknown): value is string | number | null | undefined {
  return value == null || typeof value === 'string' || (typeof value === 'number' && Number.isFinite(value));
}

// Page responses are a separate boundary from the generic reducer, which
// also serves older callers. Only known pagination fields may reach it.
export function parseCollectionPage(response: unknown): StateRecord | null {
  if (!isStateRecord(response)) {
    return null;
  }
  const { page, pageSize, totalRecords, records, sortDirection, sortKey } = response;
  if (!positiveInteger(page) || !positiveInteger(pageSize) ||
      typeof totalRecords !== 'number' || !Number.isSafeInteger(totalRecords) || totalRecords < 0 ||
      !Array.isArray(records) || records.length > Number(pageSize) ||
      !records.every((record: unknown) => isStateRecord(record) && isItemId(record.id))) {
    return null;
  }
  if (new Set(records.map((record) => String(record.id))).size !== records.length) {
    return null;
  }

  return {
    page,
    pageSize,
    totalRecords,
    records,
    ...(typeof sortKey === 'string' ? { sortKey } : {}),
    ...(isSortDirection(sortDirection) ? { sortDirection } : {})
  };
}
