import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import styles from './CreateLocalRecordingModal.css';

function requestError(xhr, fallback) {
  const failures = xhr.responseJSON;
  if (Array.isArray(failures)) {
    return failures.map((failure) => failure.errorMessage).filter(Boolean).join(' ') || fallback;
  }
  return getErrorMessage(xhr, fallback);
}

class CreateLocalRecordingModal extends Component {

  constructor(props, context) {
    super(props, context);
    this._isMounted = false;
    this._previewGeneration = 0;
    this._abortPreview = null;
    this._saveInFlight = false;
    this.state = { isLoading: false, isSaving: false, error: null, preview: null, fields: {} };
  }

  componentDidMount() {
    this._isMounted = true;
    if (this.props.isOpen) {
      this.loadPreview();
    }
  }

  componentDidUpdate(prevProps) {
    if (this.props.isOpen && !prevProps.isOpen) {
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
    this.setState({ isLoading: true, error: null, preview: null, fields: {} });
    const { request, abortRequest } = createAjaxRequest({
      url: '/localrecording',
      data: { fileIds: this.props.files.map((file) => file.id) },
      traditional: true
    });
    this._abortPreview = abortRequest;
    const isCurrent = () => this._isMounted && this.props.isOpen && generation === this._previewGeneration;
    request.done((preview) => {
      if (isCurrent()) {
        this.setState({
          isLoading: false,
          preview,
          fields: {
            title: preview.title || '',
            subtitle: preview.subtitle || '',
            authorId: preview.authorId || 0,
            narrators: (preview.narrators || []).join('\n'),
            language: preview.language || '',
            publisher: preview.publisher || '',
            year: preview.year ? String(preview.year) : ''
          }
        });
      }
    });
    request.fail((xhr) => {
      if (isCurrent() && !xhr.aborted) {
        this.setState({ isLoading: false, error: requestError(xhr, 'Unable to read the selected files.') });
      }
    });
  };

  onInputChange = ({ name, value }) => {
    this.setState((state) => ({ fields: { ...state.fields, [name]: value }, error: null }));
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
      this.props.onModalClose();
    }
  };

  onCreatePress = () => {
    const { fields, preview, isLoading } = this.state;
    if (this._saveInFlight || isLoading || !preview) {
      return;
    }
    const year = fields.year.trim();
    if (!fields.title.trim() || !this.props.authors.some((author) => author.id === fields.authorId)) {
      this.setState({ error: 'Enter a title and choose an author from your library.' });
      return;
    }
    if (year && (!/^\d{1,4}$/.test(year) || Number(year) === 0)) {
      this.setState({ error: 'Enter a year from 1 to 9999, or leave it blank.' });
      return;
    }

    this._saveInFlight = true;
    this.setState({ isSaving: true, error: null });
    const { request } = createAjaxRequest({
      url: '/localrecording',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({
        fileIds: this.props.files.map((file) => file.id),
        title: fields.title.trim(),
        subtitle: fields.subtitle.trim(),
        authorId: fields.authorId,
        narrators: fields.narrators.split('\n').map((name) => name.trim()).filter(Boolean),
        language: fields.language.trim(),
        publisher: fields.publisher.trim(),
        year: year ? Number(year) : null
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
        this.setState({ isSaving: false, error: requestError(xhr, 'Unable to create the local recording.') });
      }
    });
  };

  renderField(name, label, options = {}) {
    const { type = 'text', helpText, values, ...otherOptions } = options;
    const inputProps = {
      id: `local-recording-${name}`,
      name,
      value: this.state.fields[name],
      disabled: this.state.isSaving,
      onChange: this.onFieldChange,
      ...otherOptions
    };

    return (
      <FormGroup>
        <FormLabel name={`local-recording-${name}`}>{label}</FormLabel>
        <div className={styles.field}>
          {
            type === 'select' &&
              <select className={styles.select} {...inputProps}>
                {values.map((option) => <option key={option.key} value={option.key} disabled={option.isDisabled}>{option.value}</option>)}
              </select>
          }
          {type === 'textarea' && <textarea className={styles.textarea} rows={3} {...inputProps} />}
          {type !== 'select' && type !== 'textarea' && <input className={styles.input} type={type} {...inputProps} />}
          {helpText && <FormInputHelpText text={helpText} />}
        </div>
      </FormGroup>
    );
  }

  render() {
    const { isOpen, files, authors } = this.props;
    const { isLoading, isSaving, error, preview, fields } = this.state;
    const authorOptions = [
      { key: 0, value: 'Select an author', isDisabled: true },
      ...[...authors].sort((a, b) => a.authorName.localeCompare(b.authorName)).map((author) => ({ key: author.id, value: author.authorName }))
    ];

    return (
      <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={this.onModalClose}>
        <ModalContent onModalClose={this.onModalClose}>
          <ModalHeader>Create local recording</ModalHeader>
          <ModalBody>
            <p>Create one library recording from the selected files and their tags. The files stay in their current folders.</p>
            <details className={styles.selectedFiles}>
              <summary>Selected files ({files.length})</summary>
              <ul className={styles.fileList}>
                {files.map((file) => <li className={styles.filePath} key={file.id}>{file.path}</li>)}
              </ul>
            </details>
            {isLoading && <LoadingIndicator />}
            {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
            {!isLoading && error && !preview && <Button onPress={this.onRetryPress}>Retry</Button>}
            {
              preview && !isLoading &&
                <div>
                  {(preview.warnings || []).map((warning, index) => <Alert key={index} kind={kinds.WARNING}>{warning}</Alert>)}
                  {this.renderField('title', 'Title', { autoFocus: true })}
                  {this.renderField('authorId', 'Author', {
                    type: 'select',
                    values: authorOptions,
                    'aria-label': 'Author',
                    helpText: preview.authorName ? `File author: ${preview.authorName}` : 'Choose the author who should own this recording.'
                  })}
                  <details>
                    <summary>Recording details</summary>
                    {this.renderField('subtitle', 'Subtitle')}
                    {this.renderField('narrators', 'Narrators', { type: 'textarea', helpText: 'One narrator per line.' })}
                    {this.renderField('language', 'Language')}
                    {this.renderField('publisher', 'Publisher')}
                    {this.renderField('year', 'Year', { inputMode: 'numeric' })}
                    {preview.runtimeLengthMinutes > 0 && <p>Duration: {preview.runtimeLengthMinutes} minutes</p>}
                  </details>
                </div>
            }
          </ModalBody>
          <ModalFooter>
            <Button isDisabled={isSaving} onPress={this.onModalClose}>Cancel</Button>
            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isSaving}
              isDisabled={isLoading || !preview || !fields.title?.trim() || !fields.authorId}
              onPress={this.onCreatePress}
            >
              Create recording
            </SpinnerButton>
          </ModalFooter>
        </ModalContent>
      </Modal>
    );
  }
}

CreateLocalRecordingModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  files: PropTypes.arrayOf(PropTypes.object).isRequired,
  authors: PropTypes.arrayOf(PropTypes.object).isRequired,
  onModalClose: PropTypes.func.isRequired,
  onCreated: PropTypes.func.isRequired
};

export default connect((state) => ({ authors: state.authors.items }))(CreateLocalRecordingModal);
