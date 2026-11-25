import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useRef, useState } from 'react';
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
import PendingChangesModal from 'Settings/PendingChangesModal';
import useUnsavedNavigation from 'Settings/useUnsavedNavigation';
import { recordingCoverage, saveWorkCoverage, workCoverageError, workQueryFamily, worksSearch } from './recordingWorkQuery';
import styles from './RecordingWorkCoverage.css';

function describe(work) {
  return [work.title, work.author, [work.series, work.position].filter(Boolean).join(' #')].filter(Boolean).join(' · ');
}

function editMembers(snapshot) {
  return snapshot.memberships.map(({ work, confirmed }) => ({
    key: `work-${work.id}`,
    work,
    confirmed
  }));
}

function requestMembers(members) {
  return members.map(({ work, confirmed, newWork }) => (work ?
    { workId: work.id, confirmed } :
    { newWork, confirmed }));
}

function RecordingWorkEditor({ snapshot, onClose, onSaved }) {
  const client = useQueryClient();
  // Freeze the exact identity and revision reviewed when opening this editor.
  const [original] = useState(snapshot);
  const [members, setMembers] = useState(() => editMembers(original));
  const [term, setTerm] = useState('');
  const [search, setSearch] = useState(null);
  const [confirmClose, setConfirmClose] = useState(false);
  const nextKey = useRef(0);
  const dirty = JSON.stringify(requestMembers(members)) !== JSON.stringify(requestMembers(editMembers(original)));
  const valid = members.every((member) => member.work || member.newWork.title.trim());
  const results = useQuery({ ...worksSearch(search ?? ''), enabled: search !== null });
  const mutation = useMutation({
    mutationFn: () => saveWorkCoverage(original.recording.editionId, original.revision, requestMembers(members)),
    onSuccess(response) {
      client.setQueryData(recordingCoverage(response.recording.editionId).queryKey, response);
      client.invalidateQueries({ queryKey: workQueryFamily() });
      onSaved();
    }
  });

  useUnsavedNavigation(dirty, onClose);

  const requestClose = () => {
    if (mutation.isPending) {
      return;
    }
    if (dirty) {
      setConfirmClose(true);
    } else {
      onClose();
    }
  };

  function update(key, change) {
    setMembers((current) => current.map((member) => (member.key === key ? { ...member, ...change } : member)));
  }

  const addNew = () => {
    const key = `new-${nextKey.current++}`;
    setMembers((current) => [...current, {
      key,
      newWork: { title: '', author: original.recording.author, series: '', position: '' },
      confirmed: false
    }]);
  };

  function addExisting(work) {
    setMembers((current) => [...current, { key: `work-${work.id}`, work, confirmed: false }]);
  }

  return (
    <>
      <Modal isOpen={true} size={sizes.LARGE} onModalClose={requestClose}>
        <ModalContent onModalClose={requestClose} showCloseButton={!mutation.isPending}>
          <ModalHeader>Edit work coverage</ModalHeader>
          <ModalBody>
            <p>{original.recording.title} · {original.recording.foreignEditionId} · Edition {original.recording.editionId}</p>
            <p>Confirm only known members of this recording. Uncertain ranges and side stories may be recorded without counting them as confirmed coverage.</p>
            {original.unmatchedMembershipCount > 0 &&
              <Alert kind={kinds.WARNING}>Saving this review replaces previous memberships that no longer match the recording identity.</Alert>
            }
            {mutation.isError && <Alert kind={kinds.DANGER} role="alert">{workCoverageError(mutation.error)}</Alert>}
            <fieldset disabled={mutation.isPending} className={styles.editorFields}>
              <legend>Work memberships</legend>
              {members.map((member, index) => (
                <section className={styles.work} key={member.key} aria-label={`Work membership ${index + 1}`}>
                  {member.work ? <h3>{describe(member.work)}</h3> :
                    <div className={styles.fields}>
                      {[['title', 'Work title', 512], ['author', 'Work author', 512], ['series', 'Work series', 512], ['position', 'Volume / position', 128]].map(([field, label, max]) => (
                        <label key={field} className={styles.field}>
                          {label}
                          <input
                            className={styles.input}
                            maxLength={max}
                            value={member.newWork[field]}
                            onChange={(event) => update(member.key, { newWork: { ...member.newWork, [field]: event.target.value } })}
                          />
                        </label>
                      ))}
                    </div>
                  }
                  <label className={styles.field}>
                    Membership
                    <select className={styles.input} value={member.confirmed ? 'confirmed' : 'uncertain'}
                      onChange={(event) => update(member.key, { confirmed: event.target.value === 'confirmed' })}
                    >
                      <option value="uncertain">Uncertain</option>
                      <option value="confirmed">Confirmed</option>
                    </select>
                  </label>
                  <Button onPress={() => setMembers((current) => current.filter((item) => item.key !== member.key))}>Remove membership</Button>
                </section>
              ))}
              <Button onPress={addNew}>Add new work</Button>
              <section className={styles.work}>
                <h3>Link an existing work</h3>
                <label className={styles.field}>
                  Find works by title, author, series or position
                  <input className={styles.input} value={term} maxLength={512} onChange={(event) => setTerm(event.target.value)} />
                </label>
                <Button onPress={() => {
                  if (search === term) {
                    results.refetch();
                  } else {
                    setSearch(term);
                  }
                }}
                >Find works</Button>
                {search !== null && results.isFetching && <LoadingIndicator />}
                {search !== null && results.isError && <Alert kind={kinds.DANGER} role="alert">{workCoverageError(results.error)}</Alert>}
                {search !== null && results.isSuccess &&
                  <>
                    {!results.data.length && <p>No existing works match.</p>}
                    {results.data.length === 50 && <p>Showing the first 50 matches. Refine your search to see others.</p>}
                    <ul className={styles.matches}>
                      {results.data.map((work) => (
                        <li key={work.id}>
                          {describe(work)} (Work {work.id})
                          <Button isDisabled={members.some((member) => member.work?.id === work.id)} onPress={() => addExisting(work)}>Link work</Button>
                        </li>
                      ))}
                    </ul>
                  </>
                }
              </section>
            </fieldset>
          </ModalBody>
          <ModalFooter>
            <Button isDisabled={mutation.isPending} onPress={requestClose}>Close</Button>
            <SpinnerButton
              kind={kinds.PRIMARY}
              isDisabled={(!dirty && !original.unmatchedMembershipCount) || !valid || mutation.isPending}
              isSpinning={mutation.isPending}
              onPress={() => mutation.mutate()}
            >Save coverage</SpinnerButton>
          </ModalFooter>
        </ModalContent>
      </Modal>
      <PendingChangesModal isOpen={confirmClose} isClosingEditor={true} onCancel={() => setConfirmClose(false)} onConfirm={onClose} />
    </>
  );
}

RecordingWorkEditor.propTypes = {
  snapshot: PropTypes.object.isRequired,
  onClose: PropTypes.func.isRequired,
  onSaved: PropTypes.func.isRequired
};

export default RecordingWorkEditor;
