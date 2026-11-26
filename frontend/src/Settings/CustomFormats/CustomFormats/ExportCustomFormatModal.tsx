import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import ExportCustomFormatModalContentConnector from './ExportCustomFormatModalContentConnector';

interface Props {
  id: number;
  isOpen: boolean;
  onModalClose: () => void;
}

function ExportCustomFormatModal({ id, isOpen, onModalClose }: Props) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <ExportCustomFormatModalContentConnector id={id} onModalClose={onModalClose} />
    </Modal>
  );
}

export default ExportCustomFormatModal;
