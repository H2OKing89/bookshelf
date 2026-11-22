import React, { useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormLabel from 'Components/Form/FormLabel';
import SelectInput from 'Components/Form/SelectInput';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { queueProtocols, queueStatuses } from './queueFilters';
import { setQueueFilters, useQueuePreferences } from './queuePreferences';

function filterValues(choices) {
  return [{ key: '', value: translate('All') }, ...choices.map(([key, label]) => ({ key, value: translate(label) }))];
}

export default function QueueFilterButton() {
  const { filters } = useQueuePreferences();
  const [draft, setDraft] = useState(null);
  const activeCount = Number(!!filters.status) + Number(!!filters.protocol);
  const label = translate('QueueFilters');

  const close = () => setDraft(null);
  const onChange = ({ name, value }) => setDraft((current) => ({ ...current, [name]: value }));
  const onApply = () => {
    setQueueFilters(draft);
    close();
  };

  return (
    <>
      <PageToolbarButton
        label={activeCount ? `${label} (${activeCount})` : label}
        iconName={icons.FILTER}
        aria-haspopup="dialog"
        onPress={() => setDraft(filters)}
      />

      {
        draft &&
          <Modal isOpen={true} size={sizes.SMALL} onModalClose={close}>
            <ModalContent onModalClose={close}>
              <ModalHeader>{label}</ModalHeader>
              <ModalBody>
                <FormGroup>
                  <FormLabel htmlFor="queue-filter-status">{translate('QueueDownloadStatus')}</FormLabel>
                  <SelectInput
                    id="queue-filter-status"
                    name="status"
                    value={draft.status}
                    values={filterValues(queueStatuses)}
                    autoFocus={false}
                    onChange={onChange}
                  />
                </FormGroup>

                <FormGroup>
                  <FormLabel htmlFor="queue-filter-protocol">{translate('Protocol')}</FormLabel>
                  <SelectInput
                    id="queue-filter-protocol"
                    name="protocol"
                    value={draft.protocol}
                    values={filterValues(queueProtocols)}
                    autoFocus={false}
                    onChange={onChange}
                  />
                </FormGroup>
              </ModalBody>

              <ModalFooter>
                <Button onPress={close}>{translate('Cancel')}</Button>
                <Button kind={kinds.PRIMARY} onPress={onApply}>{translate('ApplyFilters')}</Button>
              </ModalFooter>
            </ModalContent>
          </Modal>
      }
    </>
  );
}
