import { isEqual } from 'lodash';
import PropTypes from 'prop-types';
import { useState } from 'react';
import { connect } from 'react-redux';
import PendingChangesModal from 'Settings/PendingChangesModal';
import useUnsavedNavigation from 'Settings/useUnsavedNavigation';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { cancelSaveDownloadClient, cancelTestDownloadClient } from 'Store/Actions/settingsActions';
import EditDownloadClientModal from './EditDownloadClientModal';

function mapStateToProps(state, { id, isOpen }) {
  if (!isOpen) {
    return { isDirty: false };
  }

  const { items, selectedSchema, pendingChanges } = state.settings.downloadClients;
  const saved = id ? items.find((item) => item.id === id) : { name: '', ...selectedSchema };
  const isDirty = Object.entries(pendingChanges).some(([key, value]) => {
    if (key !== 'fields') {
      return !isEqual(saved?.[key], value);
    }

    return Object.entries(value).some(([name, fieldValue]) => {
      const field = saved?.fields?.find((item) => item.name === name);

      return !isEqual(field?.value, fieldValue);
    });
  });

  return { isDirty };
}

function mapDispatchToProps(dispatch) {
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

function EditDownloadClientModalConnector({
  isOpen,
  isDirty,
  onModalClose,
  dispatchClearPendingChanges,
  dispatchCancelTestDownloadClient,
  dispatchCancelSaveDownloadClient,
  ...otherProps
}) {
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

EditDownloadClientModalConnector.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  isDirty: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired,
  dispatchClearPendingChanges: PropTypes.func.isRequired,
  dispatchCancelTestDownloadClient: PropTypes.func.isRequired,
  dispatchCancelSaveDownloadClient: PropTypes.func.isRequired
};

export default connect(mapStateToProps, mapDispatchToProps)(EditDownloadClientModalConnector);
