import { createSelector } from 'reselect';
import createAllAuthorsSelector from './createAllAuthorsSelector';
import type { AuthorCollection } from './selectorModels';

function createAuthorCountSelector() {
  return createSelector(
    createAllAuthorsSelector(),
    (state: { authors: AuthorCollection }) => state.authors.error,
    (state: { authors: AuthorCollection }) => state.authors.isFetching,
    (state: { authors: AuthorCollection }) => state.authors.isPopulated,
    (authors, error, isFetching, isPopulated) => ({ count: authors.length, error, isFetching, isPopulated })
  );
}
export default createAuthorCountSelector;
