import { createSelector } from 'reselect';
import type { StateRecord } from 'Store/StateTypes';

interface RelatedQueueItem extends StateRecord { book?: { id: number } | null }
function createQueueItemSelector() {
  return createSelector(
    (_state: unknown, { bookId }: { bookId?: number | null }) => bookId,
    (state: { queue: { details: { items?: readonly RelatedQueueItem[] } } }) => state.queue.details.items,
    (bookId, details) => !bookId || !details ? null : details.find((item) => item.book?.id === bookId)
  );
}
export default createQueueItemSelector;
