import { collectionIdentity } from './collectionIdentity';

export default function hasDifferentItemsOrOrder<T extends { id: unknown }>(prevItems: readonly T[], currentItems: readonly T[], idProp?: 'id'): boolean;
export default function hasDifferentItemsOrOrder<T extends object, K extends keyof T>(prevItems: readonly T[], currentItems: readonly T[], idProp: K): boolean;
export default function hasDifferentItemsOrOrder<T extends object>(prevItems: readonly T[], currentItems: readonly T[], idProp: PropertyKey = 'id'): boolean {
  if (prevItems === currentItems) {
    return false;
  }
  if (prevItems.length !== currentItems.length) {
    return true;
  }
  return prevItems.some((item, index) => collectionIdentity(item, idProp) !== collectionIdentity(currentItems[index], idProp));
}
