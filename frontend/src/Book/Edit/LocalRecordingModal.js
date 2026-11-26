import PropTypes from 'prop-types';
import { Component } from 'react';
import { connect } from 'react-redux';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import styles from './LocalRecordingModal.css';

function requestError(xhr, fallback) {
  const failures = xhr.responseJSON;
  if (Array.isArray(failures)) {
    return failures.map((failure) => failure.errorMessage).filter(Boolean).join(' ') || fallback;
  }
  return getErrorMessage(xhr, fallback);
}

const sourceLabels = {
  fileTags: 'File tags',
  fileName: 'Filename',
  manual: 'Manual edit',
  legacy: 'Existing local metadata',
  unknown: 'Unknown'
};

class LocalRecordingModal extends Component {

  constructor(props, context) {
    super(props, context);
    this._isMounted = false;
    this._previewGeneration = 0;
    this._abortPreview = null;
    this._saveInFlight = false;
    this._fieldId = getUniqueElementId();
    this.state = { isLoading: false, isSaving: false, error: null, preview: null, fields: {}, initialFields: {}, fieldErrors: {}, conflict: false, isDiscardOpen: false };
  }

  componentDidMount() {
    this._isMounted = true;
    if (this.props.isOpen) {
      this.loadPreview();
    }
  }

  componentDidUpdate(prevProps) {
    if (this.props.isOpen && (!prevProps.isOpen || this.props.bookId !== prevProps.bookId)) {
      this.loadPreview();
    } else if (!this.props.isOpen && prevProps.isOpen) {
      this.cancelPreview();
    }
  }

  componentWillUnmount() {
    this._isMounted = false;
    this.cancelPreview();
  }

  cancelPreview() {
    this._previewGeneration++;
    if (this._abortPreview) {
      this._abortPreview();
      this._abortPreview = null;
    }
  }

  loadPreview = () => {
    this.cancelPreview();
    const generation = this._previewGeneration;
    this.setState({ isLoading: true, error: null, preview: null, fields: {}, initialFields: {}, fieldErrors: {}, conflict: false, isDiscardOpen: false });
    const { bookId, files } = this.props;
    const { request, abortRequest } = createAjaxRequest({
      url: bookId ? `/localrecording/${bookId}` : '/localrecording',
      data: bookId ? undefined : { fileIds: files.map((file) => file.id) },
      traditional: true
    });
    this._abortPreview = abortRequest;
    const isCurrent = () => this._isMounted && this.props.isOpen && generation === this._previewGeneration;
    request.done((preview) => {
      if (isCurrent()) {
        const fields = {
          title: preview.title || '',
          subtitle: preview.subtitle || '',
          authorId: preview.authorId || 0,
          newAuthorName: preview.authorId ? '' : preview.authorName || '',
          newAuthorPath: preview.newAuthorPath || '',
          narrators: (preview.narrators || []).join('\n'),
          language: preview.language || '',
          publisher: preview.publisher || '',
          year: preview.year === null || preview.year === undefined ? '' : String(preview.year),
          chapterMetadata: preview.chapterMetadata ? {
            ...preview.chapterMetadata,
            chapters: (preview.chapterMetadata.chapters || []).map((chapter) => ({
              ...chapter,
              title: chapter.title || '',
              startOffsetMs: String(chapter.startOffsetMs),
              lengthMs: String(chapter.lengthMs)
            }))
          } : null
        };
        this.setState({
          isLoading: false,
          preview,
          fields,
          initialFields: fields
        });
      }
    });
    request.fail((xhr) => {
      if (isCurrent() && !xhr.aborted) {
        this.setState({ isLoading: false, error: requestError(xhr, bookId ? 'Unable to load the local recording.' : 'Unable to read the selected files.') });
      }
    });
  };

  onInputChange = ({ name, value }) => {
    this.setState((state) => ({
      fields: { ...state.fields, [name]: value },
      fieldErrors: { ...state.fieldErrors, [name]: undefined },
      error: null
    }));
  };

  onFieldChange = (event) => {
    const { name, value } = event.target;
    this.onInputChange({ name, value: name === 'authorId' ? Number(value) : value });
  };

  onRetryPress = () => {
    this.loadPreview();
  };

  onModalClose = () => {
    if (!this._saveInFlight) {
      if (this.props.bookId && JSON.stringify(this.state.fields) !== JSON.stringify(this.state.initialFields)) {
        this.setState({ isDiscardOpen: true });
      } else {
        this.props.onModalClose();
      }
    }
  };

  onDiscard = () => {
    this.setState({ isDiscardOpen: false });
    this.props.onModalClose();
  };

  onKeepEditing = () => this.setState({ isDiscardOpen: false });

  onChapterChange = (index, name, value) => {
    const metadata = this.state.fields.chapterMetadata;
    this.onInputChange({
      name: 'chapterMetadata',
      value: { ...metadata, chapters: metadata.chapters.map((chapter, chapterIndex) => (chapterIndex === index ? { ...chapter, [name]: value } : chapter)) }
    });
  };

  onAddChapter = () => {
    const metadata = this.state.fields.chapterMetadata || { chapters: [] };
    const last = metadata.chapters.at(-1);
    const nextOffset = last ? Number(last.startOffsetMs) + Number(last.lengthMs) : 0;
    this.onInputChange({
      name: 'chapterMetadata',
      value: {
        ...metadata,
        chapters: [...metadata.chapters, { ordinal: last ? last.ordinal + 1 : 1, title: '', startOffsetMs: String(nextOffset), lengthMs: '0' }]
      }
    });
  };

  onRemoveChapter = (index) => {
    const metadata = this.state.fields.chapterMetadata;
    this.onInputChange({ name: 'chapterMetadata', value: { ...metadata, chapters: metadata.chapters.filter((_chapter, chapterIndex) => chapterIndex !== index) } });
  };

  onSavePress = () => {
    const { fields, preview, isLoading, conflict } = this.state;
    const { bookId, files, authors } = this.props;
    if (this._saveInFlight || isLoading || !preview || conflict) {
      return;
    }
    const year = fields.year.trim();
    const fieldErrors = {};
    if (!fields.title.trim()) {
      fieldErrors.title = 'Enter a title.';
    }
    if (!bookId) {
      if (fields.authorId === -1) {
        if (!fields.newAuthorName.trim()) {fieldErrors.newAuthorName = 'Enter a local author name.';}
        if (!fields.newAuthorPath.trim()) {fieldErrors.newAuthorPath = 'Enter the author folder containing these files.';}
      } else if (!authors.some((author) => author.id === fields.authorId)) {
        fieldErrors.authorId = 'Choose an author or create a local author.';
      }
    }
    if (year && (!/^\d{1,4}$/.test(year) || Number(year) === 0)) {
      fieldErrors.year = 'Enter a year from 1 to 9999, or leave it blank.';
    }
    const chapterMetadata = fields.chapterMetadata ? { ...fields.chapterMetadata, chapters: [] } : null;
    let previousEnd = 0;
    for (const chapter of fields.chapterMetadata?.chapters || []) {
      const startOffsetMs = Number(chapter.startOffsetMs);
      const lengthMs = Number(chapter.lengthMs);
      if (!/^\d+$/.test(chapter.startOffsetMs) || !/^\d+$/.test(chapter.lengthMs) ||
        !Number.isSafeInteger(startOffsetMs) || !Number.isSafeInteger(lengthMs) ||
        !Number.isSafeInteger(startOffsetMs + lengthMs) || startOffsetMs < previousEnd) {
        fieldErrors.chapterMetadata = 'Enter whole, nonnegative chapter times in milliseconds without overlapping chapters.';
        break;
      }
      chapterMetadata.chapters.push({ ...chapter, title: chapter.title || null, startOffsetMs, lengthMs });
      previousEnd = startOffsetMs + lengthMs;
    }
    if (Object.keys(fieldErrors).length) {
      this.setState({ error: Object.values(fieldErrors).join(' '), fieldErrors });
      return;
    }

    this._saveInFlight = true;
    this.setState({ isSaving: true, error: null, fieldErrors: {} });
    const { request } = createAjaxRequest({
      url: bookId ? `/localrecording/${bookId}` : '/localrecording',
      method: bookId ? 'PUT' : 'POST',
      dataType: 'json',
      data: JSON.stringify({
        ...(bookId ? { bookId, revision: preview.revision, recordingId: preview.recordingId, editionId: preview.editionId } : { fileIds: files.map((file) => file.id) }),
        title: fields.title.trim(),
        subtitle: fields.subtitle.trim(),
        authorId: fields.authorId === -1 ? 0 : fields.authorId,
        ...(fields.authorId === -1 ? { newAuthorName: fields.newAuthorName.trim(), newAuthorPath: fields.newAuthorPath.trim() } : {}),
        narrators: fields.narrators.split('\n').map((name) => name.trim()).filter(Boolean),
        language: fields.language.trim(),
        publisher: fields.publisher.trim(),
        year: year ? Number(year) : null,
        chapterMetadata
      })
    });
    request.done((book) => {
      this._saveInFlight = false;
      if (this._isMounted) {
        this.setState({ isSaving: false });
        this.props.onCreated(book);
      }
    });
    request.fail((xhr) => {
      this._saveInFlight = false;
      if (this._isMounted) {
        this.setState({ isSaving: false, conflict: !!bookId && xhr.status === 409, error: requestError(xhr, bookId ? 'Unable to update the local recording.' : 'Unable to create the local recording.') });
      }
    });
  };

  renderField(name, label, options = {}) {
    const { type = 'text', helpText, values, ...otherOptions } = options;
    const id = `${this._fieldId}-${name}`;
    const error = this.state.fieldErrors[name];
    const source = this.props.bookId && this.state.preview?.provenance?.[name];
    const help = [helpText, source && sourceLabels[source] ? `Source: ${sourceLabels[source]}` : null].filter(Boolean).join(' ');
    const helpId = help ? `${id}-help` : undefined;
    const errorId = error ? `${id}-error` : undefined;
    const inputProps = {
      id,
      name,
      value: this.state.fields[name],
      disabled: this.state.isSaving,
      'aria-describedby': [errorId, helpId].filter(Boolean).join(' ') || undefined,
      'aria-invalid': error ? true : undefined,
      onChange: this.onFieldChange,
      ...otherOptions
    };

    return (
      <FormGroup>
        <FormLabel htmlFor={id} hasError={!!error}>{label}</FormLabel>
        <div className={styles.field}>
          {
            type === 'select' &&
              <select className={styles.select} {...inputProps}>
                {values.map((option) => <option key={option.key} value={option.key} disabled={option.isDisabled}>{option.value}</option>)}
              </select>
          }
          {type === 'textarea' && <textarea className={styles.textarea} rows={3} {...inputProps} />}
          {type !== 'select' && type !== 'textarea' && <input className={styles.input} type={type} {...inputProps} />}
          {error && <FormInputHelpText id={errorId} text={error} isError={true} />}
          {help && <FormInputHelpText id={helpId} text={help} />}
        </div>
      </FormGroup>
    );
  }

  renderChapters() {
    const { fields, preview, fieldErrors, isSaving } = this.state;
    const chapters = fields.chapterMetadata?.chapters || [];
    const source = this.props.bookId && sourceLabels[preview.provenance?.chapters];

    return (
      <details>
        <summary>Chapters ({chapters.length})</summary>
        <p>Chapter timings refer to the full recording, in milliseconds. {source && `Source: ${source}`}</p>
        {fieldErrors.chapterMetadata && <FormInputHelpText text={fieldErrors.chapterMetadata} isError={true} />}
        {chapters.map((chapter, index) => (
          <div className={styles.chapter} key={chapter.ordinal}>
            {[
              ['title', 'Chapter title', 'text'],
              ['startOffsetMs', 'Start offset (ms)', 'numeric'],
              ['lengthMs', 'Length (ms)', 'numeric']
            ].map(([name, label, inputMode]) => {
              const id = `${this._fieldId}-chapter-${chapter.ordinal}-${name}`;
              return (
                <FormGroup key={name}>
                  <FormLabel htmlFor={id}>{label} {index + 1}</FormLabel>
                  <div className={styles.field}>
                    <input id={id} className={styles.input} type="text" inputMode={inputMode} disabled={isSaving} value={chapter[name]} onChange={(event) => this.onChapterChange(index, name, event.target.value)} />
                  </div>
                </FormGroup>
              );
            })}
            <Button isDisabled={isSaving} onPress={() => this.onRemoveChapter(index)}>Remove chapter {index + 1}</Button>
          </div>
        ))}
        <Button isDisabled={isSaving} onPress={this.onAddChapter}>Add chapter</Button>
      </details>
    );
  }

  render() {
    const { isOpen, files, authors, bookId } = this.props;
    const { isLoading, isSaving, error, preview, fields, conflict, isDiscardOpen } = this.state;
    const authorOptions = [
      { key: 0, value: 'Select an author', isDisabled: true },
      ...[...authors].sort((a, b) => a.authorName.localeCompare(b.authorName)).map((author) => ({ key: author.id, value: author.authorName })),
      { key: -1, value: 'Create a local author' }
    ];

    return (
      <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={this.onModalClose}>
        <ModalContent onModalClose={this.onModalClose}>
          <ModalHeader>{bookId ? 'Edit local recording' : 'Create local recording'}</ModalHeader>
          <ModalBody>
            <p>{bookId ? 'Edit the library metadata without changing the recording identity or writing to the audio files.' : 'Create one library recording from the selected files and their tags. The files stay in their current folders.'}</p>
            {!bookId &&
              <details className={styles.selectedFiles}>
                <summary>Selected files ({files.length})</summary>
                <ul className={styles.fileList}>
                  {files.map((file) => <li className={styles.filePath} key={file.id}>{file.path}</li>)}
                </ul>
              </details>
            }
            {isLoading && <LoadingIndicator />}
            {error && <Alert kind={kinds.DANGER} role="alert">{error}</Alert>}
            {!isLoading && error && !preview && <Button onPress={this.onRetryPress}>Retry</Button>}
            {conflict && <Button onPress={this.onRetryPress}>Reload metadata and discard draft</Button>}
            {
              preview && !isLoading &&
                <div>
                  {(preview.warnings || []).map((warning, index) => <Alert key={index} kind={kinds.WARNING}>{warning}</Alert>)}
                  {this.renderField('title', 'Title', { autoFocus: true })}
                  {bookId ?
                    <p>Author: {preview.authorName}</p> :
                    this.renderField('authorId', 'Author', {
                      type: 'select',
                      values: authorOptions,
                      helpText: preview.authorName ? `File author: ${preview.authorName}` : 'Choose the author who should own this recording.'
                    })
                  }
                  {!bookId && fields.authorId === -1 &&
                    <>
                      {this.renderField('newAuthorName', 'Local author name')}
                      {this.renderField('newAuthorPath', 'Local author folder', { helpText: 'Use an existing folder inside an audio root that contains all selected files.' })}
                      <p>The local author uses the root folder's default profiles and starts unmonitored. No catalog lookup is required.</p>
                    </>
                  }
                  <details open={!!bookId}>
                    <summary>Recording details</summary>
                    {this.renderField('subtitle', 'Subtitle')}
                    {this.renderField('narrators', 'Narrators', { type: 'textarea', helpText: 'One narrator per line.' })}
                    {this.renderField('language', 'Language')}
                    {this.renderField('publisher', 'Publisher')}
                    {this.renderField('year', 'Year', { inputMode: 'numeric' })}
                    {preview.runtimeLengthMinutes > 0 && <p>Duration: {preview.runtimeLengthMinutes} minutes</p>}
                  </details>
                  {this.renderChapters()}
                </div>
            }
          </ModalBody>
          <ModalFooter>
            <Button isDisabled={isSaving} onPress={this.onModalClose}>Cancel</Button>
            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isSaving}
              isDisabled={isLoading || !preview || !fields.title?.trim() || !fields.authorId || conflict}
              onPress={this.onSavePress}
            >
              {bookId ? 'Save metadata' : 'Create recording'}
            </SpinnerButton>
          </ModalFooter>
        </ModalContent>
        <ConfirmModal
          isOpen={isDiscardOpen}
          kind={kinds.DANGER}
          title="Discard metadata changes?"
          message="Your unsaved local recording changes will be lost."
          confirmLabel="Discard changes"
          cancelLabel="Keep editing"
          onConfirm={this.onDiscard}
          onCancel={this.onKeepEditing}
        />
      </Modal>
    );
  }
}

LocalRecordingModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  bookId: PropTypes.number,
  files: PropTypes.arrayOf(PropTypes.object),
  authors: PropTypes.arrayOf(PropTypes.object).isRequired,
  onModalClose: PropTypes.func.isRequired,
  onCreated: PropTypes.func.isRequired
};

export default connect((state) => ({ authors: state.authors.items }))(LocalRecordingModal);
