import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useLayoutEffect, useState } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import { executeCommand as dispatchCommand } from 'Store/Actions/commandActions';
import * as wantedActions from 'Store/Actions/wantedActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import getFilterValue from 'Utilities/Filter/getFilterValue';
import {
  refreshWantedPages,
  setWantedMonitoring,
  wantedFileDetailsQuery,
  wantedPageQuery,
  wantedQueueDetailsQuery,
  wantedViewKey
} from './wantedPageQuery';

const emptyItems = [];
const emptyFiles = {};

const searchCommands = {
  missing: commandNames.MISSING_BOOK_SEARCH,
  cutoffUnmet: commandNames.CUTOFF_UNMET_BOOK_SEARCH
};

function WantedPageConnector(props) {
  const {
    kind,
    View,
    useCurrentPage,
    page: storedPage,
    pageSize,
    sortKey,
    sortDirection,
    filters,
    selectedFilterKey,
    authorItemMap,
    setWantedPage,
    setWantedFilter,
    setWantedSort,
    setWantedTableOption,
    executeCommand,
    ...otherProps
  } = props;

  const [pageInitialized, setPageInitialized] = useState(useCurrentPage);
  const page = pageInitialized ? storedPage : 1;
  const monitored = getFilterValue(filters, selectedFilterKey, 'monitored', true);
  const request = { page, pageSize, sortKey, sortDirection, monitored };
  const query = useQuery(wantedPageQuery(kind, request));
  const items = query.data?.records.filter(({ authorId }) => Object.hasOwn(authorItemMap, authorId)) ?? emptyItems;
  const totalRecords = query.data?.totalRecords ?? 0;
  const totalPages = Math.max(Math.ceil(totalRecords / (query.data?.pageSize ?? pageSize)), 1);
  const needsClamp = query.isSuccess && !query.isPlaceholderData && page > totalPages;
  const isActivePage = query.isSuccess && !query.isPlaceholderData && !needsClamp;
  const activeIds = isActivePage ? items.map(({ id }) => id) : emptyItems;

  const queueDetails = useQuery(wantedQueueDetailsQuery(kind, activeIds));
  const fileDetails = useQuery(wantedFileDetailsQuery(kind,
    kind === 'cutoffUnmet' ? activeIds : emptyItems));

  const queryClient = useQueryClient();
  const mutationKey = ['wanted-monitoring', window.Readarr.apiRoot, kind];
  const isSaving = useIsMutating({ mutationKey, exact: true }) > 0;
  const monitorMutation = useMutation({
    mutationKey,
    mutationFn: ({ bookIds, monitored: value }) => setWantedMonitoring(bookIds, value),
    networkMode: 'always',
    onSuccess: () => refreshWantedPages()
  });
  const canUseRowActions = isActivePage && !isSaving;

  useLayoutEffect(() => {
    if (!pageInitialized) {
      setWantedPage(kind, 1);
      setPageInitialized(true);
    } else if (needsClamp) {
      setWantedPage(kind, totalPages);
    }
  }, [kind, needsClamp, pageInitialized, setWantedPage, totalPages]);

  const goToPage = (value) => setWantedPage(kind, Math.min(Math.max(value, 1), totalPages));
  const refresh = () => refreshWantedPages(kind);
  const areActiveIds = (ids) => canUseRowActions && ids.length > 0 &&
    ids.every((id) => activeIds.includes(id));
  const selectionScope = `${wantedViewKey(request)}|${items.map(({ id }) => id).sort((a, b) => a - b).join(',')}`;

  return (
    <View
      {...otherProps}
      page={page}
      pageSize={pageSize}
      sortKey={sortKey}
      sortDirection={sortDirection}
      filters={filters}
      selectedFilterKey={selectedFilterKey}
      items={items}
      totalPages={Math.max(page, totalPages)}
      totalRecords={totalRecords}
      isFetching={query.isPending || query.isFetching || needsClamp}
      isPopulated={!!query.data}
      error={query.error}
      monitorError={monitorMutation.error}
      isSaving={isSaving}
      canUseRowActions={canUseRowActions}
      selectionScope={selectionScope}
      queuedBookIds={isActivePage ? queueDetails.data ?? emptyItems : emptyItems}
      fileQualities={isActivePage ? fileDetails.data ?? emptyFiles : emptyFiles}
      onFirstPagePress={() => goToPage(1)}
      onPreviousPagePress={() => goToPage(page - 1)}
      onNextPagePress={() => goToPage(page + 1)}
      onLastPagePress={() => goToPage(totalPages)}
      onPageSelect={goToPage}
      onSortPress={(nextKey, nextDirection) => setWantedSort({ kind, sortKey: nextKey, sortDirection: nextDirection })}
      onFilterSelect={(key) => setWantedFilter(kind, key)}
      onTableOptionChange={(payload) => setWantedTableOption({ ...payload, kind })}
      onSearchSelectedPress={(bookIds) => {
        if (areActiveIds(bookIds)) {
          executeCommand({ name: commandNames.BOOK_SEARCH, bookIds, commandFinished: refresh });
        }
      }}
      onToggleSelectedPress={(bookIds) => {
        if (areActiveIds(bookIds) && !queryClient.isMutating({ mutationKey, exact: true })) {
          monitorMutation.mutate({ bookIds, monitored: !monitored });
        }
      }}
      onSearchAllPress={() => {
        if (canUseRowActions && activeIds.length) {
          executeCommand({ name: searchCommands[kind], commandFinished: refresh });
        }
      }}
    />
  );
}

WantedPageConnector.propTypes = {
  kind: PropTypes.oneOf(['missing', 'cutoffUnmet']).isRequired,
  View: PropTypes.elementType.isRequired,
  useCurrentPage: PropTypes.bool.isRequired,
  page: PropTypes.number.isRequired,
  pageSize: PropTypes.number.isRequired,
  sortKey: PropTypes.string.isRequired,
  sortDirection: PropTypes.string.isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  selectedFilterKey: PropTypes.string.isRequired,
  authorItemMap: PropTypes.object.isRequired,
  setWantedPage: PropTypes.func.isRequired,
  setWantedFilter: PropTypes.func.isRequired,
  setWantedSort: PropTypes.func.isRequired,
  setWantedTableOption: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default function createWantedPageConnector(kind, View) {
  function createMapStateToProps() {
    return createSelector(
      (state) => state.wanted[kind],
      (state) => state.authors,
      createCommandExecutingSelector(searchCommands[kind]),
      (settings, authors, isSearching) => ({
        ...settings,
        authorItemMap: authors.itemMap ?? emptyFiles,
        isAuthorFetching: authors.isFetching,
        isAuthorPopulated: authors.isPopulated,
        isSearching
      })
    );
  }

  function ConnectedWantedPage(props) {
    return <WantedPageConnector {...props} kind={kind} View={View} />;
  }

  return withCurrentPage(connect(createMapStateToProps, {
    ...wantedActions,
    executeCommand: dispatchCommand
  })(ConnectedWantedPage));
}
