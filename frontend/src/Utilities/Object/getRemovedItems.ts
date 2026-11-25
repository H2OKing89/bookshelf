import { collectionIdentity } from './collectionIdentity';

export default function getRemovedItems<T extends { id: unknown }>(prevItems: readonly T[], currentItems: readonly T[], idProp?: 'id'): T[];
export default function getRemovedItems<T extends object, K extends keyof T>(prevItems: readonly T[], currentItems: readonly T[], idProp: K): T[];
export default function getRemovedItems<T extends object>(prevItems: readonly T[], currentItems: readonly T[], idProp: PropertyKey = 'id'): T[] {
  if (prevItems === currentItems) {
    return [];
  }
  const currentItemIds = new Set(currentItems.map((item) => collectionIdentity(item, idProp)));
  return prevItems.filter((item) => !currentItemIds.has(collectionIdentity(item, idProp)));
}
