import { asStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import pages from 'Utilities/pages';
import getSectionState from 'Utilities/State/getSectionState';
import { type CollectionFetchThunk, positiveInteger } from './pagedCollection';

type PageCommand = typeof pages[keyof typeof pages];

function createSetServerSideCollectionPageHandler(section: string, page: PageCommand, fetchHandler: CollectionFetchThunk) {
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const sectionState = asStateRecord(getSectionState(getState(), section, true));
    const currentPage = positiveInteger(sectionState.page) ?? 1;
    const totalPages = positiveInteger(sectionState.totalPages);
    let nextPage = currentPage;
    switch (page) {
      case pages.FIRST:
        nextPage = 1;
        break;
      case pages.PREVIOUS:
        nextPage = Math.max(1, currentPage - 1);
        break;
      case pages.NEXT:
        nextPage = Math.min(currentPage + 1, Number.MAX_SAFE_INTEGER);
        break;
      case pages.LAST:
        nextPage = totalPages ?? currentPage;
        break;
      case pages.EXACT:
        nextPage = positiveInteger(payload.page) ?? currentPage;
        break;
      default:
        break;
    }
    // The displayed page remains the server's last confirmed response. Never
    // issue negative/zero/fractional or known out-of-range page requests.
    dispatch(fetchHandler({ page: totalPages === undefined ? nextPage : Math.min(nextPage, totalPages) }));
  };
}

export default createSetServerSideCollectionPageHandler;
