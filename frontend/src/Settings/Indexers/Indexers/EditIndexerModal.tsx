import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditIndexerModalContentConnector, { type IndexerEditOwnProps } from './EditIndexerModalContentConnector';

export type IndexerModalProps = IndexerEditOwnProps & { isOpen: boolean };

function EditIndexerModal({ isOpen, onModalClose, ...otherProps }: IndexerModalProps) {
  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditIndexerModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditIndexerModal;
