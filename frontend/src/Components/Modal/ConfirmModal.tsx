import React from 'react';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import type { Size } from 'Helpers/Props/sizes';

export interface ConfirmModalProps {
  className?: string;
  isOpen: boolean;
  kind?: Kind;
  size?: Size;
  title: string;
  message: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  hideCancelButton?: boolean;
  isSpinning?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

function ConfirmModal(props: ConfirmModalProps) {
  const {
    isOpen,
    kind = kinds.PRIMARY,
    size = sizes.MEDIUM,
    title,
    message,
    confirmLabel = 'OK',
    cancelLabel = 'Cancel',
    hideCancelButton,
    isSpinning = false,
    onConfirm,
    onCancel
  } = props;
  const isDestructive = kind === kinds.DANGER;

  return (
    <Modal
      isOpen={isOpen}
      size={size}
      onModalClose={onCancel}
    >
      <ModalContent onModalClose={onCancel}>
        <ModalHeader>{title}</ModalHeader>

        <ModalBody>
          {message}
        </ModalBody>

        <ModalFooter>
          {
            !hideCancelButton &&
              <Button
                kind={kinds.DEFAULT}
                autoFocus={isDestructive}
                onPress={onCancel}
              >
                {cancelLabel}
              </Button>
          }

          <SpinnerButton
            autoFocus={!isDestructive}
            kind={kind}
            isSpinning={isSpinning}
            onPress={onConfirm}
          >
            {confirmLabel}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default ConfirmModal;
