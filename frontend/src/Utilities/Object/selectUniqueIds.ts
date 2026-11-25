export default function selectUniqueIds<T, K extends keyof T>(items: readonly T[], idProp: K): T[K][] {
  const ids = new Set<T[K]>();
  for (const item of items) {
    if (item[idProp]) {
      ids.add(item[idProp]);
    }
  }
  return [...ids];
}
