import PropTypes from 'prop-types';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';

function PendingChangesModal({ isOpen, isClosingEditor = false, onConfirm, onCancel }) {
  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onCancel}
    >
      <ModalContent onModalClose={onCancel}>
        <ModalHeader>Unsaved Changes</ModalHeader>

        <ModalBody>
          {isClosingEditor ? 'Discard the changes in this editor?' : 'You have unsaved changes. Leave this page?'}
        </ModalBody>

        <ModalFooter>
          <Button
            autoFocus={true}
            kind={kinds.DEFAULT}
            onPress={onCancel}
          >
            {isClosingEditor ? 'Keep editing' : 'Stay and review changes'}
          </Button>

          <Button
            kind={kinds.DANGER}
            onPress={onConfirm}
          >
            {isClosingEditor ? 'Discard changes and close' : 'Discard changes and leave'}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

PendingChangesModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  isClosingEditor: PropTypes.bool,
  onConfirm: PropTypes.func.isRequired,
  onCancel: PropTypes.func.isRequired
};

export default PendingChangesModal;
