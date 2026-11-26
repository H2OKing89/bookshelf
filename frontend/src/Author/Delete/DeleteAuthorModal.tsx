import type { AuthorId } from 'Book/BookIdentity';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import DeleteAuthorModalContentConnector from './DeleteAuthorModalContentConnector';

interface DeleteAuthorModalProps {
  authorId: AuthorId;
  isOpen: boolean;
  onModalClose: (deleted?: boolean) => void;
}

function DeleteAuthorModal(props: DeleteAuthorModalProps) {
  const {
    isOpen,
    onModalClose,
    ...otherProps
  } = props;

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.MEDIUM}
      onModalClose={onModalClose}
    >
      <DeleteAuthorModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}


export default DeleteAuthorModal;
