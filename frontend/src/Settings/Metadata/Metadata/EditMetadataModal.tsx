import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditMetadataModalContentConnector, { type MetadataEditOwnProps } from './EditMetadataModalContentConnector';

export type MetadataModalProps = MetadataEditOwnProps & { isOpen: boolean };

function EditMetadataModal({ isOpen, onModalClose, ...otherProps }: MetadataModalProps) {
  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditMetadataModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditMetadataModal;
