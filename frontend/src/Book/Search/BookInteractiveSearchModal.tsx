import type { BookId } from 'Book/BookIdentity';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import BookInteractiveSearchModalContent from './BookInteractiveSearchModalContent';

interface Props {
  bookId: BookId | null;
  bookTitle: string;
  authorName: string;
  onModalClose: () => void;
  isOpen: boolean;
}

function BookInteractiveSearchModal(props: Props) {
  const {
    isOpen,
    bookId,
    bookTitle,
    authorName,
    onModalClose
  } = props;

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      closeOnBackgroundClick={false}
      onModalClose={onModalClose}
    >
      <BookInteractiveSearchModalContent
        bookId={bookId}
        bookTitle={bookTitle}
        authorName={authorName}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default BookInteractiveSearchModal;
