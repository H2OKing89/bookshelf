import _ from 'lodash';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import QueueRow from './QueueRow';

function createMapStateToProps() {
  return createSelector(
    createUISettingsSelector(),
    (uiSettings) => _.pick(uiSettings, ['showRelativeDates', 'shortDateFormat', 'timeFormat'])
  );
}

export default connect(createMapStateToProps)(QueueRow);
