// eslint-disable-next-line init-declarations -- This symbol exists only during type checking.
declare const resourceKind: unique symbol;

export type BookId = number & { readonly [resourceKind]: 'book' };
export type EditionId = number & { readonly [resourceKind]: 'edition' };

export interface BookIdentity {
  id: BookId;
  foreignBookId?: string | null;
  foreignEditionId?: string | null;
}

export interface EditionIdentity {
  id: EditionId;
  bookId: BookId;
  foreignEditionId: string;
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
