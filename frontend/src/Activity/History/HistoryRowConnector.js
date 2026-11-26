import { useIsMutating, useMutation, useQueryClient } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { markHistoryAsFailed, scheduleHistoryRefresh } from './historyPageQuery';
import HistoryRow from './HistoryRow';

function createMapStateToProps() {
  return createSelector(
    createAuthorSelector(),
    createBookSelector(),
    createUISettingsSelector(),
    (author, book, uiSettings) => {
      return {
        author,
        book,
        shortDateFormat: uiSettings.shortDateFormat,
        timeFormat: uiSettings.timeFormat
      };
    }
  );
}

function HistoryRowConnector({ id, canUseRowActions, ...otherProps }) {
  const queryClient = useQueryClient();
  const mutationKey = ['markHistoryAsFailed', window.Readarr.apiRoot, id];
  const isMarkingAsFailed = useIsMutating({ mutationKey, exact: true }) > 0;
  const mutation = useMutation({
    mutationKey,
    mutationFn: markHistoryAsFailed,
    networkMode: 'always',
    onSuccess: () => { scheduleHistoryRefresh(); }
  });

  const onMarkAsFailedPress = () => {
    if (canUseRowActions && !queryClient.isMutating({ mutationKey, exact: true })) {
      mutation.mutate(id);
    }
  };

  return (
    <HistoryRow
      id={id}
      {...otherProps}
      canUseRowActions={canUseRowActions}
      isMarkingAsFailed={isMarkingAsFailed}
      markAsFailedError={mutation.error}
      onMarkAsFailedPress={onMarkAsFailedPress}
    />
  );
}

HistoryRowConnector.propTypes = {
  id: PropTypes.number.isRequired,
  canUseRowActions: PropTypes.bool.isRequired
};

export default connect(createMapStateToProps)(HistoryRowConnector);
