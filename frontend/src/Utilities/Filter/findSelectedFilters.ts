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

  const selected = filters.find((filter) => filter.key === selectedFilterKey) ??
    customFilters.find((filter) => filter.id === selectedFilterKey);

  return selected?.filters ?? [];
}
