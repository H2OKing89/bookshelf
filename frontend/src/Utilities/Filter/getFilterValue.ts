interface FilterGroup<Value> {
  key: string | number;
  filters: { key: string; value: Value }[];
}

export default function getFilterValue<Value, Default>(
  filters: FilterGroup<Value>[],
  filterKey: string | number,
  filterValueKey: string,
  defaultValue: Default
): Value | Default {
  const filter = filters.find((group) => group.key === filterKey);
  const selected = filter?.filters.find((entry) => entry.key === filterValueKey);
  return selected ? selected.value : defaultValue;
}
