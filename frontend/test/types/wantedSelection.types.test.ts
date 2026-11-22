import type { BookId, EditionId } from 'Book/BookIdentity';
import { emptyWantedSelection, toggleWantedBook } from 'Wanted/wantedSelection';

export function checkWantedSelectionTypes(bookId: BookId, editionId: EditionId) {
  toggleWantedBook(emptyWantedSelection, bookId, true, false);
  // @ts-expect-error An edition ID must never become a Wanted selection target.
  toggleWantedBook(emptyWantedSelection, editionId, true, false);
}
