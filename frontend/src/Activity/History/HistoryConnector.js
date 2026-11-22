import { useQuery } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useLayoutEffect, useState } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import withCurrentPage from 'Components/withCurrentPage';
import * as historyActions from 'Store/Actions/historyActions';
import { getPreferenceStorage } from 'Store/Middleware/preferenceStorage';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import History from './History';
import { hasHistoryFilters, readHistoryFilterPreference, savedHistoryFilterKey, saveHistoryFilterPreference } from './historyFilters';
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
        booksForFilter: books.items,
        authorsForFilter: authors.items,
        ...history,
        selectedFilterKey: history.filters.some(({ key }) => key === history.selectedFilterKey) ?
          history.selectedFilterKey : 'all'
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
  const apiRoot = window.Readarr.apiRoot;
  const [saved, setSaved] = useState(() => readHistoryFilterPreference(getPreferenceStorage(), apiRoot));

  // Ignore the remembered page for a new visit, before the query can fetch it.
  const [pageInitialized, setPageInitialized] = useState(useCurrentPage);
  const page = pageInitialized ? storedPage : 1;
  const presetEventTypes = findSelectedFilters(selectedFilterKey, filters)
    .filter((filter) => filter.key === 'eventType')
    .map((filter) => filter.value);
  const activeFilters = saved.active ? saved.filters : { eventTypes: presetEventTypes };
  const hasSavedFilter = hasHistoryFilters(saved.filters);
  const menuFilters = hasSavedFilter ? [
    ...filters,
    { key: savedHistoryFilterKey, label: () => translate('SavedHistoryFilter') }
  ] : filters;
  const query = useQuery(historyPageQueryOptions({ page, pageSize, sortKey, sortDirection, ...activeFilters }));

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

  const updateSaved = (preference) => {
    setSaved(preference);
    saveHistoryFilterPreference(getPreferenceStorage(), apiRoot, preference);
  };

  const onFilterSelect = (key) => {
    if (key === savedHistoryFilterKey) {
      updateSaved({ ...saved, active: true });
      setHistoryPage(1);
    } else {
      if (saved.active) {
        updateSaved({ ...saved, active: false });
      }
      setHistoryFilter(key);
    }
  };

  const onApplyFilters = (customFilters) => {
    const active = hasHistoryFilters(customFilters);
    updateSaved({ active, filters: customFilters });

    if (active) {
      setHistoryPage(1);
    } else {
      setHistoryFilter('all');
    }
  };

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
      selectedFilterKey={saved.active ? savedHistoryFilterKey : selectedFilterKey}
      filters={menuFilters}
      historyFilter={hasSavedFilter ? saved.filters : activeFilters}
      hasActiveFilters={hasHistoryFilters(activeFilters)}
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
      onFilterSelect={onFilterSelect}
      onApplyFilters={onApplyFilters}
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
