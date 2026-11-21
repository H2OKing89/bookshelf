import PropTypes from 'prop-types';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import SelectInput from 'Components/Form/SelectInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import getErrorMessage from 'Utilities/Object/getErrorMessage';

const terminalStates = new Set(['failed', 'cancelled', 'aborted', 'orphaned']);
const referenceNames = {
  Blocklist: 'Blocklist entries',
  DownloadHistory: 'Download history entries',
  ExtraFiles: 'Extra files',
  History: 'History entries',
  MetadataFiles: 'Metadata sidecars',
  PendingReleases: 'Pending releases'
};

const settings = [
  ['qualityProfile', 'Quality profile ID', (profile) => profile.qualityProfileId],
  ['metadataProfile', 'Metadata profile ID', (profile) => profile.metadataProfileId],
  ['monitoring', 'Author monitored', (profile) => (profile.monitored ? 'Yes' : 'No')],
  ['monitorNewItems', 'Monitor new books', (profile) => profile.monitorNewItems],
  ['tags', 'Tag IDs', (profile) => profile.tags?.join(', ') || 'None']
];

function ReconciliationPreview({ preview }) {
  const {
    source,
    target,
    recordings,
    files,
    references,
    settingsDifferences,
    blockers,
    existingOverrideId
  } = preview;

  return (
    <div style={{ overflowWrap: 'anywhere' }}>
      <h3>Reconciliation preview</h3>
      <p>
        Move the listed recordings from <strong>{source.name}</strong> ({source.foreignAuthorId})
        {' to '}<strong>{target.name}</strong> ({target.foreignAuthorId}).
        {' The canonical settings and recording identities will be kept. Files stay in their current folders.'}
      </p>
      <p>{existingOverrideId ? 'The existing metadata correction will retain its aliases and include this review.' :
        'A metadata correction will record this author credit for future lookups.'}</p>

      {
        !!blockers.length &&
          <Alert kind={kinds.DANGER}>
            <ul>{blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}</ul>
          </Alert>
      }

      {
        !!settingsDifferences.length &&
          <Alert kind={kinds.WARNING}>
            <strong>Settings that differ</strong>
            <ul>
              {settings.filter(([key]) => settingsDifferences.includes(key)).map(([key, label, getValue]) => (
                <li key={key}>{label} changes from {getValue(source)} to {getValue(target)}.</li>
              ))}
            </ul>
          </Alert>
      }

      <h4>Recordings to move</h4>
      {
        recordings.length ?
          <ul>
            {recordings.map((recording) => (
              <li key={`${recording.bookId}-${recording.editionId}`}>
                {recording.title} ({recording.foreignEditionId || recording.foreignBookId || `Book ${recording.bookId}`})
              </li>
            ))}
          </ul> :
          <p>The name-only profile has no recordings.</p>
      }
      {
        !!files.length &&
          <details>
            <summary>Files ({files.length})</summary>
            <ul>{files.map((file) => <li key={file.id}>{file.path} (file {file.id})</li>)}</ul>
          </details>
      }
      <details>
        <summary>Author references</summary>
        <ul>
          {references.map(({ table, count }) => <li key={table}>{referenceNames[table] || table}: {count}</li>)}
        </ul>
      </details>
    </div>
  );
}

ReconciliationPreview.propTypes = {
  preview: PropTypes.object.isRequired
};

function AuthorReconciliationModal({ sourceAuthorId, onModalClose, onReconciled }) {
  const [candidateInputId] = useState(getUniqueElementId);
  const [candidates, setCandidates] = useState(null);
  const [fields, setFields] = useState({ targetAuthorId: '', evidenceUrl: '', reason: '' });
  const [preview, setPreview] = useState(null);
  const [keepCanonicalSettings, setKeepCanonicalSettings] = useState(false);
  const [error, setError] = useState(null);
  const [isReviewing, setIsReviewing] = useState(false);
  const [isApplying, setIsApplying] = useState(false);
  const abortCurrent = useRef(null);
  const pollTimer = useRef(null);

  const send = useCallback((options, done, failed) => {
    const { request, abortRequest } = createAjaxRequest({ dataType: 'json', ...options });
    abortCurrent.current = abortRequest;
    request.done(done).fail((xhr) => {
      if (!xhr.aborted) {
        failed(xhr);
      }
    }).always(() => {
      if (abortCurrent.current === abortRequest) {
        abortCurrent.current = null;
      }
    });
  }, []);

  useEffect(() => {
    send(
      { url: '/authorreconciliation/candidates', data: { sourceAuthorId } },
      setCandidates,
      (xhr) => {
        setCandidates([]);
        setError(getErrorMessage(xhr, 'Unable to find canonical author profiles.'));
      }
    );
    return () => {
      clearTimeout(pollTimer.current);
      abortCurrent.current?.();
    };
  }, [send, sourceAuthorId]);

  const updateField = ({ name, value }) => {
    setFields((previous) => ({ ...previous, [name]: value }));
    setPreview(null);
    setKeepCanonicalSettings(false);
    setError(null);
  };
  const requestBody = () => ({
    sourceAuthorId,
    targetAuthorId: Number(fields.targetAuthorId),
    evidenceUrl: fields.evidenceUrl.trim(),
    reason: fields.reason.trim()
  });
  const review = () => {
    setIsReviewing(true);
    setError(null);
    setPreview(null);
    setKeepCanonicalSettings(false);
    send(
      { url: '/authorreconciliation/preview', method: 'POST', data: JSON.stringify(requestBody()) },
      (result) => {
        setPreview(result);
        setIsReviewing(false);
      },
      (xhr) => {
        setError(getErrorMessage(xhr, 'Unable to preview author reconciliation.'));
        setIsReviewing(false);
      }
    );
  };
  const apply = () => {
    setIsApplying(true);
    setError(null);
    const fail = (message) => {
      setIsApplying(false);
      setPreview(null);
      setError(message);
    };
    const poll = (commandId) => {
      send({ url: `/command/${commandId}` }, (command) => {
        if (command.status === 'completed') {
          onReconciled(preview.target.titleSlug);
        } else if (terminalStates.has(command.status)) {
          fail('Reconciliation did not complete. Review the current profiles and try again.');
        } else {
          pollTimer.current = setTimeout(() => poll(commandId), 500);
        }
      }, () => fail('Unable to read the reconciliation result. Check System Tasks for its progress.'));
    };
    send({
      url: '/authorreconciliation',
      method: 'POST',
      data: JSON.stringify({ ...requestBody(), previewToken: preview.previewToken, keepCanonicalSettings })
    }, ({ commandId }) => poll(commandId), (xhr) => fail(getErrorMessage(xhr, 'Unable to apply author reconciliation.')));
  };
  const close = () => {
    if (!isApplying) {
      onModalClose();
    }
  };
  const busy = isReviewing || isApplying;
  const canReview = !!(fields.targetAuthorId && fields.evidenceUrl.trim() && fields.reason.trim());
  const canApply = preview?.canApply && (!preview.settingsDifferences.length || keepCanonicalSettings);

  return (
    <Modal isOpen={true} onModalClose={close} size={sizes.LARGE}>
      <ModalContent onModalClose={close} showCloseButton={!isApplying}>
        <ModalHeader>Reconcile Author</ModalHeader>
        <ModalBody>
          {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
          {candidates === null && <LoadingIndicator />}
          {candidates?.length === 0 && !error && <p>No saved canonical author matches this name credit.</p>}
          {
            !!candidates?.length &&
              <>
                <FormGroup>
                  <FormLabel name={candidateInputId}>Canonical author</FormLabel>
                  <SelectInput
                    id={candidateInputId}
                    name="targetAuthorId"
                    value={fields.targetAuthorId}
                    values={[{ key: '', value: 'Choose a canonical author' }, ...candidates.map((candidate) => ({
                      key: String(candidate.id),
                      value: `${candidate.name} (${candidate.foreignAuthorId})`
                    }))]}
                    isDisabled={busy}
                    onChange={updateField}
                  />
                </FormGroup>
                <FormGroup>
                  <FormLabel>Evidence URL</FormLabel>
                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="evidenceUrl"
                    helpText="Link to the source that connects this name credit to the canonical author."
                    value={fields.evidenceUrl}
                    isDisabled={busy}
                    onChange={updateField}
                  />
                </FormGroup>
                <FormGroup>
                  <FormLabel>Reason</FormLabel>
                  <FormInputGroup
                    type={inputTypes.TEXT_AREA}
                    name="reason"
                    value={fields.reason}
                    isDisabled={busy}
                    onChange={updateField}
                  />
                </FormGroup>
              </>
          }
          {preview && <ReconciliationPreview preview={preview} />}
          {
            !!preview?.settingsDifferences.length &&
              <FormGroup>
                <FormLabel>Keep canonical author settings</FormLabel>
                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="keepCanonicalSettings"
                  value={keepCanonicalSettings}
                  isDisabled={busy}
                  onChange={({ value }) => setKeepCanonicalSettings(value)}
                />
              </FormGroup>
          }
          {isApplying && <p role="status">Applying the reviewed author reconciliation…</p>}
        </ModalBody>
        <ModalFooter>
          <Button isDisabled={isApplying} onPress={close}>Cancel</Button>
          <SpinnerButton aria-label="Review reconciliation" isDisabled={!canReview || isApplying} isSpinning={isReviewing} onPress={review}>
            Review
          </SpinnerButton>
          {
            preview &&
              <SpinnerButton aria-label="Apply reconciliation" kind={kinds.DANGER} isDisabled={!canApply || isReviewing} isSpinning={isApplying} onPress={apply}>
                Apply
              </SpinnerButton>
          }
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

AuthorReconciliationModal.propTypes = {
  sourceAuthorId: PropTypes.number.isRequired,
  onModalClose: PropTypes.func.isRequired,
  onReconciled: PropTypes.func.isRequired
};

export default AuthorReconciliationModal;
