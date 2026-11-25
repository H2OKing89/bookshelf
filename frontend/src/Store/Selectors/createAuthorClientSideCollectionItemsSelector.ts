import { createSelector } from 'reselect';
import { getItemId } from 'Store/StateTypes';
import createClientSideCollectionSelector from './createClientSideCollectionSelector';

function createAuthorClientSideCollectionItemsSelector(uiSection: string) {
  return createSelector(
    createClientSideCollectionSelector('authors', uiSection),
    (authors) => {
      const items = authors.items.flatMap((author) => {
        const id = getItemId(author);

        if (id === undefined) {
          return [];
        }

        return [{
          id,
          sortName: typeof author.sortName === 'string' ? author.sortName : undefined,
          sortNameLastFirst: typeof author.sortNameLastFirst === 'string' ? author.sortNameLastFirst : undefined
        }];
      });

      return { ...authors, items };
    }
  );
}

export default createAuthorClientSideCollectionItemsSelector;
