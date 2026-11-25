export function collectionIdentity(item: object, key: PropertyKey): unknown {
  return Reflect.get(item, key);
}
