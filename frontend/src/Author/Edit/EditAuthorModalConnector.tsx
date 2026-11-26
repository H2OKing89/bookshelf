import { Component } from 'react';
import { connect } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditAuthorModal, { type EditAuthorModalProps } from './EditAuthorModal';

const mapDispatchToProps = {
  clearPendingChanges
};

interface EditAuthorModalConnectorProps extends EditAuthorModalProps {
  clearPendingChanges: typeof clearPendingChanges;
}

class EditAuthorModalConnector extends Component<EditAuthorModalConnectorProps> {

  //
  // Listeners

  onModalClose = () => {
    this.props.clearPendingChanges({ section: 'author' });
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    const { isOpen, authorId, onDeleteAuthorPress } = this.props;
    return (
      <EditAuthorModal
        isOpen={isOpen}
        authorId={authorId}
        onDeleteAuthorPress={onDeleteAuthorPress}
        onModalClose={this.onModalClose}
      />
    );
  }
}


export default connect(undefined, mapDispatchToProps)(EditAuthorModalConnector);
