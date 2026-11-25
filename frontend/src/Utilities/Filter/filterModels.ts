import * as filterTypes from 'Helpers/Props/filterTypes';
import { isStateRecord } from 'Store/StateTypes';

export function isFilterEntry(value: unknown): boolean {
  return isStateRecord(value) && typeof value.key === 'string';
}

export function isStoredCustomFilter(value: unknown): boolean {
  return isStateRecord(value) && typeof value.id === 'number' &&
    Number.isSafeInteger(value.id) && value.id > 0 &&
    typeof value.label === 'string' && typeof value.type === 'string' &&
    Array.isArray(value.filters) && value.filters.every(isFilterEntry) &&
    value.filters.every((entry: unknown) =>
      isStateRecord(entry) && (entry.type === undefined || filterTypes.all.some((type: string) => type === entry.type)));
}
