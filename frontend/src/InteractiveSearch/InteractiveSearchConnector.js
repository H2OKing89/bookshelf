import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useMemo, useState } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as releaseActions from 'Store/Actions/releaseActions';
import createCustomFiltersSelector from 'Store/Selectors/createCustomFiltersSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import filterCollection from 'Utilities/Array/filterCollection';
import sortCollection from 'Utilities/Array/sortCollection';
import InteractiveSearch from './InteractiveSearch';
import InteractiveSearchFilterMenu from './InteractiveSearchFilterMenu';
import releaseSearchQuery, {
  currentRelease,
  filterReleaseTitles,
  grabSearchRelease,
  parseReleaseSearchScope,
  releaseGrabError,
  sameRelease,
  setReleaseStatus
} from './releaseSearchQuery';

const emptyResults = [];

function createMapStateToProps(_state, { type }) {
  return createSelector(
    (state) => state.releases,
    createCustomFiltersSelector('releases', `releases.${type}`),
    createUISettingsSelector(),
    (releases, customFilters, uiSettings) => ({
      preferences: { ...releases, ...releases[type], customFilters },
      longDateFormat: uiSettings.longDateFormat,
      timeFormat: uiSettings.timeFormat
    })
  );
}

function createMapDispatchToProps(dispatch, { type }) {
  const setFilter = type === 'book' ? releaseActions.setBookReleasesFilter : releaseActions.setAuthorReleasesFilter;

  return {
    onSortPress(sortKey, sortDirection) {
      dispatch(releaseActions.setReleasesSort({ sortKey, sortDirection }));
    },
    onFilterSelect(selectedFilterKey) {
      dispatch(setFilter({ selectedFilterKey }));
    }
  };
}

function InteractiveSearchConnector({
  searchPayload,
  type,
  preferences,
  onSortPress,
  onFilterSelect,
  longDateFormat,
  timeFormat
}) {
  const options = releaseSearchQuery(parseReleaseSearchScope(searchPayload));
  const scopeKey = JSON.stringify(options.queryKey);
  const query = useQuery(options);
  const queryClient = useQueryClient();
  const results = query.data?.items ?? emptyResults;
  const [text, setText] = useState({ scopeKey, value: '' });
  const textFilter = text.scopeKey === scopeKey ? text.value : '';
  const needsRefresh = !!query.data && (query.isStale || !!query.data.refreshRequired);
  const canGrab = query.isSuccess && !query.isFetching && !needsRefresh;
  const mutationKey = ['grabSearchRelease', options.queryKey[1]];
  const { mutate } = useMutation({
    mutationKey,
    mutationFn: grabSearchRelease,
    networkMode: 'always',
    retry: false,
    onMutate: (action) => setReleaseStatus(queryClient, action, { isGrabbing: true }),
    onSuccess: (_result, action) => setReleaseStatus(queryClient, action, { isGrabbing: false, isGrabbed: true, grabError: null }),
    onError: (error, action) => {
      const { message, refreshRequired } = releaseGrabError(error);
      setReleaseStatus(queryClient, action, { isGrabbing: false, isGrabbed: false, grabError: message }, refreshRequired);
    }
  });

  const { filters, customFilters, selectedFilterKey, sortKey, sortDirection } = preferences;
  const validFilter = filters.some(filter => filter.key === selectedFilterKey) || customFilters.some(filter => filter.id === selectedFilterKey);
  const activeFilter = validFilter ? selectedFilterKey : 'all';
  const visibleResults = useMemo(() => {
    const state = { ...preferences, selectedFilterKey: activeFilter };
    return sortCollection(filterReleaseTitles(filterCollection(results, state), textFilter), state);
  }, [results, preferences, activeFilter, textFilter]);

  const onGrabPress = (payload) => {
    const release = currentRelease(queryClient, options.queryKey, payload);
    if (!release || queryClient.isMutating({
      mutationKey, exact: true,
      predicate: mutation => mutation.state.variables?.identity && sameRelease(mutation.state.variables.identity, release)
    })) {
      return;
    }

    const { guid, indexerId, searchResultId } = release;
    mutate({ key: options.queryKey, identity: { guid, indexerId, searchResultId } });
  };

  return (
    <InteractiveSearch
      searchPayload={options.queryKey[2]}
      searchScopeKey={JSON.stringify([scopeKey, query.data?.fetchedAt])}
      type={type}
      items={visibleResults}
      totalReleasesCount={results.length}
      isPopulated={query.data != null}
      isFetching={query.isFetching || query.isPending}
      error={query.error}
      needsRefresh={needsRefresh}
      canGrab={canGrab}
      sortKey={sortKey}
      sortDirection={sortDirection}
      longDateFormat={longDateFormat}
      timeFormat={timeFormat}
      textFilter={textFilter}
      onTextFilterChange={(value) => setText({ scopeKey, value })}
      onRefreshPress={() => query.refetch()}
      onSortPress={onSortPress}
      onGrabPress={onGrabPress}
      filterMenu={
        <InteractiveSearchFilterMenu
          key={scopeKey}
          type={type}
          sectionItems={results}
          filters={filters}
          customFilters={customFilters}
          selectedFilterKey={activeFilter}
          onFilterSelect={onFilterSelect}
        />
      }
    />
  );
}

InteractiveSearchConnector.propTypes = {
  searchPayload: PropTypes.object.isRequired,
  type: PropTypes.string.isRequired,
  preferences: PropTypes.object.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onFilterSelect: PropTypes.func.isRequired,
  longDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired
};

export default connect(createMapStateToProps, createMapDispatchToProps)(InteractiveSearchConnector);
