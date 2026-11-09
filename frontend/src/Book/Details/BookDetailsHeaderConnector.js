import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import createBookSelector from 'Store/Selectors/createBookSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import BookDetailsHeader from './BookDetailsHeader';

function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    (state) => state.editions,
    createUISettingsSelector(),
    createDimensionsSelector(),
    (book, editions, uiSettings, dimensions) => {
      const edition = editions.items.find((item) => item.bookId === book.id && item.monitored);

      return {
        ...book,
        overview: edition?.overview || book.overview,
        shortDateFormat: uiSettings.shortDateFormat,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  toggleBooksMonitored
};

class BookDetailsHeaderConnector extends Component {

  //
  // Listeners

  onMonitorTogglePress = (monitored) => {
    this.props.toggleBooksMonitored({
      bookIds: [this.props.bookId],
      monitored
    });
  };

  //
  // Render

  render() {
    return (
      <BookDetailsHeader
        {...this.props}
        onMonitorTogglePress={this.onMonitorTogglePress}
      />
    );
  }
}

BookDetailsHeaderConnector.propTypes = {
  bookId: PropTypes.number,
  toggleBooksMonitored: PropTypes.func.isRequired,
  author: PropTypes.object
};

export default connect(createMapStateToProps, mapDispatchToProps)(BookDetailsHeaderConnector);
