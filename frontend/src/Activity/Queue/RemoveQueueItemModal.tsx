import React, { useCallback, useMemo, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './RemoveQueueItemModal.css';

interface RemovePressProps {
  remove: boolean;
  changeCategory: boolean;
  blocklist: boolean;
  skipRedownload: boolean;
}

interface RemoveQueueItemModalProps {
  isOpen: boolean;
  sourceTitle?: string;
  canChangeCategory: boolean;
  canIgnore: boolean;
  canRemoveFromClient: boolean;
  canBlocklist: boolean;
  canRetry: boolean;
  hasPending: boolean;
  isPending: boolean;
  isRemoveDisabled: boolean;
  selectedCount?: number;
  onRemovePress(props: RemovePressProps): void;
  onModalClose: () => void;
}

type RemovalMethod = 'removeFromClient' | 'changeCategory' | 'ignore';
type BlocklistMethod =
  | 'doNotBlocklist'
  | 'blocklistAndSearch'
  | 'blocklistOnly';

function RemoveQueueItemModal(props: RemoveQueueItemModalProps) {
  const {
    isOpen,
    sourceTitle = '',
    canIgnore,
    canRemoveFromClient,
    canBlocklist,
    canRetry,
    hasPending,
    canChangeCategory,
    isPending,
    isRemoveDisabled,
    selectedCount,
    onRemovePress,
    onModalClose,
  } = props;

  const multipleSelected = selectedCount && selectedCount > 1;

  const [removalMethod, setRemovalMethod] =
    useState<RemovalMethod>('removeFromClient');
  const [blocklistMethod, setBlocklistMethod] =
    useState<BlocklistMethod>('doNotBlocklist');
  let effectiveRemovalMethod = removalMethod;
  const removalMethodAllowed = removalMethod === 'removeFromClient' && canRemoveFromClient ||
    removalMethod === 'changeCategory' && canChangeCategory ||
    removalMethod === 'ignore' && (canIgnore || canBlocklist);

  if (!removalMethodAllowed) {
    if (canRemoveFromClient) {
      effectiveRemovalMethod = 'removeFromClient';
    } else if (canChangeCategory) {
      effectiveRemovalMethod = 'changeCategory';
    } else {
      effectiveRemovalMethod = 'ignore';
    }
  }

  let effectiveBlocklistMethod = blocklistMethod;
  if (!canBlocklist) {
    effectiveBlocklistMethod = 'doNotBlocklist';
  } else if ((isPending || !canRetry) && blocklistMethod === 'blocklistAndSearch') {
    effectiveBlocklistMethod = 'blocklistOnly';
  }

  const unavailable = !isPending && effectiveRemovalMethod === 'ignore' &&
    !canIgnore && effectiveBlocklistMethod === 'doNotBlocklist';

  const { title, message } = useMemo(() => {
    if (!selectedCount) {
      return {
        title: translate('RemoveQueueItem', { sourceTitle }),
        message: translate('RemoveQueueItemConfirmation', { sourceTitle }),
      };
    }

    if (selectedCount === 1) {
      return {
        title: translate('RemoveSelectedItem'),
        message: translate('RemoveSelectedItemQueueMessageText'),
      };
    }

    return {
      title: translate('RemoveSelectedItems'),
      message: translate('RemoveSelectedItemsQueueMessageText', {
        selectedCount,
      }),
    };
  }, [sourceTitle, selectedCount]);

  const removalMethodOptions = useMemo(() => {
    return [
      {
        key: 'removeFromClient',
        isDisabled: !canRemoveFromClient,
        value: translate('RemoveFromDownloadClient'),
        hint: multipleSelected
          ? translate('RemoveMultipleFromDownloadClientHint')
          : translate('RemoveFromDownloadClientHint'),
      },
      {
        key: 'changeCategory',
        value: translate('ChangeCategory'),
        isDisabled: !canChangeCategory,
        hint: multipleSelected
          ? translate('ChangeCategoryMultipleHint')
          : translate('ChangeCategoryHint'),
      },
      {
        key: 'ignore',
        value: multipleSelected
          ? translate('IgnoreDownloads')
          : translate('IgnoreDownload'),
        isDisabled: !canIgnore && !canBlocklist,
        hint: multipleSelected
          ? translate('IgnoreDownloadsHint')
          : translate('IgnoreDownloadHint'),
      },
    ];
  }, [canChangeCategory, canIgnore, canRemoveFromClient, canBlocklist, multipleSelected]);

  const blocklistMethodOptions = useMemo(() => {
    return [
      {
        key: 'doNotBlocklist',
        value: translate('DoNotBlocklist'),
        hint: translate('DoNotBlocklistHint'),
      },
      {
        key: 'blocklistAndSearch',
        isDisabled: isPending || !canBlocklist || !canRetry,
        value: translate('BlocklistAndSearch'),
        hint: multipleSelected
          ? translate('BlocklistAndSearchMultipleHint')
          : translate('BlocklistAndSearchHint'),
      },
      {
        key: 'blocklistOnly',
        isDisabled: !canBlocklist,
        value: translate('BlocklistOnly'),
        hint: multipleSelected
          ? translate('BlocklistMultipleOnlyHint')
          : translate('BlocklistOnlyHint'),
      },
    ];
  }, [multipleSelected, canBlocklist, canRetry, isPending]);

  const handleRemovalMethodChange = useCallback(
    ({ value }: { value: unknown }) => {
      if (value === 'removeFromClient' || value === 'changeCategory' || value === 'ignore') {
        setRemovalMethod(value);
      }
    },
    [setRemovalMethod]
  );

  const handleBlocklistMethodChange = useCallback(
    ({ value }: { value: unknown }) => {
      if (value === 'doNotBlocklist' || value === 'blocklistAndSearch' || value === 'blocklistOnly') {
        setBlocklistMethod(value);
      }
    },
    [setBlocklistMethod]
  );

  const handleConfirmRemove = useCallback(() => {
    if (isRemoveDisabled || unavailable) {
      return;
    }

    onRemovePress({
      remove: !isPending && effectiveRemovalMethod === 'removeFromClient',
      changeCategory: !isPending && effectiveRemovalMethod === 'changeCategory',
      blocklist: effectiveBlocklistMethod !== 'doNotBlocklist',
      skipRedownload: effectiveBlocklistMethod === 'blocklistOnly',
    });

    setRemovalMethod('removeFromClient');
    setBlocklistMethod('doNotBlocklist');
  }, [
    isRemoveDisabled,
    effectiveRemovalMethod,
    effectiveBlocklistMethod,
    unavailable,
    isPending,
    setRemovalMethod,
    setBlocklistMethod,
    onRemovePress,
  ]);

  const handleModalClose = useCallback(() => {
    setRemovalMethod('removeFromClient');
    setBlocklistMethod('doNotBlocklist');

    onModalClose();
  }, [setRemovalMethod, setBlocklistMethod, onModalClose]);

  return (
    <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={handleModalClose}>
      <ModalContent onModalClose={handleModalClose}>
        <ModalHeader>{title}</ModalHeader>

        <ModalBody>
          <div className={styles.message}>{message}</div>

          {hasPending && <div className={styles.message}>{translate('QueuePendingRemovalHint')}</div>}

          {isPending ? null : (
            <FormGroup>
              <FormLabel>{translate('RemoveQueueItemRemovalMethod')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="removalMethod"
                value={effectiveRemovalMethod}
                values={removalMethodOptions}
                isDisabled={!canChangeCategory && !canIgnore && !canBlocklist}
                helpTextWarning={translate(
                  'RemoveQueueItemRemovalMethodHelpTextWarning'
                )}
                onChange={handleRemovalMethodChange}
              />
            </FormGroup>
          )}

          <FormGroup>
            <FormLabel>
              {multipleSelected
                ? translate('BlocklistReleases')
                : translate('BlocklistRelease')}
            </FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="blocklistMethod"
              value={effectiveBlocklistMethod}
              values={blocklistMethodOptions}
              isDisabled={!canBlocklist}
              helpText={translate('BlocklistReleaseHelpText')}
              onChange={handleBlocklistMethodChange}
            />
          </FormGroup>
        </ModalBody>

        <ModalFooter>
          <Button onPress={handleModalClose}>{translate('Close')}</Button>

          <Button kind={kinds.DANGER} isDisabled={isRemoveDisabled || unavailable} onPress={handleConfirmRemove}>
            {translate('Remove')}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default RemoveQueueItemModal;
