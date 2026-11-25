import React, { Component } from 'react';
import { connect } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditDelayProfileModal, { type EditDelayProfileModalProps } from './EditDelayProfileModal';

const mapDispatchToProps = {
  clearPendingChanges
};

interface EditDelayProfileModalConnectorProps extends EditDelayProfileModalProps {
  clearPendingChanges: typeof clearPendingChanges;
}

class EditDelayProfileModalConnector extends Component<EditDelayProfileModalConnectorProps> {
  onModalClose = () => {
    this.props.clearPendingChanges({ section: 'settings.delayProfiles' });
    this.props.onModalClose();
  };

  render() {
    const { id, isOpen, onDeleteDelayProfilePress } = this.props;
    return (
      <EditDelayProfileModal
        id={id}
        isOpen={isOpen}
        onDeleteDelayProfilePress={onDeleteDelayProfilePress}
        onModalClose={this.onModalClose}
      />
    );
  }
}

export default connect(undefined, mapDispatchToProps)(EditDelayProfileModalConnector);
