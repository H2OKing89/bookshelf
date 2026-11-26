import { createSelector } from 'reselect';
import type { StateRecord } from 'Store/StateTypes';

interface TagDetails extends StateRecord { id: number }
function createTagDetailsSelector() {
  return createSelector(
    (_state: unknown, { id }: { id: number }) => id,
    (state: { tags: { details: { items: readonly TagDetails[] } } }) => state.tags.details.items,
    (id, items) => items.find((tag) => tag.id === id)
  );
}
export default createTagDetailsSelector;
