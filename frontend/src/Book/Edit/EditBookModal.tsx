import React from 'react';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import Modal from 'Components/Modal/Modal';
import EditBookModalContentConnector from './EditBookModalContentConnector';

export interface EditBookModalProps {
  isOpen: boolean;
  authorId: AuthorId;
  onModalClose: () => void;
  onDeleteAuthorPress?: () => void;
  bookId: BookId;
}

function EditBookModal({ isOpen, onModalClose, ...otherProps }: EditBookModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditBookModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}


export default EditBookModal;
