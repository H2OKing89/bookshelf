import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import AuthorHistoryContentConnector, { type AuthorHistoryScope } from './AuthorHistoryContentConnector';

interface Props extends AuthorHistoryScope {
  isOpen: boolean;
  onModalClose: () => void;
}

function AuthorHistoryModal(props: Props) {
  const {
    isOpen,
    onModalClose,
    ...otherProps
  } = props;

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_LARGE}
      onModalClose={onModalClose}
    >
      <AuthorHistoryContentConnector
        mode="modal"
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default AuthorHistoryModal;
