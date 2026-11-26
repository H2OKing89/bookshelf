import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { BookId, EditionId } from 'Book/BookIdentity';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import RecordingWorkEditor from './RecordingWorkEditor';
import { type Recording, recordingCoverage, recordingsForBook, workCoverageError,type WorkDescription } from './recordingWorkQuery';
import styles from './RecordingWorkCoverage.css';

function workLabel(work: WorkDescription) {
  return [work.title, work.author, [work.series, work.position].filter(Boolean).join(' #')].filter(Boolean).join(' · ');
}

function recordingLabel(recording: Recording) {
  return [recording.title, recording.foreignEditionId, `Edition ${recording.editionId}`].filter(Boolean).join(' · ');
}

function Coverage({ editionId }: { editionId: EditionId }) {
  const query = useQuery(recordingCoverage(editionId));
  const [editing, setEditing] = useState(false);
  const [saved, setSaved] = useState(false);
  const snapshot = query.data;

  if (!snapshot) {
    return query.isError ?
      <Alert kind={kinds.DANGER} role="alert">{workCoverageError(query.error)}</Alert> :
      <LoadingIndicator />;
  }

  const recording = snapshot.recording;
  return (
    <>
      {query.isError && <Alert kind={kinds.DANGER} role="alert">{workCoverageError(query.error)}</Alert>}
      <div className={styles.controls}>
        <Button isDisabled={query.isFetching || !recording.foreignEditionId} onPress={() => setEditing(true)}>Edit work coverage</Button>
        <Button isDisabled={query.isFetching} onPress={() => query.refetch()}>Refresh coverage</Button>
      </div>
      {saved && <p role="status">Work coverage saved.</p>}
      <p><strong>Exact recording:</strong> {recordingLabel(recording)}</p>
      <p>Files directly associated: {recording.fileCount} · {formatBytes(recording.sizeOnDisk)}</p>
      {snapshot.unmatchedMembershipCount > 0 &&
        <Alert kind={kinds.WARNING}>
          {snapshot.unmatchedMembershipCount} previous memberships no longer match this recording identity. Review and save again to replace them.
        </Alert>
      }
      <p>
        Related confirmed coverage: {snapshot.coveredWorkCount} works with associated files,
        {' '}{snapshot.relatedRecordingCount} distinct recordings, {snapshot.relatedFileCount} distinct files,
        {' '}{formatBytes(snapshot.relatedSizeOnDisk)}.
      </p>
      <p>Associated files indicate coverage, not proof that a recording is complete. Each file is counted once.</p>
      {!snapshot.memberships.length &&
        <p>No reviewed work memberships. Series positions and ranges are not expanded automatically.</p>
      }
      {snapshot.memberships.map((member) => (
        <section key={member.work.id} className={styles.work}>
          <h3>{workLabel(member.work)}</h3>
          <p>
            {member.confirmed ? 'Confirmed membership' : 'Uncertain membership'}
            {' · '}
            {member.hasConfirmedFiles ? 'Confirmed recording with files' : 'No confirmed recording with files'}
          </p>
          <ul>
            {member.recordings.map(({ recording: other, confirmed }) => (
              <li key={other.editionId}>
                {other.bookId === recording.bookId ?
                  recordingLabel(other) :
                  <Link to={`/book/${other.bookSlug}`}>{recordingLabel(other)}</Link>
                }
                {' · '}{confirmed ? 'Confirmed' : 'Uncertain'} · {other.fileCount} files
              </li>
            ))}
          </ul>
        </section>
      ))}
      {editing && <RecordingWorkEditor
        snapshot={snapshot}
        onClose={() => setEditing(false)}
        onSaved={() => {
          setEditing(false);
          setSaved(true);
        }}
      />}
    </>
  );
}

function RecordingWorkCoverage({ bookId }: { bookId: BookId }) {
  const query = useQuery(recordingsForBook(bookId));
  const [selection, setSelection] = useState<EditionId | null>(null);
  const recordings = query.data || [];
  const selected = recordings.find((recording) => recording.editionId === selection) || recordings[0];

  return (
    <section aria-label="Recording work coverage" className={styles.coverage}>
      <h2>Work coverage</h2>
      <p>Link this exact recording to the works it includes. Publisher packs, standalone recordings and regional editions remain separate; monitoring and file ownership do not change.</p>
      {query.isPending && <LoadingIndicator />}
      {query.isError && <Alert kind={kinds.DANGER} role="alert">{workCoverageError(query.error)}</Alert>}
      {query.isSuccess && !recordings.length && <p>No audiobook recordings for this book.</p>}
      {selected &&
        <>
          {recordings.length > 1 &&
            <label className={styles.field}>
              Recording
              <select className={styles.input} value={selected.editionId} onChange={(event) => {
                const recording = recordings.find((item) => String(item.editionId) === event.currentTarget.value);
                setSelection(recording?.editionId ?? null);
              }}>
                {recordings.map((recording) => (
                  <option key={recording.editionId} value={recording.editionId}>{recordingLabel(recording)}</option>
                ))}
              </select>
            </label>
          }
          <Coverage key={selected.editionId} editionId={selected.editionId} />
        </>
      }
    </section>
  );
}

export default RecordingWorkCoverage;
