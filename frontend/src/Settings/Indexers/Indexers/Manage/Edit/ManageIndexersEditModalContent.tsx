import { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './ManageIndexersEditModalContent.css';

interface SavePayload {
  enableRss?: boolean;
  enableAutomaticSearch?: boolean;
  enableInteractiveSearch?: boolean;
  priority?: number;
}

interface ManageIndexersEditModalContentProps {
  indexerIds: number[];
  onSavePress(payload: object): void;
  onModalClose(): void;
}

const NO_CHANGE = 'noChange';

const enableOptions = [
  {
    key: NO_CHANGE,
    get value() {
      return translate('NoChange');
    },
    isDisabled: true,
  },
  {
    key: 'enabled',
    get value() {
      return translate('Enabled');
    },
  },
  {
    key: 'disabled',
    get value() {
      return translate('Disabled');
    },
  },
];

function ManageIndexersEditModalContent(
  props: ManageIndexersEditModalContentProps
) {
  const { indexerIds, onSavePress, onModalClose } = props;

  const [enableRss, setEnableRss] = useState(NO_CHANGE);
  const [enableAutomaticSearch, setEnableAutomaticSearch] = useState(NO_CHANGE);
  const [enableInteractiveSearch, setEnableInteractiveSearch] =
    useState(NO_CHANGE);
  const [priority, setPriority] = useState<number | null>(null);

  const save = useCallback(() => {
    let hasChanges = false;
    const payload: SavePayload = {};

    if (enableRss !== NO_CHANGE) {
      hasChanges = true;
      payload.enableRss = enableRss === 'enabled';
    }

    if (enableAutomaticSearch !== NO_CHANGE) {
      hasChanges = true;
      payload.enableAutomaticSearch = enableAutomaticSearch === 'enabled';
    }

    if (enableInteractiveSearch !== NO_CHANGE) {
      hasChanges = true;
      payload.enableInteractiveSearch = enableInteractiveSearch === 'enabled';
    }

    if (priority !== null) {
      hasChanges = true;
      payload.priority = priority;
    }

    if (hasChanges) {
      onSavePress(payload);
    }

    onModalClose();
  }, [
    enableRss,
    enableAutomaticSearch,
    enableInteractiveSearch,
    priority,
    onSavePress,
    onModalClose,
  ]);

  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) => {
      switch (name) {
        case 'enableRss':
          if (value === 'enabled' || value === 'disabled' || value === NO_CHANGE) {
            setEnableRss(value);
          }
          break;
        case 'enableAutomaticSearch':
          if (value === 'enabled' || value === 'disabled' || value === NO_CHANGE) {
            setEnableAutomaticSearch(value);
          }
          break;
        case 'enableInteractiveSearch':
          if (value === 'enabled' || value === 'disabled' || value === NO_CHANGE) {
            setEnableInteractiveSearch(value);
          }
          break;
        case 'priority':
          if (value === null) {
            setPriority(null);
          } else if (typeof value === 'number' && Number.isFinite(value)) {
            setPriority(value);
          }
          break;
        default:
          console.warn(`EditIndexersModalContent Unknown Input: '${name}'`);
      }
    },
    []
  );

  const selectedCount = indexerIds.length;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('EditSelectedIndexers')}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{translate('EnableRSS')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="enableRss"
            value={enableRss}
            values={enableOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('EnableAutomaticSearch')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="enableAutomaticSearch"
            value={enableAutomaticSearch}
            values={enableOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('EnableInteractiveSearch')}</FormLabel>

          <FormInputGroup
            type={inputTypes.SELECT}
            name="enableInteractiveSearch"
            value={enableInteractiveSearch}
            values={enableOptions}
            onChange={onInputChange}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>{translate('Priority')}</FormLabel>

          <FormInputGroup
            type={inputTypes.NUMBER}
            name="priority"
            value={priority}
            min={1}
            max={50}
            onChange={onInputChange}
          />
        </FormGroup>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div className={styles.selected}>
          {translate('CountIndexersSelected', { selectedCount })}
        </div>

        <div>
          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <Button onPress={save}>{translate('ApplyChanges')}</Button>
        </div>
      </ModalFooter>
    </ModalContent>
  );
}

export default ManageIndexersEditModalContent;
