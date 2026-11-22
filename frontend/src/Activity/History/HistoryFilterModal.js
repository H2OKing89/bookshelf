import PropTypes from 'prop-types';
import React, { useId, useMemo, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import CheckInput from 'Components/Form/CheckInput';
import FormGroup from 'Components/Form/FormGroup';
import FormLabel from 'Components/Form/FormLabel';
import SelectInput from 'Components/Form/SelectInput';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { historyEventChoices, normalizeHistoryEventTypes, parseHistoryBookId } from './historyFilters';

function bookOptions(books, authors, selectedBook) {
  const authorNames = new Map(authors.map(({ id, authorName }) => [id, authorName]));
  const choices = books.map(({ id, authorId, title, displayTitle, disambiguation }) => {
    const author = authorNames.get(authorId);
    const name = [displayTitle || title, author, disambiguation].filter(Boolean).join(' · ');
    return { key: id, value: name };
  });
  const counts = new Map();

  for (const { value } of choices) {
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }

  const options = choices
    .map(({ key, value }) => ({ key, value: counts.get(value) > 1 ? `${value} (#${key})` : value }))
    .sort((left, right) => left.value.localeCompare(right.value) || left.key - right.key);

  if (selectedBook !== undefined && !choices.some(({ key }) => key === selectedBook)) {
    options.unshift({ key: selectedBook, value: translate('HistoryFilterUnavailableBook', { id: selectedBook }) });
  }

  return [{ key: '', value: translate('AllBooks') }, ...options];
}

function HistoryFilterEditor({ historyFilter, books, authors, onApplyFilters, onModalClose }) {
  const id = useId();
  const [bookId, setBookId] = useState(historyFilter.bookId);
  const [eventTypes, setEventTypes] = useState(historyFilter.eventTypes);
  const options = useMemo(() => bookOptions(books, authors, bookId), [books, authors, bookId]);

  const apply = () => {
    onApplyFilters({ bookId, eventTypes: normalizeHistoryEventTypes(eventTypes) });
    onModalClose();
  };

  return (
    <Modal isOpen={true} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{translate('HistoryFilters')}</ModalHeader>

        <ModalBody>
          <FormGroup>
            <FormLabel htmlFor={id}>{translate('Book')}</FormLabel>
            <SelectInput
              id={id}
              name="historyBookId"
              value={bookId ?? ''}
              values={options}
              onChange={({ value }) => setBookId(parseHistoryBookId(value))}
            />
          </FormGroup>

          <FieldSet legend={translate('Events')} size={sizes.SMALL}>
            {
              historyEventChoices.map(({ key, value: eventType, label }) => (
                <CheckInput
                  key={key}
                  name={`historyEventType-${eventType}`}
                  aria-label={label}
                  helpText={label}
                  value={eventTypes.includes(eventType)}
                  onChange={({ value }) => setEventTypes((current) => (value ?
                    [...current, eventType] : current.filter((item) => item !== eventType)))}
                />
              ))
            }
          </FieldSet>
        </ModalBody>

        <ModalFooter>
          <Button onPress={() => { setBookId(undefined); setEventTypes([]); }}>
            {translate('Clear')}
          </Button>

          <Button onPress={onModalClose}>{translate('Cancel')}</Button>

          <Button kind={kinds.PRIMARY} onPress={apply}>{translate('ApplyFilters')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

HistoryFilterEditor.propTypes = {
  historyFilter: PropTypes.object.isRequired,
  books: PropTypes.arrayOf(PropTypes.object).isRequired,
  authors: PropTypes.arrayOf(PropTypes.object).isRequired,
  onApplyFilters: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

function HistoryFilterModal({ isOpen, ...props }) {
  return isOpen ? <HistoryFilterEditor {...props} /> : null;
}

HistoryFilterModal.propTypes = {
  isOpen: PropTypes.bool.isRequired
};

export default HistoryFilterModal;
