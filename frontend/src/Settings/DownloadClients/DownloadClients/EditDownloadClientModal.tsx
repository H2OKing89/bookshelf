import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditDownloadClientModalContentConnector, { type DownloadClientEditOwnProps } from './EditDownloadClientModalContentConnector';

export type DownloadClientModalProps = DownloadClientEditOwnProps & { isOpen: boolean };

function EditDownloadClientModal({ isOpen, onModalClose, ...otherProps }: DownloadClientModalProps) {
  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditDownloadClientModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditDownloadClientModal;
