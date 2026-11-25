import { queryOptions } from '@tanstack/react-query';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';

export interface WorkDescription {
  title: string;
  author: string;
  series: string;
  position: string;
}

export interface Work extends WorkDescription {
  id: number;
}

export interface Recording {
  editionId: number;
  bookId: number;
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
  { workId: number; newWork?: never } | { newWork: WorkDescription; workId?: never }
);

export const workQueryFamily = () => ['recordingwork', window.Readarr.apiRoot];

function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('Invalid work coverage response');
  }
  return value as Record<string, unknown>;
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
    id: number(item.id, 1),
    title: string(item.title),
    author: string(item.author),
    series: string(item.series),
    position: string(item.position)
  };
}

function parseRecording(value: unknown): Recording {
  const item = record(value);
  return {
    editionId: number(item.editionId, 1),
    bookId: number(item.bookId, 1),
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

export function recordingsForBook(bookId: number) {
  return queryOptions({
    queryKey: [...workQueryFamily(), 'book', bookId],
    queryFn: ({ signal }) => fetchJson({
      path: `/recordingwork/book/${bookId}`,
      parse: (value) => array(value, parseRecording),
      signal
    })
  });
}

export function recordingCoverage(editionId: number) {
  return queryOptions({
    queryKey: [...workQueryFamily(), 'recording', editionId],
    queryFn: ({ signal }) => fetchJson({
      path: `/recordingwork/${editionId}`,
      parse: parseSnapshot,
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

export function saveWorkCoverage(editionId: number, revision: string, memberships: WorkEditMember[]) {
  return fetchJson({
    path: `/recordingwork/${editionId}`,
    method: 'PUT',
    body: { revision, memberships },
    parse: parseSnapshot
  });
}

export function workCoverageError(error: unknown): string {
  if (error instanceof ApiError && error.details.kind === 'http') {
    if (error.details.status === 409) {
      return 'Work coverage changed in another session. Close the editor, refresh, and review the recording again.';
    }
    const body = error.details.body;
    if (body.format === 'json' && body.value && typeof body.value === 'object') {
      const message = (body.value as { message?: unknown }).message;
      if (typeof message === 'string') {
        return message;
      }
    }
  }

  return 'Could not load or save work coverage. Please try again.';
}
