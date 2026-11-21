import PropTypes from 'prop-types';
import React, { useEffect, useRef, useState } from 'react';
import { useSelector } from 'react-redux';
import Alert from 'Components/Alert';
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
import translate from 'Utilities/String/translate';
import styles from './RecordingReviewModal.css';

const reviewReasons = {
  alreadyAssociated: 'RecordingReviewAlreadyAssociated',
  ambiguousCatalogMatch: 'RecordingReviewAmbiguousCatalogMatch',
  authorMissing: 'RecordingReviewAuthorMissing',
  catalogMissing: 'RecordingReviewCatalogMissing',
  differentPerformance: 'RecordingReviewDifferentPerformance',
  exactCatalogMatch: 'RecordingReviewExactCatalogMatch',
  existingOrMultipartRecording: 'RecordingReviewExistingOrMultipart',
  fileChanged: 'RecordingReviewFileChanged',
  fileUnavailable: 'RecordingReviewFileUnavailable',
  identifierConflict: 'RecordingReviewIdentifierConflict',
  insufficientEvidence: 'RecordingReviewInsufficientEvidence',
  outsideAuthorFolder: 'RecordingReviewOutsideAuthorFolder',
  unsupportedFile: 'RecordingReviewUnsupportedFile'
};

function valueOrNone(value) {
  return (Array.isArray(value) ? value.join(', ') : value) || translate('RecordingReviewNone');
}

function EvidenceField({ label, value }) {
  return (
    <div>
      <dt>{translate(label)}</dt>
      <dd>{valueOrNone(value)}</dd>
    </div>
  );
}

EvidenceField.propTypes = {
  label: PropTypes.string.isRequired,
  value: PropTypes.oneOfType([PropTypes.string, PropTypes.arrayOf(PropTypes.string)])
};

function RecordingTarget({ target }) {
  return (
    <dl className={styles.evidence}>
      <EvidenceField label="Title" value={target.title} />
      <EvidenceField label="Author" value={target.author} />
      <EvidenceField label="ASIN" value={target.asin} />
      <EvidenceField label="RecordingReviewMarketplace" value={target.region?.toUpperCase()} />
      <EvidenceField label="RecordingReviewIdentity" value={target.recordingId} />
      <EvidenceField label="RecordingReviewNarrators" value={target.narrators} />
    </dl>
  );
}

RecordingTarget.propTypes = {
  target: PropTypes.object.isRequired
};

function RecordingEvidence({ review }) {
  return (
    <>
      <h3>{translate('RecordingReviewEvidence')}</h3>
      <dl className={styles.evidence}>
        <EvidenceField label="Filename" value={review.fileName} />
        <EvidenceField label="RecordingReviewFilenameAsins" value={review.filenameAsins} />
        <EvidenceField label="RecordingReviewFolderAsins" value={review.folderAsins} />
        <EvidenceField label="RecordingReviewEmbeddedAsins" value={review.embeddedAsins} />
        <EvidenceField label="RecordingReviewEffectiveAsin" value={review.effectiveAsin} />
        <EvidenceField label="RecordingReviewFilenamePreference" value={translate(review.preferPathAsin ? 'Enabled' : 'Disabled')} />
        <EvidenceField label="RecordingReviewTaggedTitle" value={review.tagTitle} />
        <EvidenceField label="RecordingReviewTaggedAuthors" value={review.tagAuthors} />
        <EvidenceField label="RecordingReviewTaggedNarrators" value={review.tagNarrators} />
      </dl>
      {review.malformedEmbeddedAsin && <p>{translate('RecordingReviewMalformedEmbedded')}</p>}
      <h3>{translate('RecordingReviewCurrent')}</h3>
      {review.currentRecording ? <RecordingTarget target={review.currentRecording} /> : <p>{translate('RecordingReviewUnmapped')}</p>}
      <h3>{translate('RecordingReviewCatalogMatches')}</h3>
      {review.candidates.length === 0 && <p>{translate('RecordingReviewNoLocalMatches')}</p>}
    </>
  );
}

RecordingEvidence.propTypes = {
  review: PropTypes.object.isRequired
};

function RecordingReviewModal({ isOpen, fileId, onModalClose, onManualImport, onCreateLocalRecording, onAssociated }) {
  const [review, setReview] = useState(null);
  const [preview, setPreview] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [isApplying, setIsApplying] = useState(false);
  const associationRequest = useRef(null);
  const authors = useSelector((state) => state.authors.items);
  const route = `/bookfile/${fileId}/recordingreview`;

  useEffect(() => {
    if (!isOpen) {
      return undefined;
    }

    setPreview(null);
    setReview(null);
    setError(null);
    setLoading(true);
    const { request, abortRequest } = createAjaxRequest({ url: route, dataType: 'json' });
    let active = true;
    request.done((result) => {
      if (active) {
        setReview(result);
      }
    })
      .fail((xhr) => {
        if (active && !xhr.aborted) {
          setError(getErrorMessage(xhr, translate('RecordingReviewLoadFailed')));
        }
      })
      .always(() => {
        if (active) {
          setLoading(false);
        }
      });
    return () => {
      active = false;
      abortRequest();
    };
  }, [attempt, isOpen, route]);

  useEffect(() => () => {
    const association = associationRequest.current;
    associationRequest.current = null;
    association?.abortRequest();
  }, []);

  const associate = () => {
    if (!preview || associationRequest.current) {
      return;
    }

    setIsApplying(true);
    const association = createAjaxRequest({
      url: route,
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify({ editionId: preview.editionId, revision: preview.revision })
    });
    associationRequest.current = association;

    association.request.done((result) => {
      if (associationRequest.current !== association) {
        return;
      }

      setReview(result);
      setPreview(null);
      onAssociated?.(result);
    }).fail((xhr) => {
      if (associationRequest.current === association && !xhr.aborted) {
        setPreview(null);
        setReview(null);
        setError(getErrorMessage(xhr, translate('RecordingReviewSaveFailed')));
      }
    }).always(() => {
      if (associationRequest.current === association) {
        associationRequest.current = null;
        setIsApplying(false);
      }
    });
  };

  const asins = review ? [...new Set([
    review.effectiveAsin, ...review.filenameAsins, ...review.folderAsins, ...review.embeddedAsins
  ].filter(Boolean))] : [];
  const needsLookup = review && ['catalogMissing', 'identifierConflict', 'ambiguousCatalogMatch'].includes(review.reason);
  const canCreateLocal = review && asins.length === 0 && ['insufficientEvidence', 'authorMissing'].includes(review.reason);
  const correctionAuthorIds = new Set(review?.candidates.map((candidate) => candidate.authorId) ?? []);
  if (review?.currentRecording) {
    correctionAuthorIds.add(review.currentRecording.authorId);
  }
  const correctionAuthor = authors.find((author) =>
    correctionAuthorIds.has(author.id) && /^audible:[a-z]+:name:/.test(author.foreignAuthorId) && author.titleSlug);

  const close = () => {
    if (!associationRequest.current) {
      setPreview(null);
      onModalClose();
    }
  };

  return (
    <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={close}>
      <ModalContent onModalClose={close} showCloseButton={!isApplying}>
        <ModalHeader>{translate('RecordingReviewHeading')}</ModalHeader>
        <ModalBody>
          {loading && <LoadingIndicator />}
          {error && <Alert kind={kinds.DANGER} role="alert">{error}</Alert>}
          {
            review && !preview &&
              <>
                <Alert kind={kinds.INFO}>{translate(reviewReasons[review.reason] || 'RecordingReviewInsufficientEvidence')}</Alert>
                <RecordingEvidence review={review} />
                {review.candidates.map((target) => (
                  <section className={styles.target} key={target.editionId}>
                    <RecordingTarget target={target} />
                    {
                      target.revision &&
                        <Button onPress={() => setPreview(target)}>
                          {translate('RecordingReviewPreviewAssociation')}
                        </Button>
                    }
                  </section>
                ))}
                <div className={styles.actions}>
                  {
                    needsLookup && asins.slice(0, 4).map((asin) => (
                      <Button key={asin} to={`/add/search?term=${encodeURIComponent(`asin:${asin}`)}`}>
                        {translate('RecordingReviewExactLookup', { asin })}
                      </Button>
                    ))
                  }
                  {
                    !review.currentRecording && onManualImport &&
                      <Button onPress={onManualImport}>{translate('ManualImport')}</Button>
                  }
                  {
                    canCreateLocal && onCreateLocalRecording &&
                      <Button onPress={() => onCreateLocalRecording(fileId)}>{translate('RecordingReviewCreateLocal')}</Button>
                  }
                  {
                    correctionAuthor &&
                      <Button to={`/author/${encodeURIComponent(correctionAuthor.titleSlug)}`}>
                        {translate('RecordingReviewAuthorCorrection')}
                      </Button>
                  }
                </div>
              </>
          }
          {
            review && preview &&
              <>
                <h3>{translate('RecordingReviewAssociationHeading')}</h3>
                <p>{translate('RecordingReviewAssociationSummary', { fileName: review.fileName })}</p>
                <RecordingTarget target={preview} />
              </>
          }
        </ModalBody>
        <ModalFooter>
          {
            error && !review && !loading &&
              <Button onPress={() => setAttempt(attempt + 1)}>{translate('RecordingReviewRefresh')}</Button>
          }
          {
            preview &&
              <Button isDisabled={isApplying} onPress={() => setPreview(null)}>
                {translate('RecordingReviewBack')}
              </Button>
          }
          <Button isDisabled={isApplying} onPress={close}>{translate('Close')}</Button>
          {
            preview &&
              <SpinnerButton kind={kinds.PRIMARY} isSpinning={isApplying} onPress={associate}>
                {translate('RecordingReviewAssociate')}
              </SpinnerButton>
          }
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

RecordingReviewModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  fileId: PropTypes.number.isRequired,
  onModalClose: PropTypes.func.isRequired,
  onManualImport: PropTypes.func,
  onCreateLocalRecording: PropTypes.func,
  onAssociated: PropTypes.func
};

export default RecordingReviewModal;
