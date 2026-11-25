import { isStateRecord } from 'Store/StateTypes';
import { type FilterEntry, isFilterEntry } from './filterModels';

export default function findSelectedFilters(
  selectedFilterKey: string | number | null | undefined,
  filters: unknown = [],
  customFilters: unknown = []
): FilterEntry[] {
  if (selectedFilterKey == null || selectedFilterKey === '') {
    return [];
  }

  const presetGroups: unknown[] = Array.isArray(filters) ? filters : [];
  const customGroups: unknown[] = Array.isArray(customFilters) ? customFilters : [];
  const selected = presetGroups.find(
    (filter) => isStateRecord(filter) && filter.key === selectedFilterKey
  ) ?? customGroups.find(
    (filter) => isStateRecord(filter) && filter.id === selectedFilterKey
  );

  // Old/custom filter data is deserialized independently of these types.
  // Never apply a partly malformed filter as if it were a valid preset.
  if (!isStateRecord(selected) || !Array.isArray(selected.filters)) {
    return [];
  }

  const entries: unknown[] = selected.filters;
  return entries.every(isFilterEntry) ? entries : [];
}
