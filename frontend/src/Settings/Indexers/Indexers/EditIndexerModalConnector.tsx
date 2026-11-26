import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { cancelSaveIndexer, cancelTestIndexer } from 'Store/Actions/settingsActions';
import type { LegacyDispatch } from 'Store/StateTypes';
import EditIndexerModal, { type IndexerModalProps } from './EditIndexerModal';

function createMapDispatchToProps(dispatch: LegacyDispatch) {
  const section = 'settings.indexers';

  return {
    dispatchClearPendingChanges() {
      dispatch(clearPendingChanges({ section }));
    },

    dispatchCancelTestIndexer() {
      dispatch(cancelTestIndexer({ section }));
    },

    dispatchCancelSaveIndexer() {
      dispatch(cancelSaveIndexer({ section }));
    }
  };
}

const connector = connect(null, createMapDispatchToProps);
type Props = IndexerModalProps & ConnectedProps<typeof connector>;

class EditIndexerModalConnector extends Component<Props> {

  //
  // Listeners

  onModalClose = () => {
    this.props.dispatchClearPendingChanges();
    this.props.dispatchCancelTestIndexer();
    this.props.dispatchCancelSaveIndexer();
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    return (
      <EditIndexerModal
        id={this.props.id}
        isOpen={this.props.isOpen}
        onDeleteIndexerPress={this.props.onDeleteIndexerPress}
        onModalClose={this.onModalClose}
      />
    );
  }
}

export default connector(EditIndexerModalConnector);
