import { queryOptions } from '@tanstack/react-query';
import { type BookId, type EditionId, parseResourceId,type WorkId } from 'Book/BookIdentity';
import { isRecord } from 'Book/LibraryModels';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';

export interface WorkDescription {
  title: string;
  author: string;
  series: string;
  position: string;
}

export interface Work extends WorkDescription {
  id: WorkId;
}

export interface Recording {
  editionId: EditionId;
  bookId: BookId;
  foreignEditionId: string | null;
  asin: string | null;
  audibleRegion: string | null;
  title: string;
  author: string;
  bookSlug: string;
  monitored: boolean;
  fileCount: number;
  sizeOnDisk: number;
}

interface WorkReference {
  recording: Recording;
  confirmed: boolean;
}

interface WorkMembership {
  work: Work;
  confirmed: boolean;
  hasConfirmedFiles: boolean;
  recordings: WorkReference[];
}

export interface WorkSnapshot {
  recording: Recording;
  revision: string;
  unmatchedMembershipCount: number;
  memberships: WorkMembership[];
  coveredWorkCount: number;
  relatedRecordingCount: number;
  relatedFileCount: number;
  relatedSizeOnDisk: number;
}

export type WorkEditMember = { confirmed: boolean } & (
  { workId: WorkId; newWork?: never } | { newWork: WorkDescription; workId?: never }
);

export const workQueryFamily = () => ['recordingwork', window.Readarr.apiRoot];

function record(value: unknown): Record<string, unknown> {
  if (!isRecord(value)) {
    throw new Error('Invalid work coverage response');
  }
  return value;
}

function number(value: unknown, minimum = 0): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < minimum || !Number.isSafeInteger(value)) {
    throw new Error('Invalid work coverage number');
  }
  return value;
}

function string(value: unknown): string {
  if (typeof value !== 'string') {
    throw new Error('Invalid work coverage text');
  }
  return value;
}

function boolean(value: unknown): boolean {
  if (typeof value !== 'boolean') {
    throw new Error('Invalid membership confirmation');
  }
  return value;
}

function array<T>(value: unknown, item: (value: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw new Error('Invalid work coverage list');
  }
  return value.map(item);
}

function parseWork(value: unknown): Work {
  const item = record(value);
  return {
    id: parseResourceId(item.id, 'work'),
    title: string(item.title),
    author: string(item.author),
    series: string(item.series),
    position: string(item.position)
  };
}

function parseRecording(value: unknown): Recording {
  const item = record(value);
  return {
    editionId: parseResourceId(item.editionId, 'edition'),
    bookId: parseResourceId(item.bookId, 'book'),
    foreignEditionId: item.foreignEditionId == null ? null : string(item.foreignEditionId),
    asin: item.asin == null ? null : string(item.asin),
    audibleRegion: item.audibleRegion == null ? null : string(item.audibleRegion),
    title: string(item.title),
    author: string(item.author),
    bookSlug: string(item.bookSlug),
    monitored: boolean(item.monitored),
    fileCount: number(item.fileCount),
    sizeOnDisk: number(item.sizeOnDisk)
  };
}

function parseReference(value: unknown): WorkReference {
  const item = record(value);
  return { recording: parseRecording(item.recording), confirmed: boolean(item.confirmed) };
}

function parseMembership(value: unknown): WorkMembership {
  const item = record(value);
  return {
    work: parseWork(item.work),
    confirmed: boolean(item.confirmed),
    hasConfirmedFiles: boolean(item.hasConfirmedFiles),
    recordings: array(item.recordings, parseReference)
  };
}

function parseSnapshot(value: unknown): WorkSnapshot {
  const item = record(value);
  return {
    recording: parseRecording(item.recording),
    revision: string(item.revision),
    memberships: array(item.memberships, parseMembership),
    unmatchedMembershipCount: number(item.unmatchedMembershipCount),
    coveredWorkCount: number(item.coveredWorkCount),
    relatedRecordingCount: number(item.relatedRecordingCount),
    relatedFileCount: number(item.relatedFileCount),
    relatedSizeOnDisk: number(item.relatedSizeOnDisk)
  };
}

function parseExpectedSnapshot(value: unknown, editionId: EditionId): WorkSnapshot {
  const snapshot = parseSnapshot(value);
  if (snapshot.recording.editionId !== editionId) {
    throw new Error('Work coverage belongs to a different recording');
  }
  return snapshot;
}

export function recordingsForBook(bookId: BookId) {
  parseResourceId(bookId, 'book');
  return queryOptions({
    queryKey: [...workQueryFamily(), 'book', bookId],
    queryFn: ({ signal }) => fetchJson({
      path: `/recordingwork/book/${bookId}`,
      parse: (value) => {
        const recordings = array(value, parseRecording);
        if (recordings.some((recording) => recording.bookId !== bookId)) {
          throw new Error('Recording belongs to a different book');
        }
        return recordings;
      },
      signal
    })
  });
}

export function recordingCoverage(editionId: EditionId) {
  parseResourceId(editionId, 'edition');
  return queryOptions({
    queryKey: [...workQueryFamily(), 'recording', editionId],
    queryFn: ({ signal }) => fetchJson({
      path: `/recordingwork/${editionId}`,
      parse: (value) => parseExpectedSnapshot(value, editionId),
      signal
    })
  });
}

export function worksSearch(term: string) {
  return queryOptions({
    queryKey: [...workQueryFamily(), 'works', term],
    queryFn: ({ signal }) => fetchJson({
      path: `/recordingwork/works?${new URLSearchParams({ term })}`,
      parse: (value) => array(value, parseWork),
      signal
    })
  });
}

export function saveWorkCoverage(editionId: EditionId, revision: string, memberships: WorkEditMember[]) {
  parseResourceId(editionId, 'edition');
  return fetchJson({
    path: `/recordingwork/${editionId}`,
    method: 'PUT',
    body: { revision, memberships },
    parse: (value) => parseExpectedSnapshot(value, editionId)
  });
}

export function workCoverageError(error: unknown): string {
  if (error instanceof ApiError && error.details.kind === 'http') {
    if (error.details.status === 409) {
      return 'Work coverage changed in another session. Close the editor, refresh, and review the recording again.';
    }
    const body = error.details.body;
    if (body.format === 'json' && isRecord(body.value)) {
      const message = body.value.message;
      if (typeof message === 'string') {
        return message;
      }
    }
  }

  return 'Could not load or save work coverage. Please try again.';
}
