import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditCustomFormatModalContentConnector from './EditCustomFormatModalContentConnector';

export interface EditFormatModalProps {
  id?: number;
  tagsFromId?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteCustomFormatPress?: () => void;
}

function EditCustomFormatModal({ isOpen, onModalClose, ...props }: EditFormatModalProps) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <EditCustomFormatModalContentConnector {...props} onModalClose={onModalClose} />
    </Modal>
  );
}

export default EditCustomFormatModal;
