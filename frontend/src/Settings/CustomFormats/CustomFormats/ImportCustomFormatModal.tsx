import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import ImportCustomFormatModalContentConnector from './ImportCustomFormatModalContentConnector';

interface Props {
  isOpen: boolean;
  onModalClose: () => void;
}

function ImportCustomFormatModal({ isOpen, onModalClose }: Props) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <ImportCustomFormatModalContentConnector onModalClose={onModalClose} />
    </Modal>
  );
}

export default ImportCustomFormatModal;
