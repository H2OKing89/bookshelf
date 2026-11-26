import { createSelector } from 'reselect';
import type { AuthorCollection } from './selectorModels';

function createAllAuthorsSelector() {
  return createSelector(
    (state: { authors: AuthorCollection }) => state.authors,
    (authors) => authors.items
  );
}
export default createAllAuthorsSelector;
