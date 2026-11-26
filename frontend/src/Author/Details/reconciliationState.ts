import { asStateRecord } from 'Store/StateTypes';

function integer(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) {
    throw new TypeError(`Invalid reconciliation ${field}`);
  }
  return value;
}

function text(value: unknown, field: string): string {
  if (typeof value !== 'string') {
    throw new TypeError(`Invalid reconciliation ${field}`);
  }
  return value;
}

function optionalText(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function flags(value: unknown, field: string): boolean {
  if (typeof value !== 'boolean') {
    throw new TypeError(`Invalid reconciliation ${field}`);
  }
  return value;
}

function list<T>(value: unknown, field: string, parse: (item: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`Invalid reconciliation ${field}`);
  }
  return value.map(parse);
}

function candidate(value: unknown) {
  const item = asStateRecord(value);
  return {
    id: integer(item.id, 'author ID'),
    name: text(item.name, 'author name'),
    foreignAuthorId: text(item.foreignAuthorId, 'author identity')
  };
}

export function parseCandidates(value: unknown) {
  return list(value, 'candidates', candidate);
}

export function parseProfile(value: unknown) {
  const item = asStateRecord(value);
  return {
    ...candidate(item),
    titleSlug: text(item.titleSlug, 'slug'),
    qualityProfileId: integer(item.qualityProfileId, 'quality profile'),
    metadataProfileId: integer(item.metadataProfileId, 'metadata profile'),
    monitored: flags(item.monitored, 'monitored'),
    monitorNewItems: typeof item.monitorNewItems === 'number' ? item.monitorNewItems : text(item.monitorNewItems, 'monitor new items'),
    tags: list(item.tags ?? [], 'tags', (tag) => integer(tag, 'tag'))
  };
}

export type ReconciliationProfile = ReturnType<typeof parseProfile>;

export function parseReconciliationPreview(value: unknown) {
  const item = asStateRecord(value);
  const source = parseProfile(item.source);
  const target = parseProfile(item.target);
  const previewToken = text(item.previewToken, 'preview token');
  if (!previewToken || !source.id || !target.id || !target.titleSlug || source.id === target.id) {
    throw new TypeError('Invalid reviewed reconciliation identity');
  }

  return {
    source,
    target,
    previewToken,
    canApply: flags(item.canApply, 'apply status'),
    existingOverrideId: item.existingOverrideId == null ? null : integer(item.existingOverrideId, 'override'),
    recordings: list(item.recordings, 'recordings', (value) => {
      const recording = asStateRecord(value);
      return {
        bookId: integer(recording.bookId, 'book ID'),
        editionId: integer(recording.editionId, 'edition ID'),
        title: text(recording.title, 'recording title'),
        foreignEditionId: optionalText(recording.foreignEditionId),
        foreignBookId: optionalText(recording.foreignBookId)
      };
    }),
    files: list(item.files, 'files', (value) => {
      const file = asStateRecord(value);
      return { id: integer(file.id, 'file ID'), path: text(file.path, 'file path') };
    }),
    references: list(item.references, 'references', (value) => {
      const reference = asStateRecord(value);
      return { table: text(reference.table, 'reference table'), count: integer(reference.count, 'reference count') };
    }),
    blockers: list(item.blockers, 'blockers', (value) => text(value, 'blocker')),
    settingsDifferences: list(item.settingsDifferences, 'settings differences', (value) => text(value, 'setting'))
  };
}

export type ReconciliationPreviewData = ReturnType<typeof parseReconciliationPreview>;
