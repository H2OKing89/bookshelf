import React from 'react';
import type { AuthorId } from 'Book/BookIdentity';
import Modal from 'Components/Modal/Modal';
import EditAuthorModalContentConnector from './EditAuthorModalContentConnector';

export interface EditAuthorModalProps {
  isOpen: boolean;
  authorId: AuthorId;
  onModalClose: () => void;
  onDeleteAuthorPress?: () => void;
}

function EditAuthorModal({ isOpen, onModalClose, ...otherProps }: EditAuthorModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditAuthorModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}


export default EditAuthorModal;
