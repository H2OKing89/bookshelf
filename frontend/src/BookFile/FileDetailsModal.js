import PropTypes from 'prop-types';
import React, { useState } from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import FileDetailsConnector from './FileDetailsConnector';
import RecordingReviewModal from './RecordingReviewModal';

function FileDetailsModal(props) {
  const {
    isOpen,
    onModalClose,
    id
  } = props;
  const [isReviewOpen, setIsReviewOpen] = useState(false);

  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <ModalContent
        onModalClose={onModalClose}
      >
        <ModalHeader>
          Details
        </ModalHeader>

        <ModalBody>
          <FileDetailsConnector
            id={id}
          />
        </ModalBody>

        <ModalFooter>
          <Button onPress={() => setIsReviewOpen(true)}>
            {translate('RecordingReviewAction')}
          </Button>
          <Button onPress={onModalClose}>
            Close
          </Button>
        </ModalFooter>
        {
          isReviewOpen &&
            <RecordingReviewModal
              isOpen={true}
              fileId={id}
              onModalClose={() => setIsReviewOpen(false)}
            />
        }
      </ModalContent>
    </Modal>
  );
}

FileDetailsModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired,
  id: PropTypes.number.isRequired
};

export default FileDetailsModal;
