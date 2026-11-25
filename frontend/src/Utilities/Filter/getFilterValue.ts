import { isStateRecord } from 'Store/StateTypes';

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
  const filter = (Array.isArray(filters) ? filters : []).find(
    (group) => isStateRecord(group) && group.key === filterKey
  );
  const selected = Array.isArray(filter?.filters) ?
    filter.filters.find((entry) => isStateRecord(entry) && entry.key === filterValueKey) :
    undefined;
  return selected ? selected.value : defaultValue;
}
