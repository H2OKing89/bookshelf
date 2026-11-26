import PropTypes from 'prop-types';
import { useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';

function SubmissionHolds({ isRefreshing }) {
  const [holds, setHolds] = useState([]);
  const [selected, setSelected] = useState(null);
  const [error, setError] = useState(null);
  const [isSaving, setIsSaving] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    if (isRefreshing) {
      return undefined;
    }

    let active = true;
    const { request, abortRequest } = createAjaxRequest({ url: '/downloadsubmission' });
    request.done((data) => {
      if (active) {
        setHolds(data);
        setError(null);
      }
    }).fail((xhr) => {
      if (active && !xhr.aborted) {
        setError('Unable to load submission holds. Refresh the queue to try again.');
      }
    });

    return () => {
      active = false;
      abortRequest();
    };
  }, [isRefreshing, revision]);

  const acknowledge = () => {
    if (isSaving || !selected) {
      return;
    }

    setIsSaving(true);
    const { request } = createAjaxRequest({
      method: 'POST',
      url: '/downloadsubmission/acknowledge',
      contentType: 'application/json',
      data: JSON.stringify({
        attemptId: selected.attemptId,
        downloadClientId: selected.downloadClientId,
        clientReviewed: true
      })
    });
    request.done(() => {
      setSelected(null);
      setRevision((value) => value + 1);
    }).fail(() => {
      setError('The hold could not be cleared. Refresh the queue and check whether the submission is still in progress.');
    }).always(() => setIsSaving(false));
  };

  return (
    <div>
      {error ? <Alert kind={kinds.DANGER}>{error}</Alert> : null}
      {holds.map((hold) => (
        <Alert key={hold.attemptId} kind={kinds.WARNING}>
          <strong>Submission needs review: {hold.title}</strong>
          <p>
            {hold.downloadClientName} (client {hold.downloadClientId}) may have accepted this download.
            Automatic grabs for these books are held to prevent duplicates. Check that client for this release before clearing the hold.
          </p>
          {hold.expectedDownloadId ? <p>Download ID: {hold.expectedDownloadId}</p> : null}
          <Button isDisabled={!hold.canAcknowledge} onPress={() => setSelected(hold)}>
            {hold.canAcknowledge ? 'Review submission' : 'Submission in progress'}
          </Button>
        </Alert>
      ))}
      <ConfirmModal
        isOpen={!!selected}
        title="Clear submission hold?"
        message={`Confirm you checked ${selected?.downloadClientName || 'the download client'} for this release. Clearing the hold allows future grabs. It does not remove or restart any download.`}
        confirmLabel="I checked the client; clear hold"
        isSpinning={isSaving}
        onConfirm={acknowledge}
        onCancel={() => { if (!isSaving) { setSelected(null); } }}
      />
    </div>
  );
}

SubmissionHolds.propTypes = {
  isRefreshing: PropTypes.bool.isRequired
};

export default SubmissionHolds;
