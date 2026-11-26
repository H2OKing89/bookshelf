import Modal from 'Components/Modal/Modal';
import EditImportListModalContentConnector, { type ImportListEditOwnProps } from './EditImportListModalContentConnector';

export type ImportListModalProps = ImportListEditOwnProps & { isOpen: boolean };

function EditImportListModal({ isOpen, onModalClose, ...otherProps }: ImportListModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditImportListModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditImportListModal;
