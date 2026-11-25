import * as filterTypes from './filterTypes';

export type FilterPredicate = (itemValue: unknown, filterValue: unknown) => boolean;

function stringMatch(
  itemValue: unknown,
  filterValue: unknown,
  compare: (item: string, filter: string) => boolean
): boolean | undefined {
  if (typeof itemValue !== 'string' || typeof filterValue !== 'string') {
    return undefined;
  }

  return compare(itemValue.toLowerCase(), filterValue.toLowerCase());
}

function contains(item: unknown, filter: unknown): boolean | undefined {
  return Array.isArray(item) ?
    item.some((value: unknown) => value === filter) :
    stringMatch(item, filter, (a, b) => a.includes(b));
}

function relation(
  item: unknown,
  filter: unknown,
  compareNumbers: (a: number, b: number) => boolean,
  compareStrings: (a: string, b: string) => boolean
): boolean {
  if (typeof item === 'string' && typeof filter === 'string') {
    return compareStrings(item, filter);
  }
  const numeric = (value: unknown) =>
    typeof value === 'number' || typeof value === 'string' || typeof value === 'boolean' || value === null;
  return numeric(item) && numeric(filter) && compareNumbers(Number(item), Number(filter));
}

// Calendar/date predicates are supplied separately by dateFilterPredicate.
const filterTypePredicates: Partial<Record<filterTypes.FilterType, FilterPredicate>> = {
  [filterTypes.CONTAINS]: (item, filter) => contains(item, filter) === true,
  [filterTypes.NOT_CONTAINS]: (item, filter) => contains(item, filter) === false,
  [filterTypes.EQUAL]: (item, filter) => item === filter,
  [filterTypes.NOT_EQUAL]: (item, filter) => item !== filter,
  [filterTypes.GREATER_THAN]: (item, filter) => relation(item, filter, (a, b) => a > b, (a, b) => a > b),
  [filterTypes.GREATER_THAN_OR_EQUAL]: (item, filter) => relation(item, filter, (a, b) => a >= b, (a, b) => a >= b),
  [filterTypes.LESS_THAN]: (item, filter) => relation(item, filter, (a, b) => a < b, (a, b) => a < b),
  [filterTypes.LESS_THAN_OR_EQUAL]: (item, filter) => relation(item, filter, (a, b) => a <= b, (a, b) => a <= b),
  [filterTypes.STARTS_WITH]: (item, filter) => stringMatch(item, filter, (a, b) => a.startsWith(b)) === true,
  [filterTypes.NOT_STARTS_WITH]: (item, filter) => stringMatch(item, filter, (a, b) => a.startsWith(b)) === false,
  [filterTypes.ENDS_WITH]: (item, filter) => stringMatch(item, filter, (a, b) => a.endsWith(b)) === true,
  [filterTypes.NOT_ENDS_WITH]: (item, filter) => stringMatch(item, filter, (a, b) => a.endsWith(b)) === false
};

export default filterTypePredicates;
