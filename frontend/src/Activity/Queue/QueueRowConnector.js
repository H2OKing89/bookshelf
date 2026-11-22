import _ from 'lodash';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import QueueRow from './QueueRow';

function createMapStateToProps() {
  return createSelector(
    createAuthorSelector(),
    createBookSelector(),
    createUISettingsSelector(),
    (author, book, uiSettings) => ({
      ..._.pick(uiSettings, ['showRelativeDates', 'shortDateFormat', 'timeFormat']),
      author,
      book
    })
  );
}

export default connect(createMapStateToProps)(QueueRow);
