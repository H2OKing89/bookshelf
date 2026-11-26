import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { cancelSaveNotification, cancelTestNotification } from 'Store/Actions/settingsActions';
import type { LegacyDispatch } from 'Store/StateTypes';
import EditNotificationModal, { type NotificationModalProps } from './EditNotificationModal';

function createMapDispatchToProps(dispatch: LegacyDispatch) {
  const section = 'settings.notifications';

  return {
    dispatchClearPendingChanges() {
      dispatch(clearPendingChanges({ section }));
    },

    dispatchCancelTestNotification() {
      dispatch(cancelTestNotification({ section }));
    },

    dispatchCancelSaveNotification() {
      dispatch(cancelSaveNotification({ section }));
    }
  };
}

const connector = connect(null, createMapDispatchToProps);
type Props = NotificationModalProps & ConnectedProps<typeof connector>;

class EditNotificationModalConnector extends Component<Props> {

  //
  // Listeners

  onModalClose = () => {
    this.props.dispatchClearPendingChanges();
    this.props.dispatchCancelTestNotification();
    this.props.dispatchCancelSaveNotification();
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    return (
      <EditNotificationModal
        id={this.props.id}
        isOpen={this.props.isOpen}
        onDeleteNotificationPress={this.props.onDeleteNotificationPress}
        onModalClose={this.onModalClose}
      />
    );
  }
}

export default connector(EditNotificationModalConnector);
