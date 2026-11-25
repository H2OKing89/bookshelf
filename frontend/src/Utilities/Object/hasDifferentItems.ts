import { collectionIdentity } from './collectionIdentity';

export default function hasDifferentItems<T extends { id: unknown }>(prevItems: readonly T[], currentItems: readonly T[], idProp?: 'id'): boolean;
export default function hasDifferentItems<T extends object, K extends keyof T>(prevItems: readonly T[], currentItems: readonly T[], idProp: K): boolean;
export default function hasDifferentItems<T extends object>(prevItems: readonly T[], currentItems: readonly T[], idProp: PropertyKey = 'id'): boolean {
  if (prevItems === currentItems) {
    return false;
  }
  if (prevItems.length !== currentItems.length) {
    return true;
  }
  const currentIds = new Set(currentItems.map((item) => collectionIdentity(item, idProp)));
  return prevItems.some((item) => !currentIds.has(collectionIdentity(item, idProp)));
}
