import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditSpecificationModalContentConnector from './EditSpecificationModalContentConnector';

export interface EditSpecificationModalProps {
  id?: number;
  isOpen: boolean;
  onModalClose: () => void;
  onDeleteSpecificationPress?: () => void;
}

function EditSpecificationModal({ isOpen, onModalClose, ...props }: EditSpecificationModalProps) {
  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={onModalClose}>
      <EditSpecificationModalContentConnector {...props} onModalClose={onModalClose} />
    </Modal>
  );
}

export default EditSpecificationModal;
