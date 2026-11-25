import { isStateRecord } from 'Store/StateTypes';
import { isFilterEntry } from './filterModels';

interface PresetFilter<Filter> {
  key: string | number;
  filters: Filter[];
}

interface CustomFilter<Filter> {
  id: number;
  filters: Filter[];
}

export default function findSelectedFilters<Filter>(
  selectedFilterKey: string | number | null | undefined,
  filters: PresetFilter<Filter>[] = [],
  customFilters: CustomFilter<Filter>[] = []
): Filter[] {
  if (selectedFilterKey == null || selectedFilterKey === '') {
    return [];
  }

  const selected = (Array.isArray(filters) ? filters : []).find(
    (filter) => isStateRecord(filter) && filter.key === selectedFilterKey
  ) ?? (Array.isArray(customFilters) ? customFilters : []).find(
    (filter) => isStateRecord(filter) && filter.id === selectedFilterKey
  );

  // Old/custom filter data is deserialized independently of these types.
  // Never apply a partly malformed filter as if it were a valid preset.
  return selected && Array.isArray(selected.filters) && selected.filters.every(isFilterEntry) ?
    selected.filters : [];
}
