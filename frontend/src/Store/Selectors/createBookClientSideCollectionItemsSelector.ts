import { createSelector } from 'reselect';
import createBooksClientSideCollectionSelector from './createBooksClientSideCollectionSelector';

function createBookClientSideCollectionItemsSelector(uiSection: string) {
  return createSelector(
    createBooksClientSideCollectionSelector(uiSection),
    (books) => ({
      ...books,
      items: books.items.map(({ id, title, authorTitle }) => ({ id, title, authorTitle }))
    })
  );
}

export default createBookClientSideCollectionItemsSelector;
