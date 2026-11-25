import { collectionIdentity } from '../Object/collectionIdentity';

export default function getIndexOfFirstCharacter<T extends object>(items: readonly T[], sortKey: keyof T & string, character: string): number {
  return items.findIndex((item) => {
    const value = collectionIdentity(item, sortKey);
    if (typeof value !== 'string') {
      return false;
    }
    const firstCharacter = value.charAt(0);
    return character === '#' ? !Number.isNaN(Number(firstCharacter)) : firstCharacter === character;
  });
}
