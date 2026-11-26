import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditDelayProfileModalContentConnector from './EditDelayProfileModalContentConnector';

export interface EditDelayProfileModalProps {
  id?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteDelayProfilePress?: () => void;
}

function EditDelayProfileModal({ isOpen, onModalClose, ...otherProps }: EditDelayProfileModalProps) {
  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditDelayProfileModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}


export default EditDelayProfileModal;
