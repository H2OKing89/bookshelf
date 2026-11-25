import _ from 'lodash';
import { sortDirections } from 'Helpers/Props';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';

function getSortClause<T extends object>(sortKey: unknown, sortDirection: unknown, sortPredicates: unknown): (item: T) => unknown {
  if (typeof sortKey !== 'string') {
    return () => undefined;
  }

  const predicates = asStateRecord(sortPredicates);
  const predicate = predicates[sortKey];

  if (Object.hasOwn(predicates, sortKey) && typeof predicate === 'function') {
    return (item) => predicate.call(predicates, item, sortDirection);
  }

  return (item) => {
    const value: unknown = Reflect.get(item, sortKey);
    return value;
  };
}

function sortCollection<T extends object>(items: readonly T[], state: StateRecord): T[] {
  const {
    sortKey, sortDirection, sortPredicates, secondarySortKey, secondarySortDirection
  } = state;

  const clauses = [getSortClause<T>(sortKey, sortDirection, sortPredicates)];
  const orders: ('asc' | 'desc')[] = [sortDirection === sortDirections.ASCENDING ? 'asc' : 'desc'];

  if (secondarySortKey && secondarySortDirection &&
      (sortKey !== secondarySortKey || sortDirection !== secondarySortDirection)) {
    clauses.push(getSortClause<T>(secondarySortKey, secondarySortDirection, sortPredicates));
    orders.push(secondarySortDirection === sortDirections.ASCENDING ? 'asc' : 'desc');
  }

  return _.orderBy(items, clauses, orders);
}

export default sortCollection;
