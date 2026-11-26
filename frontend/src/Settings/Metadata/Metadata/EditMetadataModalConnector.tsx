import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import type { LegacyDispatch } from 'Store/StateTypes';
import EditMetadataModal, { type MetadataModalProps } from './EditMetadataModal';

function createMapDispatchToProps(dispatch: LegacyDispatch) {
  const section = 'settings.metadata';

  return {
    dispatchClearPendingChanges() {
      dispatch(clearPendingChanges({ section }));
    }
  };
}

const connector = connect(null, createMapDispatchToProps);
type Props = MetadataModalProps & ConnectedProps<typeof connector>;

class EditMetadataModalConnector extends Component<Props> {

  //
  // Listeners

  onModalClose = () => {
    this.props.dispatchClearPendingChanges();
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    return (
      <EditMetadataModal
        {...this.props}
        onModalClose={this.onModalClose}
      />
    );
  }
}

export default connector(EditMetadataModalConnector);
