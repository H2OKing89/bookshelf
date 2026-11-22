import { useQuery } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useLayoutEffect, useState } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import withCurrentPage from 'Components/withCurrentPage';
import * as historyActions from 'Store/Actions/historyActions';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import History from './History';
import historyPageQueryOptions from './historyPageQuery';

function createMapStateToProps() {
  return createSelector(
    (state) => state.history,
    (state) => state.authors,
    (state) => state.books,
    (history, authors, books) => {
      return {
        isAuthorFetching: authors.isFetching,
        isAuthorPopulated: authors.isPopulated,
        isBooksFetching: books.isFetching,
        isBooksPopulated: books.isPopulated,
        booksError: books.error,
        ...history
      };
    }
  );
}

const mapDispatchToProps = {
  setHistoryFilter: historyActions.setHistoryFilter,
  setHistoryPage: historyActions.setHistoryPage,
  setHistorySort: historyActions.setHistorySort,
  setHistoryTableOption: historyActions.setHistoryTableOption
};

function HistoryConnector(props) {
  const {
    useCurrentPage,
    page: storedPage,
    pageSize,
    sortKey,
    sortDirection,
    selectedFilterKey,
    filters,
    setHistoryFilter,
    setHistoryPage,
    setHistorySort,
    setHistoryTableOption,
    ...otherProps
  } = props;

  // Ignore the remembered page for a new visit, before the query can fetch it.
  const [pageInitialized, setPageInitialized] = useState(useCurrentPage);
  const page = pageInitialized ? storedPage : 1;
  const eventTypes = findSelectedFilters(selectedFilterKey, filters)
    .filter((filter) => filter.key === 'eventType')
    .map((filter) => filter.value);
  const query = useQuery(historyPageQueryOptions({ page, pageSize, sortKey, sortDirection, eventTypes }));

  const totalRecords = query.data?.totalRecords ?? 0;
  const totalPages = Math.max(Math.ceil(totalRecords / (query.data?.pageSize ?? pageSize)), 1);
  const needsClamp = query.isSuccess && !query.isPlaceholderData && page > totalPages;

  useLayoutEffect(() => {
    if (!pageInitialized) {
      setHistoryPage(1);
      setPageInitialized(true);
    } else if (needsClamp) {
      setHistoryPage(totalPages);
    }
  }, [needsClamp, pageInitialized, setHistoryPage, totalPages]);

  const goToPage = (value) => setHistoryPage(Math.min(Math.max(value, 1), totalPages));

  const onRefreshPress = () => {
    if (page === 1) {
      query.refetch();
    } else {
      goToPage(1);
    }
  };

  return (
    <History
      {...otherProps}
      page={page}
      pageSize={pageSize}
      sortKey={sortKey}
      sortDirection={sortDirection}
      selectedFilterKey={selectedFilterKey}
      filters={filters}
      items={query.data?.records ?? []}
      totalPages={Math.max(page, totalPages)}
      totalRecords={totalRecords}
      isFetching={query.isPending || query.isFetching || needsClamp}
      isPopulated={!!query.data}
      canUseRowActions={query.isSuccess && !query.isPlaceholderData && !needsClamp}
      error={query.error}
      onRefreshPress={onRefreshPress}
      onFirstPagePress={() => goToPage(1)}
      onPreviousPagePress={() => goToPage(page - 1)}
      onNextPagePress={() => goToPage(page + 1)}
      onLastPagePress={() => goToPage(totalPages)}
      onPageSelect={goToPage}
      onSortPress={(nextKey, nextDirection) => setHistorySort({ sortKey: nextKey, sortDirection: nextDirection })}
      onFilterSelect={setHistoryFilter}
      onTableOptionChange={setHistoryTableOption}
    />
  );
}

HistoryConnector.propTypes = {
  useCurrentPage: PropTypes.bool.isRequired,
  page: PropTypes.number.isRequired,
  pageSize: PropTypes.number.isRequired,
  sortKey: PropTypes.string.isRequired,
  sortDirection: PropTypes.string.isRequired,
  selectedFilterKey: PropTypes.string.isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  setHistoryFilter: PropTypes.func.isRequired,
  setHistoryPage: PropTypes.func.isRequired,
  setHistorySort: PropTypes.func.isRequired,
  setHistoryTableOption: PropTypes.func.isRequired
};

export default withCurrentPage(
  connect(createMapStateToProps, mapDispatchToProps)(HistoryConnector)
);
