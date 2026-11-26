import Modal from 'Components/Modal/Modal';
import AddSpecificationModalContentConnector from './AddSpecificationModalContentConnector';

interface Props {
  isOpen: boolean;
  onModalClose: (result?: { specificationSelected?: boolean }) => void;
}

function AddSpecificationModal({ isOpen, onModalClose }: Props) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddSpecificationModalContentConnector onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddSpecificationModal;
