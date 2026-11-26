import { createSelector } from 'reselect';
import createAllAuthorsSelector from './createAllAuthorsSelector';

function createExistingAuthorSelector() {
  return createSelector(
    (_state: unknown, { titleSlug }: { titleSlug?: string | null }) => titleSlug,
    createAllAuthorsSelector(),
    (titleSlug, authors) => typeof titleSlug === 'string' && titleSlug.length > 0 && authors.some((author) => author.titleSlug === titleSlug)
  );
}
export default createExistingAuthorSelector;
