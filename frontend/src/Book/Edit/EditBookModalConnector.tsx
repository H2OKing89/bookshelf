import React, { Component } from 'react';
import { connect } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditBookModal, { type EditBookModalProps } from './EditBookModal';

const mapDispatchToProps = {
  clearPendingChanges
};

interface EditBookModalConnectorProps extends EditBookModalProps {
  clearPendingChanges: typeof clearPendingChanges;
}

class EditBookModalConnector extends Component<EditBookModalConnectorProps> {

  //
  // Listeners

  onModalClose = () => {
    this.props.clearPendingChanges({ section: 'books' });
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    const { isOpen, authorId, onDeleteAuthorPress, bookId } = this.props;
    return (
      <EditBookModal
        isOpen={isOpen}
        authorId={authorId}
        onDeleteAuthorPress={onDeleteAuthorPress}
        bookId={bookId}
        onModalClose={this.onModalClose}
      />
    );
  }
}


export default connect(undefined, mapDispatchToProps)(EditBookModalConnector);
