import { createSelector } from 'reselect';
import type { LibraryAuthor } from 'Book/LibraryModels';
import createBookSelector from './createBookSelector';
import selectById, { type ResourceCollection } from './selectById';

function createBookAuthorSelector() {
  return createSelector(
    createBookSelector(),
    (state: { authors: ResourceCollection<LibraryAuthor> }) => state.authors,
    (book, authors) => selectById(authors, book?.authorId)
  );
}

export default createBookAuthorSelector;
