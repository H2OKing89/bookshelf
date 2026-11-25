import { filterTypePredicates, filterTypes } from 'Helpers/Props';
import type { FilterType } from 'Helpers/Props/filterTypes';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';

function isFilterType(value: unknown): value is FilterType {
  return filterTypes.all.some((type) => type === value);
}

function matchesValues(value: unknown, type: FilterType, predicate: (value: unknown) => boolean): boolean {
  if (!Array.isArray(value)) {
    return predicate(value);
  }

  const values: unknown[] = value;

  // Negative multi-value selections require all conditions, while positive
  // selections accept any matching value. Empty arrays keep their semantics.
  return type === filterTypes.NOT_CONTAINS || type === filterTypes.NOT_EQUAL ?
    values.every(predicate) :
    values.some(predicate);
}

function filterCollection<T extends object>(items: readonly T[], state: StateRecord): readonly T[] {
  const { selectedFilterKey, filters, customFilters } = state;

  if (!selectedFilterKey ||
      (typeof selectedFilterKey !== 'string' && typeof selectedFilterKey !== 'number')) {
    return items;
  }

  const selectedFilters = findSelectedFilters(selectedFilterKey, filters, customFilters);
  const predicates = asStateRecord(state.filterPredicates);

  return items.filter((item) => selectedFilters.every((rule) => {
    const type = rule.type === undefined ? filterTypes.EQUAL : rule.type;

    // A retired or malformed custom filter must not broaden the selection.
    if (!isFilterType(type)) {
      return false;
    }

    if (Object.hasOwn(predicates, rule.key)) {
      const predicate = predicates[rule.key];

      return typeof predicate === 'function' &&
        matchesValues(rule.value, type, (value) => Boolean(predicate(item, value, type)));
    }

    const predicate = filterTypePredicates[type];

    if (!Object.hasOwn(item, rule.key) || typeof predicate !== 'function') {
      return false;
    }

    const itemValue: unknown = Reflect.get(item, rule.key);
    return matchesValues(rule.value, type, (value) => predicate(itemValue, value));
  }));
}

export default filterCollection;
