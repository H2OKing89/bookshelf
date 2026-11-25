import type { ThunkDispatch } from 'redux-thunk';

// The registered legacy slices still have different shapes. Their dynamic
// keys stay unknown here rather than presenting an incomplete AppState as
// the entire store or allowing unchecked values throughout new reducers.
export type StateRecord = Record<string, unknown>;
export type LegacyRootState = Record<string, StateRecord>;
export type ItemId = number | string;

export interface StoreAction {
  type: string;
  payload?: unknown;
  meta?: unknown;
  error?: boolean;
}

export type LegacyDispatch = ThunkDispatch<LegacyRootState, undefined, StoreAction>;

export type LegacyThunk<Result = unknown> = (
  dispatch: LegacyDispatch,
  getState: () => LegacyRootState
) => Result;

export function isStateRecord(value: unknown): value is StateRecord {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function asStateRecord(value: unknown): StateRecord {
  return isStateRecord(value) ? value : {};
}

export function isItemId(value: unknown): value is ItemId {
  return (typeof value === 'number' && Number.isFinite(value)) ||
    (typeof value === 'string' && value.length > 0);
}

export function getItemId(value: unknown): ItemId | undefined {
  return isStateRecord(value) && isItemId(value.id) ? value.id : undefined;
}

export function createItemMap(items: readonly unknown[]): Record<ItemId, number> {
  const entries: [ItemId, number][] = [];
  items.forEach((item, index) => {
    const id = getItemId(item);
    if (id !== undefined) {
      entries.push([id, index]);
    }
  });
  return Object.fromEntries(entries);
}

export function findItemIndex(
  items: readonly unknown[],
  itemMap: unknown,
  id: ItemId
): number {
  const index = isStateRecord(itemMap) && Object.hasOwn(itemMap, id) ? itemMap[id] : undefined;
  if (typeof index === 'number' && Number.isInteger(index) && index >= 0 &&
    index < items.length && getItemId(items[index]) === id) {
    return index;
  }

  return items.findIndex((item) => getItemId(item) === id);
}
