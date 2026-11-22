import PropTypes from 'prop-types';
import React, { useLayoutEffect } from 'react';
import { connect } from 'react-redux';
import { cancelFetchReleases, clearReleases } from 'Store/Actions/releaseActions';
import BookInteractiveSearchModal from './BookInteractiveSearchModal';

function createMapDispatchToProps(dispatch) {
  return {
    onDismiss() {
      dispatch(cancelFetchReleases());
      dispatch(clearReleases());
    }
  };
}

function BookInteractiveSearchModalConnector({ isOpen, onDismiss, ...props }) {
  useLayoutEffect(() => (isOpen ? onDismiss : undefined), [isOpen, onDismiss]);

  return <BookInteractiveSearchModal {...props} isOpen={isOpen} />;
}

BookInteractiveSearchModalConnector.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onDismiss: PropTypes.func.isRequired
};

export default connect(null, createMapDispatchToProps)(BookInteractiveSearchModalConnector);
