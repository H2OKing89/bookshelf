import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useLayoutEffect, useState } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import * as commandActions from 'Store/Actions/commandActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import Queue from './Queue';
import queuePageQuery, {
  holdQueueRefresh,
  queuePageFamilyKey,
  queueTargetsAreCurrent,
  scheduleQueueRefresh,
  submitQueueAction
} from './queuePageQuery';
import { setQueuePage, setQueueSort, setQueueTableOptions, useQueuePreferences } from './queuePreferences';

const emptyRecords = [];

function createMapStateToProps() {
  return createSelector(
    createCommandExecutingSelector(commandNames.REFRESH_MONITORED_DOWNLOADS),
    (isRefreshMonitoredDownloadsExecuting) => ({ isRefreshMonitoredDownloadsExecuting })
  );
}

const mapDispatchToProps = {
  executeCommand: commandActions.executeCommand
};

function QueueConnector({
  useCurrentPage,
  isRefreshMonitoredDownloadsExecuting,
  executeCommand,
  ...otherProps
}) {
  const { page: storedPage, pageSize, sortKey, sortDirection, includeUnknownAuthorItems, columns, filters } = useQueuePreferences();
  const [pageInitialized, setPageInitialized] = useState(useCurrentPage);
  const [actionRejected, setActionRejected] = useState(false);
  const page = pageInitialized ? storedPage : 1;
  const options = queuePageQuery({ page, pageSize, sortKey, sortDirection, includeUnknownAuthorItems, filters });
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
  }, [needsClamp, pageInitialized, totalPages]);

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

  return (
    <Queue
      {...otherProps}
      columns={columns}
      items={items}
      page={page}
      pageKey={pageKey}
      pageSize={pageSize}
      sortKey={sortKey}
      sortDirection={sortDirection}
      totalPages={Math.max(page, totalPages)}
      totalRecords={totalRecords}
      hasQueueFilters={!!(filters.status || filters.protocol)}
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
      onSortPress={setQueueSort}
      onTableOptionChange={setQueueTableOptions}
    />
  );
}

QueueConnector.propTypes = {
  useCurrentPage: PropTypes.bool.isRequired,
  isRefreshMonitoredDownloadsExecuting: PropTypes.bool.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default withCurrentPage(connect(createMapStateToProps, mapDispatchToProps)(QueueConnector));
