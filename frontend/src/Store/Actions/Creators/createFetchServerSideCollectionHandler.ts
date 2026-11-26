import { batchActions } from 'redux-batched-actions';
import { asStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import getSectionState from 'Utilities/State/getSectionState';
import { set, updateServerSideCollection } from '../baseActions';
import { requestLegacy } from './legacyRequest';
import { isFilterKey, isSortDirection, parseCollectionPage, positiveInteger } from './pagedCollection';
import { isAborted } from './providerRequests';
import { createSectionRequests } from './sectionRequests';

export type FetchDataAugmenter = (getState: () => LegacyRootState, payload: StateRecord, data: StateRecord) => void;
const requests = createSectionRequests();

export function cancelServerSideCollection(getState: () => LegacyRootState, section: string) {
  requests.cancel(getState, section);
}

function createFetchServerSideCollectionHandler(section: string, url: string, fetchDataAugmenter?: FetchDataAugmenter) {
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const sectionState = asStateRecord(getSectionState(getState(), section, true));
    const { pageSize, sortDirection, sortKey, selectedFilterKey, filters, customFilters } = sectionState;
    const data: StateRecord = {
      page: positiveInteger(payload.page) ?? positiveInteger(sectionState.page) ?? 1,
      ...(positiveInteger(pageSize) ? { pageSize } : {}),
      ...(isSortDirection(sortDirection) ? { sortDirection } : {}),
      ...(typeof sortKey === 'string' && sortKey ? { sortKey } : {})
    };
    fetchDataAugmenter?.(getState, payload, data);

    const selectedFilters = isFilterKey(selectedFilterKey) ? findSelectedFilters(selectedFilterKey, filters, customFilters) : [];
    selectedFilters.forEach((filter) => {
      // Filter data must not override pagination controls or object prototypes.
      if (!['page', 'pageSize', 'sortDirection', 'sortKey', '__proto__', 'prototype', 'constructor'].includes(filter.key)) {
        data[filter.key] = filter.value;
      }
    });

    const attempt = requests.begin(getState, section);
    dispatch(set({ section, isFetching: true }));
    function failed(error: unknown) {
      if (attempt.finish()) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error }));
      }
    }

    try {
      const { request, abortRequest } = requestLegacy({ url, data });
      attempt.attachAbort(abortRequest);
      request.done((response) => {
        const page = parseCollectionPage(response);
        if (!page) {
          failed(new ApiError({ kind: 'invalid-response' }));
          return;
        }
        if (attempt.finish()) {
          dispatch(batchActions([
            updateServerSideCollection({ section, data: page }),
            set({ section, isFetching: false, isPopulated: true, error: null })
          ]));
        }
      });
      request.fail((xhr) => failed(isAborted(xhr) ? null : xhr));
    } catch {
      failed(new ApiError({ kind: 'invalid-request' }));
    }
  };
}

export default createFetchServerSideCollectionHandler;
