import type { ThunkHandler } from 'Store/thunks';
import pages from 'Utilities/pages';
import serverSideCollectionHandlers from 'Utilities/serverSideCollectionHandlers';
import createFetchServerSideCollectionHandler, { type FetchDataAugmenter } from './createFetchServerSideCollectionHandler';
import createSetServerSideCollectionFilterHandler from './createSetServerSideCollectionFilterHandler';
import createSetServerSideCollectionPageHandler from './createSetServerSideCollectionPageHandler';
import createSetServerSideCollectionSortHandler from './createSetServerSideCollectionSortHandler';
import type { CollectionFetchThunk } from './pagedCollection';

type HandlerName = typeof serverSideCollectionHandlers[keyof typeof serverSideCollectionHandlers];
type ActionHandlers = Partial<Record<HandlerName, string>> & { fetch: string };

function createServerSideCollectionHandlers(section: string, url: string, fetchThunk: CollectionFetchThunk, handlers: ActionHandlers, fetchDataAugmenter?: FetchDataAugmenter) {
  const actionHandlers: Record<string, ThunkHandler> = {
    [handlers[serverSideCollectionHandlers.FETCH]]: createFetchServerSideCollectionHandler(section, url, fetchDataAugmenter)
  };
  const pageHandlers = [
    [serverSideCollectionHandlers.FIRST_PAGE, pages.FIRST],
    [serverSideCollectionHandlers.PREVIOUS_PAGE, pages.PREVIOUS],
    [serverSideCollectionHandlers.NEXT_PAGE, pages.NEXT],
    [serverSideCollectionHandlers.LAST_PAGE, pages.LAST],
    [serverSideCollectionHandlers.EXACT_PAGE, pages.EXACT]
  ] as const;
  for (const [handler, page] of pageHandlers) {
    const action = handlers[handler];
    if (action) {
      actionHandlers[action] = createSetServerSideCollectionPageHandler(section, page, fetchThunk);
    }
  }
  const sort = handlers[serverSideCollectionHandlers.SORT];
  if (sort) {
    actionHandlers[sort] = createSetServerSideCollectionSortHandler(section, fetchThunk);
  }
  const filter = handlers[serverSideCollectionHandlers.FILTER];
  if (filter) {
    actionHandlers[filter] = createSetServerSideCollectionFilterHandler(section, fetchThunk);
  }
  return actionHandlers;
}

export default createServerSideCollectionHandlers;
