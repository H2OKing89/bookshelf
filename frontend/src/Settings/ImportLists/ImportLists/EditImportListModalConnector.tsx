import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { cancelSaveImportList, cancelTestImportList } from 'Store/Actions/settingsActions';
import type { LegacyDispatch } from 'Store/StateTypes';
import EditImportListModal, { type ImportListModalProps } from './EditImportListModal';

function createMapDispatchToProps(dispatch: LegacyDispatch) {
  const section = 'settings.importLists';

  return {
    dispatchClearPendingChanges() {
      dispatch(clearPendingChanges({ section }));
    },

    dispatchCancelTestImportList() {
      dispatch(cancelTestImportList({ section }));
    },

    dispatchCancelSaveImportList() {
      dispatch(cancelSaveImportList({ section }));
    }
  };
}

const connector = connect(null, createMapDispatchToProps);
type Props = ImportListModalProps & ConnectedProps<typeof connector>;

class EditImportListModalConnector extends Component<Props> {

  //
  // Listeners

  onModalClose = () => {
    this.props.dispatchClearPendingChanges();
    this.props.dispatchCancelTestImportList();
    this.props.dispatchCancelSaveImportList();
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    return (
      <EditImportListModal
        id={this.props.id}
        isOpen={this.props.isOpen}
        onDeleteImportListPress={this.props.onDeleteImportListPress}
        onModalClose={this.onModalClose}
      />
    );
  }
}

export default connector(EditImportListModalConnector);
