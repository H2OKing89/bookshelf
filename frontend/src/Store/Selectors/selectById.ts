import { findItemIndex } from 'Store/StateTypes';

export interface ResourceCollection<T> {
  items: readonly T[];
  itemMap?: Readonly<Record<number, number>>;
}

// Every map entry is checked against the actual row identity; outdated
// maps must never select an unrelated author, book or recording file.
export default function selectById<T extends { id: number }>(
  collection: ResourceCollection<T>,
  id: number | null | undefined
): T | undefined {
  if (id == null || !Number.isInteger(id) || id <= 0) {
    return undefined;
  }

  return collection.items[findItemIndex(collection.items, collection.itemMap, id)];
}
