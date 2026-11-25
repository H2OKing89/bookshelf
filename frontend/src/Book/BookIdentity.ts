declare const resourceKind: unique symbol;

export type ResourceKind = 'book' | 'author' | 'edition' | 'bookFile' | 'work' | 'downloadClient';
type ResourceId<Kind extends ResourceKind> = number & { readonly [resourceKind]: Kind };

export type BookId = ResourceId<'book'>;
export type AuthorId = ResourceId<'author'>;
export type EditionId = ResourceId<'edition'>;
export type BookFileId = ResourceId<'bookFile'>;
export type WorkId = ResourceId<'work'>;
export type DownloadClientId = ResourceId<'downloadClient'>;

function isResourceId<Kind extends ResourceKind>(value: unknown): value is ResourceId<Kind> {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 && value <= 2_147_483_647;
}

// A brand is assigned only at the runtime boundary, not at a call site
// that happens to have a numeric ID from another resource.
export function parseResourceId<Kind extends ResourceKind>(value: unknown, kind: Kind): ResourceId<Kind> {
  if (!isResourceId<Kind>(value)) {
    throw new TypeError(`Invalid ${kind} ID`);
  }
  return value;
}

export function parseAssociationId<Kind extends ResourceKind>(value: unknown, kind: Kind): ResourceId<Kind> | 0 | null | undefined {
  if (value === 0) {
    return 0;
  }
  if (value === null) {
    return null;
  }
  if (value === undefined) {
    return undefined;
  }
  return parseResourceId(value, kind);
}

export interface BookIdentity {
  id: BookId;
  foreignBookId?: string | null;
  foreignEditionId?: string | null;
}

export interface EditionIdentity {
  id: EditionId;
  bookId: BookId;
  foreignEditionId?: string | null;
  isEbook: boolean;
  format?: string | null;
  isbn13?: string | null;
  asin?: string | null;
  audibleRegion?: string | null;
}

export const audibleMarketplaces = [
  'au', 'ca', 'de', 'es', 'fr', 'in', 'it', 'jp', 'us', 'uk',
] as const;

export type AudibleMarketplace = (typeof audibleMarketplaces)[number];
export type MarketplaceRecordingId<Marketplace extends AudibleMarketplace = AudibleMarketplace> =
  `audible:${Marketplace}:${string}`;
export type LocalRecordingId = `local:recording:${string}`;
export type RecordingId = MarketplaceRecordingId | LocalRecordingId;

export type RecordingIdentity =
  | { source: 'audible'; id: MarketplaceRecordingId; marketplace: AudibleMarketplace; asin: string }
  | { source: 'local'; id: LocalRecordingId };
