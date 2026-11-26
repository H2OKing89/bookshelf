import { useState } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import PendingChangesModal from 'Settings/PendingChangesModal';
import useUnsavedNavigation from 'Settings/useUnsavedNavigation';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { cancelSaveDownloadClient, cancelTestDownloadClient } from 'Store/Actions/settingsActions';
import type { LegacyDispatch, LegacyRootState } from 'Store/StateTypes';
import { providerHasChanges } from '../../providerSettingsState';
import EditDownloadClientModal, { type DownloadClientModalProps } from './EditDownloadClientModal';

type OwnProps = Omit<DownloadClientModalProps, 'onSaveSuccess'>;

function mapStateToProps(state: LegacyRootState, { id, isOpen }: OwnProps) {
  return { isDirty: isOpen && providerHasChanges(state.settings.downloadClients, id) };
}

function mapDispatchToProps(dispatch: LegacyDispatch) {
  const section = 'settings.downloadClients';

  return {
    dispatchClearPendingChanges() {
      dispatch(clearPendingChanges({ section }));
    },

    dispatchCancelTestDownloadClient() {
      dispatch(cancelTestDownloadClient({ section }));
    },

    dispatchCancelSaveDownloadClient() {
      dispatch(cancelSaveDownloadClient({ section }));
    }
  };
}

const connector = connect(mapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

function EditDownloadClientModalConnector({
  isOpen,
  isDirty,
  onModalClose,
  dispatchClearPendingChanges,
  dispatchCancelTestDownloadClient,
  dispatchCancelSaveDownloadClient,
  ...otherProps
}: Props) {
  const [isConfirmingClose, setIsConfirmingClose] = useState(false);

  const onCloseEditor = () => {
    setIsConfirmingClose(false);
    dispatchClearPendingChanges();
    dispatchCancelTestDownloadClient();
    dispatchCancelSaveDownloadClient();
    onModalClose();
  };

  useUnsavedNavigation(isDirty, onCloseEditor);

  const onRequestClose = () => {
    if (isDirty) {
      setIsConfirmingClose(true);
    } else {
      onCloseEditor();
    }
  };

  return (
    <>
      <EditDownloadClientModal
        {...otherProps}
        isOpen={isOpen}
        onModalClose={onRequestClose}
        onSaveSuccess={onCloseEditor}
      />
      <PendingChangesModal
        isOpen={isOpen && isConfirmingClose}
        isClosingEditor={true}
        onConfirm={onCloseEditor}
        onCancel={() => setIsConfirmingClose(false)}
      />
    </>
  );
}

export default connector(EditDownloadClientModalConnector);
