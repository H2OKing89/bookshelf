import { createSelector } from 'reselect';
import Author from 'Author/Author';
import { AuthorId } from 'Book/BookIdentity';
import selectById, { ResourceCollection } from './selectById';

function createAuthorSelector() {
  return createSelector(
    (state: { authors: ResourceCollection<Author> }) => state.authors,
    (_state: unknown, { authorId }: { authorId: AuthorId | 0 | null | undefined }) => authorId,
    selectById
  );
}

export default createAuthorSelector;
