import { useMutation, useQuery } from '@tanstack/react-query';
import React, { useState } from 'react';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import fetchJson from 'Utilities/Fetch/fetchJson';
import styles from './ImportListCleanup.css';

function parseTags(value) {
  if (!Array.isArray(value) || value.some((tag) => !tag || !Number.isInteger(tag.id) || typeof tag.label !== 'string')) {
    throw new Error('Invalid tags response');
  }
  return value;
}

function parsePreview(value) {
  if (!value || !['preview', 'unmonitor', 'tag'].includes(value.mode) ||
    !Array.isArray(value.blockers) || value.blockers.some((blocker) => typeof blocker !== 'string') ||
    !Array.isArray(value.candidates) ||
    value.candidates.some((candidate) => !candidate || !Number.isInteger(candidate.bookId) ||
      !Number.isInteger(candidate.authorId) || ['recordingId', 'title', 'authorName'].some((field) => typeof candidate[field] !== 'string') ||
      typeof candidate.monitored !== 'boolean') ||
    (value.tagId != null && !Number.isInteger(value.tagId)) ||
    (value.confirmationToken != null && typeof value.confirmationToken !== 'string')) {
    throw new Error('Invalid cleanup preview');
  }
  return value;
}

function ImportListCleanup() {
  const [isOpen, setIsOpen] = useState(false);
  const [mode, setMode] = useState('preview');
  const [tagId, setTagId] = useState('');
  const [preview, setPreview] = useState(null);
  const [receipt, setReceipt] = useState(null);
  const tags = useQuery({
    queryKey: ['importlistcleanup', 'tags', window.Readarr.apiRoot],
    queryFn: ({ signal }) => fetchJson({ path: '/tag', parse: parseTags, signal }),
    enabled: isOpen && mode === 'tag'
  });
  const getPreview = useMutation({
    mutationFn: () => fetchJson({
      path: `/importlistcleanup?mode=${mode}${mode === 'tag' ? `&tagId=${encodeURIComponent(tagId)}` : ''}`,
      parse: parsePreview
    }),
    onSuccess: (data) => setPreview(data)
  });
  const apply = useMutation({
    mutationFn: (confirmed) => fetchJson({
      path: '/importlistcleanup',
      method: 'POST',
      body: { mode: confirmed.mode, tagId: confirmed.tagId, confirmationToken: confirmed.confirmationToken },
      parse: parsePreview
    }),
    onSuccess: (data) => {
      setReceipt(data);
      setPreview(null);
    },
    onError: () => setPreview(null)
  });

  const busy = getPreview.isPending || apply.isPending;
  const clearPreview = () => {
    setPreview(null);
    setReceipt(null);
    getPreview.reset();
    apply.reset();
  };

  return (
    <FieldSet legend="Import list cleanup">
      <details onToggle={(event) => setIsOpen(event.currentTarget.open)}>
        <summary>Review former list-managed recordings</summary>
        <p>Automatic cleanup is disabled. Only recordings added directly by import lists since management tracking began can qualify; manually added and local recordings are not candidates. Files are never removed. All enabled lists must have successful, complete, resolved membership snapshots from the last 24 hours.</p>
        <div className={styles.controls}>
          <label>
            Action
            <select
              value={mode}
              disabled={busy}
              onChange={(event) => {
                setMode(event.target.value);
                clearPreview();
              }}
            >
              <option value="preview">Preview only</option>
              <option value="unmonitor">Unmonitor recordings</option>
              <option value="tag">Tag affected authors</option>
            </select>
          </label>
          {mode === 'tag' &&
            <label>
              Author tag
              <select
                value={tagId}
                disabled={busy || tags.isPending}
                onChange={(event) => {
                  setTagId(event.target.value);
                  clearPreview();
                }}
              >
                <option value="">Select a tag</option>
                {(tags.data || []).map((tag) => <option key={tag.id} value={tag.id}>{tag.label}</option>)}
              </select>
            </label>
          }
          <Button
            isDisabled={busy || (mode === 'tag' && !tagId)}
            onPress={() => {
              clearPreview();
              getPreview.mutate();
            }}
          >
            Generate preview
          </Button>
        </div>
        {mode === 'tag' && <p>Tags are author-level in Bookshelf: tagging an affected author also applies to their other recordings. Recording monitoring and files are unchanged.</p>}
        {tags.isError && mode === 'tag' && <p role="alert">Unable to load tags.</p>}
        {getPreview.isPending && <p role="status">Generating preview…</p>}
        {getPreview.isError && <p role="alert">Unable to generate preview.</p>}
        {apply.isError && <p role="alert">Unable to apply cleanup. Generate a new preview; its evidence may have changed.</p>}
        {receipt && <p role="status">Applied to {receipt.mode === 'tag' ? `${new Set(receipt.candidates.map((item) => item.authorId)).size} authors` : `${receipt.candidates.length} recordings`}. No files were removed.</p>}
        {preview &&
          <div>
            {preview.blockers.length > 0
              ? <div role="status"><p>Cleanup unavailable:</p><ul>{preview.blockers.map((blocker, index) => <li key={index}>{blocker}</li>)}</ul></div>
              : <p>{preview.candidates.length} recording(s) eligible.</p>}
            {preview.candidates.length > 0 &&
              <ul className={styles.candidates}>
                {preview.candidates.map((candidate) => (
                  <li key={candidate.bookId}>
                    {candidate.title} — {candidate.authorName} ({candidate.recordingId}) · {candidate.monitored ? 'Monitored' : 'Unmonitored'}
                  </li>
                ))}
              </ul>
            }
            {mode === 'tag' && preview.candidates.length > 0 &&
              <p>{new Set(preview.candidates.map((candidate) => candidate.authorId)).size} distinct author(s) would receive the selected tag.</p>}
            {preview.confirmationToken && mode !== 'preview' &&
              <Button isDisabled={busy} onPress={() => apply.mutate(preview)}>
                {mode === 'unmonitor' ? 'Confirm unmonitoring' : 'Confirm tagging authors'}
              </Button>
            }
          </div>
        }
      </details>
    </FieldSet>
  );
}

export default ImportListCleanup;
