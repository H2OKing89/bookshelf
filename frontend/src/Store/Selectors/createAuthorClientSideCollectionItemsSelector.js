import { createSelector } from 'reselect';
import createClientSideCollectionSelector from './createClientSideCollectionSelector';

function createAuthorClientSideCollectionItemsSelector(uiSection) {
  return createSelector(
    createClientSideCollectionSelector('authors', uiSection),
    (authors) => {
      const items = authors.items.map((s) => {
        const {
          id,
          sortName,
          sortNameLastFirst
        } = s;

        return {
          id,
          sortName,
          sortNameLastFirst
        };
      });

      return {
        ...authors,
        items
      };
    }
  );
}

export default createAuthorClientSideCollectionItemsSelector;
