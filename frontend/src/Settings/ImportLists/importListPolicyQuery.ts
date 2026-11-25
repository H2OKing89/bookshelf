import type { BookId, MarketplaceRecordingId } from 'Book/BookIdentity';
import { parseRecordingIdentity } from 'Utilities/Book/recordingIdentity';

// IDs returned by the provider are deliberately opaque. They are not local
// numeric library identities, and an ASIN without a marketplace is not a
// recording identity. The membership API exposes these fields independently.
export interface MembershipItem {
  sourceAuthor: string | null;
  sourceTitle: string | null;
  sourceAuthorId: string | null;
  sourceBookId: string | null;
  sourceEditionId: string | null;
  authorId: string | null;
  bookId: string | null;
  editionId: string | null;
  excluded: boolean;
}

export interface MembershipSnapshot {
  importListId: number;
  isCurrent: boolean;
  lastSuccessfulSync: string | null;
  items: MembershipItem[];
}

export type CleanupMode = 'preview' | 'unmonitor' | 'tag';
export type CleanupSelection =
  | { mode: 'preview' | 'unmonitor'; tagId: null }
  | { mode: 'tag'; tagId: number };
export type CleanupAction =
  | { mode: 'unmonitor'; tagId: null; confirmationToken: string }
  | { mode: 'tag'; tagId: number; confirmationToken: string };

export interface CleanupCandidate {
  bookId: BookId;
  recordingId: MarketplaceRecordingId;
  title: string;
  authorId: number;
  authorName: string;
  monitored: boolean;
}

export type CleanupPreview = CleanupSelection & {
  blockers: string[];
  candidates: CleanupCandidate[];
  confirmationToken: string | null;
};

export interface CleanupTag {
  id: number;
  label: string;
}

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('Invalid list response');
  }
  return value as Record<string, unknown>;
}

function id(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) {
    throw new Error('Invalid list resource ID');
  }
  return value;
}

function text(value: unknown): string {
  if (typeof value !== 'string') {
    throw new Error('Invalid list text');
  }
  return value;
}

function optionalText(value: unknown): string | null {
  return value == null ? null : text(value);
}

function flag(value: unknown): boolean {
  if (typeof value !== 'boolean') {
    throw new Error('Invalid list flag');
  }
  return value;
}

function array<T>(value: unknown, parse: (item: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw new Error('Invalid list items');
  }
  return value.map(parse);
}

function parseMember(value: unknown): MembershipItem {
  const item = record(value);
  return {
    sourceAuthor: optionalText(item.sourceAuthor),
    sourceTitle: optionalText(item.sourceTitle),
    sourceAuthorId: optionalText(item.sourceAuthorId),
    sourceBookId: optionalText(item.sourceBookId),
    sourceEditionId: optionalText(item.sourceEditionId),
    authorId: optionalText(item.authorId),
    bookId: optionalText(item.bookId),
    editionId: optionalText(item.editionId),
    excluded: flag(item.excluded)
  };
}

export function parseMembershipSnapshot(value: unknown, expectedListId: number): MembershipSnapshot {
  const snapshot = record(value);
  const importListId = id(snapshot.importListId);
  const lastSuccessfulSync = optionalText(snapshot.lastSuccessfulSync);
  if (importListId !== id(expectedListId) || (lastSuccessfulSync !== null && !Number.isFinite(Date.parse(lastSuccessfulSync)))) {
    throw new Error('Invalid list membership response');
  }
  return { importListId, lastSuccessfulSync, isCurrent: flag(snapshot.isCurrent), items: array(snapshot.items, parseMember) };
}

export function membershipStatus(item: MembershipItem): 'Excluded' | 'Resolved identity' | 'Unresolved identity' {
  if (item.excluded) {
    return 'Excluded';
  }
  const isBook = item.sourceTitle || item.sourceBookId || item.sourceEditionId;
  const resolved = isBook ? item.bookId && item.editionId === item.bookId && item.authorId : item.authorId;
  return resolved ? 'Resolved identity' : 'Unresolved identity';
}

export function parseCleanupMode(value: unknown): CleanupMode {
  if (value !== 'preview' && value !== 'unmonitor' && value !== 'tag') {
    throw new Error('Invalid cleanup mode');
  }
  return value;
}

export function cleanupSelection(mode: CleanupMode, selectedTag: string): CleanupSelection | null {
  if (mode !== 'tag') {
    return { mode, tagId: null };
  }
  const tagId = Number(selectedTag);
  return /^[1-9]\d*$/.test(selectedTag) && Number.isSafeInteger(tagId) ? { mode, tagId } : null;
}

function parseCandidate(value: unknown): CleanupCandidate {
  const item = record(value);
  const identity = parseRecordingIdentity(item.recordingId);
  // Cleanup only ever targets known marketplace recordings. Never accept a
  // provider string as a local ID or a local recording as a cleanup candidate.
  if (identity?.source !== 'audible') {
    throw new Error('Invalid cleanup recording identity');
  }
  return {
    bookId: id(item.bookId) as BookId,
    recordingId: identity.id,
    title: text(item.title),
    authorId: id(item.authorId),
    authorName: text(item.authorName),
    monitored: flag(item.monitored)
  };
}

export function parseCleanupTags(value: unknown): CleanupTag[] {
  return array(value, (item) => {
    const tag = record(item);
    return { id: id(tag.id), label: text(tag.label) };
  });
}

export function parseCleanupPreview(value: unknown, expected: CleanupSelection): CleanupPreview {
  const preview = record(value);
  const mode = parseCleanupMode(preview.mode);
  const tagId = preview.tagId == null ? null : id(preview.tagId);
  const confirmationToken = optionalText(preview.confirmationToken);
  const blockers = array(preview.blockers, text);
  const candidates = array(preview.candidates, parseCandidate);
  if (mode !== expected.mode || tagId !== expected.tagId ||
      (confirmationToken !== null && (!confirmationToken || mode === 'preview' || blockers.length !== 0 || candidates.length === 0))) {
    throw new Error('Invalid cleanup preview');
  }
  return { ...expected, blockers, candidates, confirmationToken };
}

export function cleanupPreviewPath(selection: CleanupSelection): string {
  const params = new URLSearchParams({ mode: selection.mode });
  if (selection.mode === 'tag') {
    params.set('tagId', String(selection.tagId));
  }
  return `/importlistcleanup?${params.toString()}`;
}

export function cleanupConfirmation(preview: CleanupPreview | null, selection: CleanupSelection | null): CleanupAction | null {
  if (!preview || !selection || preview.mode !== selection.mode || preview.tagId !== selection.tagId ||
      preview.blockers.length || !preview.candidates.length || !preview.confirmationToken) {
    return null;
  }
  if (preview.mode === 'unmonitor') {
    return { mode: 'unmonitor', tagId: null, confirmationToken: preview.confirmationToken };
  }
  if (preview.mode === 'tag') {
    return { mode: 'tag', tagId: preview.tagId, confirmationToken: preview.confirmationToken };
  }
  return null;
}
