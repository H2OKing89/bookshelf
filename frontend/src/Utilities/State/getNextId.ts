import { getItemId } from 'Store/StateTypes';

function getNextId(items: readonly unknown[]): number | undefined {
  const max = items.reduce<number>((highest, item) => {
    const id = getItemId(item);
    return typeof id === 'number' && Number.isSafeInteger(id) ? Math.max(highest, id) : highest;
  }, 1);
  return max < Number.MAX_SAFE_INTEGER ? max + 1 : undefined;
}

export default getNextId;
