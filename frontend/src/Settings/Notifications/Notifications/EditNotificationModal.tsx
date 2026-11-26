import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EditNotificationModalContentConnector, { type NotificationEditOwnProps } from './EditNotificationModalContentConnector';

export type NotificationModalProps = NotificationEditOwnProps & { isOpen: boolean };

function EditNotificationModal({ isOpen, onModalClose, ...otherProps }: NotificationModalProps) {
  return (
    <Modal
      size={sizes.MEDIUM}
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <EditNotificationModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

export default EditNotificationModal;
