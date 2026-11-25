import { createSelector } from 'reselect';
import { BookId } from 'Book/BookIdentity';
import { LibraryBook } from 'Book/LibraryModels';
import selectById, { ResourceCollection } from './selectById';

function createBookSelector() {
  return createSelector(
    (state: { books: ResourceCollection<LibraryBook> }) => state.books,
    (_state: unknown, { bookId }: { bookId: BookId | 0 | null | undefined }) => bookId,
    selectById
  );
}

export default createBookSelector;
