import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useEffect, useLayoutEffect, useState } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import * as commandActions from 'Store/Actions/commandActions';
import * as queueActions from 'Store/Actions/queueActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import Queue from './Queue';
import queuePageQuery, {
  holdQueueRefresh,
  queuePageFamilyKey,
  queueTargetsAreCurrent,
  scheduleQueueRefresh,
  submitQueueAction
} from './queuePageQuery';

const emptyRecords = [];

function createMapStateToProps() {
  return createSelector(
    (state) => state.authors,
    (state) => state.books,
    (state) => state.queue.options,
    (state) => state.queue.paged,
    createCommandExecutingSelector(commandNames.REFRESH_MONITORED_DOWNLOADS),
    (authors, books, options, queue, isRefreshMonitoredDownloadsExecuting) => ({
      isAuthorFetching: authors.isFetching,
      isAuthorPopulated: authors.isPopulated,
      isBooksFetching: books.isFetching,
      isBooksPopulated: books.isPopulated,
      booksError: books.error,
      isRefreshMonitoredDownloadsExecuting,
      ...options,
      ...queue
    })
  );
}

const mapDispatchToProps = {
  fetchQueueStatus: queueActions.fetchQueueStatus,
  setQueuePage: queueActions.setQueuePage,
  setQueueSort: queueActions.setQueueSort,
  setQueueTableOption: queueActions.setQueueTableOption,
  executeCommand: commandActions.executeCommand
};

function QueueConnector({
  useCurrentPage,
  page: storedPage,
  pageSize,
  sortKey,
  sortDirection,
  includeUnknownAuthorItems,
  isRefreshMonitoredDownloadsExecuting,
  fetchQueueStatus,
  setQueuePage,
  setQueueSort,
  setQueueTableOption,
  executeCommand,
  ...otherProps
}) {
  const [pageInitialized, setPageInitialized] = useState(useCurrentPage);
  const [actionRejected, setActionRejected] = useState(false);
  const page = pageInitialized ? storedPage : 1;
  const options = queuePageQuery({ page, pageSize, sortKey, sortDirection, includeUnknownAuthorItems });
  const pageKey = JSON.stringify(options.queryKey);
  const query = useQuery(options);
  const queryClient = useQueryClient();
  const mutationKey = [...queuePageFamilyKey(), 'action'];
  const mutation = useMutation({
    mutationKey,
    mutationFn: submitQueueAction,
    networkMode: 'always',
    retry: false,
    onMutate: holdQueueRefresh,
    onSettled: (_value, _error, _action, release) => {
      scheduleQueueRefresh();
      release?.();
    }
  });

  const items = query.data?.records ?? emptyRecords;
  const totalRecords = query.data?.totalRecords ?? 0;
  const totalPages = Math.max(Math.ceil(totalRecords / (query.data?.pageSize ?? pageSize)), 1);
  const needsClamp = query.isSuccess && !query.isPlaceholderData && page > totalPages;
  const canUseRowActions = query.isSuccess && !query.isPlaceholderData && !needsClamp && !mutation.isPending;

  useLayoutEffect(() => {
    if (!pageInitialized) {
      setQueuePage(1);
      setPageInitialized(true);
    } else if (needsClamp) {
      setQueuePage(totalPages);
    }
  }, [needsClamp, pageInitialized, setQueuePage, totalPages]);

  useEffect(() => {
    fetchQueueStatus();
  }, [fetchQueueStatus]);

  useLayoutEffect(() => {
    if (isRefreshMonitoredDownloadsExecuting) {
      return holdQueueRefresh();
    }

    return undefined;
  }, [isRefreshMonitoredDownloadsExecuting]);

  const onQueueAction = (action) => {
    if (!canUseRowActions || action.pageKey !== pageKey ||
        queryClient.isMutating({ mutationKey, exact: true }) ||
        !queueTargetsAreCurrent(items, action.targets)) {
      setActionRejected(true);
      scheduleQueueRefresh();
      return;
    }

    setActionRejected(false);
    mutation.mutate(action);
  };

  const onRefreshPress = () => {
    if (!isRefreshMonitoredDownloadsExecuting && !queryClient.isMutating({ mutationKey, exact: true })) {
      setActionRejected(false);
      mutation.reset();
      executeCommand({
        name: commandNames.REFRESH_MONITORED_DOWNLOADS,
        commandFinished: scheduleQueueRefresh
      });
    }
  };

  const goToPage = (value) => setQueuePage(Math.min(Math.max(value, 1), totalPages));
  const onFirstPagePress = () => goToPage(1);
  const onPreviousPagePress = () => goToPage(page - 1);
  const onNextPagePress = () => goToPage(page + 1);
  const onLastPagePress = () => goToPage(totalPages);
  const onSortPress = (nextKey, nextDirection) => setQueueSort({ sortKey: nextKey, sortDirection: nextDirection });

  return (
    <Queue
      {...otherProps}
      items={items}
      page={page}
      pageKey={pageKey}
      pageSize={pageSize}
      sortKey={sortKey}
      sortDirection={sortDirection}
      totalPages={Math.max(page, totalPages)}
      totalRecords={totalRecords}
      isFetching={query.isPending || query.isFetching || needsClamp}
      isPopulated={!!query.data}
      error={query.error}
      canUseRowActions={canUseRowActions}
      actionError={actionRejected || mutation.isError}
      pendingAction={mutation.isPending ? mutation.variables : undefined}
      isRefreshMonitoredDownloadsExecuting={isRefreshMonitoredDownloadsExecuting}
      onRefreshPress={onRefreshPress}
      onQueueAction={onQueueAction}
      onFirstPagePress={onFirstPagePress}
      onPreviousPagePress={onPreviousPagePress}
      onNextPagePress={onNextPagePress}
      onLastPagePress={onLastPagePress}
      onPageSelect={goToPage}
      onSortPress={onSortPress}
      onTableOptionChange={setQueueTableOption}
    />
  );
}

QueueConnector.propTypes = {
  useCurrentPage: PropTypes.bool.isRequired,
  page: PropTypes.number.isRequired,
  pageSize: PropTypes.number.isRequired,
  sortKey: PropTypes.string.isRequired,
  sortDirection: PropTypes.string.isRequired,
  includeUnknownAuthorItems: PropTypes.bool.isRequired,
  isRefreshMonitoredDownloadsExecuting: PropTypes.bool.isRequired,
  fetchQueueStatus: PropTypes.func.isRequired,
  setQueuePage: PropTypes.func.isRequired,
  setQueueSort: PropTypes.func.isRequired,
  setQueueTableOption: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default withCurrentPage(connect(createMapStateToProps, mapDispatchToProps)(QueueConnector));
