import { createSelector } from 'reselect';
import type { LibraryAuthor, LibraryBook } from 'Book/LibraryModels';
import type { StateRecord } from 'Store/StateTypes';
import filterCollection from 'Utilities/Array/filterCollection';
import sortCollection from 'Utilities/Array/sortCollection';
import { sectionRecord } from './collectionProjection';
import createCustomFiltersSelector from './createCustomFiltersSelector';
import type { ResourceCollection } from './selectById';

interface BookCollectionItem extends LibraryBook {
  authorTitle?: string | null;
}

interface BookCollectionState extends StateRecord {
  books: ResourceCollection<BookCollectionItem> & StateRecord;
  authors: ResourceCollection<LibraryAuthor> & StateRecord;
}

function createBooksClientSideCollectionSelector(uiSection: string) {
  return createSelector(
    (state: BookCollectionState) => state.books,
    (state: BookCollectionState) => state.authors,
    (state: BookCollectionState) => sectionRecord(state, uiSection),
    createCustomFiltersSelector('books', uiSection),
    (bookState, authorState, uiSectionState, customFilters) => {
      const state = { ...bookState, ...uiSectionState, customFilters };

      // Build the join from canonical identities rather than trusting an old
      // itemMap. Never attach author metadata to a stored Redux book object;
      // the projected rows are owned by this selector.
      const authorsById = new Map(authorState.items.map((author) => [author.id, author]));
      const books = bookState.items.map((book) => ({
        ...book,
        author: book.authorId ? authorsById.get(book.authorId) : undefined
      }));

      const filtered = filterCollection(books, state);

      return {
        ...bookState,
        ...uiSectionState,
        customFilters,
        items: sortCollection(filtered, state),
        totalItems: books.length
      };
    }
  );
}

export default createBooksClientSideCollectionSelector;
