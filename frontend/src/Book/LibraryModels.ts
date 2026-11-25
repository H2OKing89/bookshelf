import { type AuthorId, type BookFileId, type BookId, type EditionId, parseAssociationId, parseResourceId } from './BookIdentity';

export function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function resource(value: unknown): Record<string, unknown> {
  if (!isRecord(value)) {
    throw new TypeError('Invalid library resource');
  }
  return value;
}

export function optionalText(value: unknown): string | null | undefined {
  if (typeof value === 'string') {
    return value;
  }
  if (value == null) {
    return value === null ? null : undefined;
  }
  throw new TypeError('Invalid library text');
}

export function requiredText(value: unknown): string {
  if (typeof value !== 'string') {
    throw new TypeError('Invalid library text');
  }
  return value;
}

export function optionalFlag(value: unknown): boolean | null | undefined {
  if (typeof value === 'boolean') {
    return value;
  }
  if (value == null) {
    return value === null ? null : undefined;
  }
  throw new TypeError('Invalid library flag');
}

export function optionalQuantity(value: unknown): number | null | undefined {
  if (value == null) {
    return value === null ? null : undefined;
  }
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new TypeError('Invalid library quantity');
  }
  return value;
}

function optionalNonnegativeInteger(value: unknown): number | null | undefined {
  const number = optionalQuantity(value);
  if (number != null && (!Number.isSafeInteger(number) || number < 0)) {
    throw new TypeError('Invalid library count');
  }
  return number;
}

function omitUndefined<T extends object>(item: T): T {
  for (const key of Object.keys(item)) {
    if (Reflect.get(item, key) === undefined) {
      Reflect.deleteProperty(item, key);
    }
  }
  return item;
}

export interface QualityModel {
  quality: { id?: number | null; name: string };
  revision: { real?: number | null; version?: number | null; isRepack?: boolean | null };
}

// Quality ID zero is a legitimate Unknown quality. It is not a database
// association, so it must not be passed through the positive-ID decoder.
export function parseQualityModel(value: unknown): QualityModel | null | undefined {
  if (value == null) {
    return value === null ? null : undefined;
  }
  const model = resource(value);
  const quality = resource(model.quality);
  const revision = resource(model.revision);
  return {
    ...model,
    quality: omitUndefined({ ...quality, id: optionalNonnegativeInteger(quality.id), name: requiredText(quality.name) }),
    revision: omitUndefined({
      ...revision,
      real: optionalNonnegativeInteger(revision.real),
      version: optionalNonnegativeInteger(revision.version),
      isRepack: optionalFlag(revision.isRepack)
    })
  };
}

type Association<Id> = Id | 0 | null | undefined;

export interface LibraryAuthor {
  id: AuthorId;
  authorName?: string | null;
  foreignAuthorId?: string | null;
  titleSlug?: string | null;
  sortName?: string | null;
  monitored?: boolean | null;
  qualityProfileId?: number | null;
  metadataProfileId?: number | null;
}

export interface LibraryEdition {
  id: EditionId;
  bookId?: Association<BookId>;
  title?: string | null;
  titleSlug?: string | null;
  foreignEditionId?: string | null;
  asin?: string | null;
  audibleRegion?: string | null;
  isbn13?: string | null;
  format?: string | null;
  isEbook?: boolean | null;
  pageCount?: number | null;
  monitored?: boolean | null;
  runtimeLengthMinutes?: number | null;
  isAdult?: boolean | null;
}

export interface LibraryBook {
  id: BookId;
  authorId?: Association<AuthorId>;
  title?: string | null;
  titleSlug?: string | null;
  foreignBookId?: string | null;
  foreignEditionId?: string | null;
  disambiguation?: string | null;
  monitored?: boolean | null;
  anyEditionOk?: boolean | null;
  releaseDate?: string | null;
  lastSearchTime?: string | null;
  releaseDateIsPlaceholder?: boolean | null;
  pageCount?: number | null;
  runtimeLengthMinutes?: number | null;
  runtimeLengthMs?: number | null;
  isAdult?: boolean | null;
  author?: LibraryAuthor | null;
  editions?: LibraryEdition[] | null;
}

export interface AudioStreamInfo {
  index?: number | null;
  audioChannels?: number | null;
  audioCodec?: string | null;
  audioBitRate?: string | null;
  audioBits?: string | null;
  audioSampleRate?: string | null;
  audioFormat?: string | null;
  audioProfile?: string | null;
  audioAdditionalFeatures?: string | null;
  audioCommercialName?: string | null;
  audioCodecId?: string | null;
  audioBitrateMode?: string | null;
  audioChannelLayout?: string | null;
  durationSeconds?: number | null;
  language?: string | null;
  title?: string | null;
  isDefault?: boolean | null;
}

export interface MediaInfoModel extends AudioStreamInfo {
  containerFormat?: string | null;
  source?: string | null;
  sourceVersion?: string | null;
  analysisRevision?: number | null;
  analysisStatus?: string | null;
  audioStreams?: AudioStreamInfo[] | null;
}

export interface LibraryBookFile {
  id: BookFileId;
  bookId?: Association<BookId>;
  authorId?: Association<AuthorId>;
  path?: string | null;
  size?: number | null;
  dateAdded?: string | null;
  quality?: QualityModel | null;
  qualityRank?: number | null;
  qualityWeight?: number | null;
  qualityCutoffNotMet?: boolean | null;
  mediaInfo?: MediaInfoModel | null;
}

// Preserve fields that remain in legacy JavaScript consumers (including ebook
// metadata, images, ratings, and provider-specific details). The strict
// projection only exposes validated fields, not an arbitrary index signature.
export function parseLibraryAuthor(value: unknown): LibraryAuthor {
  const item = resource(value);
  return omitUndefined({
    ...item,
    id: parseResourceId(item.id, 'author'),
    authorName: optionalText(item.authorName),
    foreignAuthorId: optionalText(item.foreignAuthorId),
    titleSlug: optionalText(item.titleSlug),
    sortName: optionalText(item.sortName),
    monitored: optionalFlag(item.monitored),
    qualityProfileId: optionalNonnegativeInteger(item.qualityProfileId),
    metadataProfileId: optionalNonnegativeInteger(item.metadataProfileId)
  });
}

export function parseLibraryEdition(value: unknown): LibraryEdition {
  const item = resource(value);
  return omitUndefined({
    ...item,
    id: parseResourceId(item.id, 'edition'),
    bookId: parseAssociationId(item.bookId, 'book'),
    title: optionalText(item.title),
    titleSlug: optionalText(item.titleSlug),
    foreignEditionId: optionalText(item.foreignEditionId),
    asin: optionalText(item.asin),
    audibleRegion: optionalText(item.audibleRegion),
    isbn13: optionalText(item.isbn13),
    format: optionalText(item.format),
    isEbook: optionalFlag(item.isEbook),
    pageCount: optionalNonnegativeInteger(item.pageCount),
    monitored: optionalFlag(item.monitored),
    runtimeLengthMinutes: optionalNonnegativeInteger(item.runtimeLengthMinutes),
    isAdult: optionalFlag(item.isAdult)
  });
}

function parseAudioStream(value: unknown): AudioStreamInfo {
  const item = resource(value);
  const channels = optionalQuantity(item.audioChannels);
  const duration = optionalQuantity(item.durationSeconds);
  if ((channels != null && channels < 0) || (duration != null && duration < 0)) {
    throw new TypeError('Invalid audio measurement');
  }
  return omitUndefined({
    ...item,
    index: optionalNonnegativeInteger(item.index),
    audioChannels: channels,
    audioCodec: optionalText(item.audioCodec),
    audioBitRate: optionalText(item.audioBitRate),
    audioBits: optionalText(item.audioBits),
    audioSampleRate: optionalText(item.audioSampleRate),
    audioFormat: optionalText(item.audioFormat),
    audioProfile: optionalText(item.audioProfile),
    audioAdditionalFeatures: optionalText(item.audioAdditionalFeatures),
    audioCommercialName: optionalText(item.audioCommercialName),
    audioCodecId: optionalText(item.audioCodecId),
    audioBitrateMode: optionalText(item.audioBitrateMode),
    audioChannelLayout: optionalText(item.audioChannelLayout),
    durationSeconds: duration,
    language: optionalText(item.language),
    title: optionalText(item.title),
    isDefault: optionalFlag(item.isDefault)
  });
}

export function parseMediaInfo(value: unknown): MediaInfoModel | null | undefined {
  if (value == null) {
    return value === null ? null : undefined;
  }
  const item = resource(value);
  let audioStreams: AudioStreamInfo[] | null | undefined = undefined;
  if (item.audioStreams == null) {
    audioStreams = item.audioStreams === null ? null : undefined;
  } else if (Array.isArray(item.audioStreams)) {
    audioStreams = item.audioStreams.map(parseAudioStream);
  } else {
    throw new TypeError('Invalid audio stream list');
  }
  return omitUndefined({
    ...item,
    ...parseAudioStream(item),
    containerFormat: optionalText(item.containerFormat),
    source: optionalText(item.source),
    sourceVersion: optionalText(item.sourceVersion),
    analysisRevision: optionalNonnegativeInteger(item.analysisRevision),
    analysisStatus: optionalText(item.analysisStatus),
    audioStreams
  });
}

export function parseLibraryBookFile(value: unknown): LibraryBookFile {
  const item = resource(value);
  return omitUndefined({
    ...item,
    id: parseResourceId(item.id, 'bookFile'),
    bookId: parseAssociationId(item.bookId, 'book'),
    authorId: parseAssociationId(item.authorId, 'author'),
    path: optionalText(item.path),
    size: optionalNonnegativeInteger(item.size),
    dateAdded: optionalText(item.dateAdded),
    quality: parseQualityModel(item.quality),
    qualityRank: optionalQuantity(item.qualityRank),
    qualityWeight: optionalNonnegativeInteger(item.qualityWeight),
    qualityCutoffNotMet: optionalFlag(item.qualityCutoffNotMet),
    mediaInfo: parseMediaInfo(item.mediaInfo)
  });
}

export function parseLibraryCollection<T extends { id: number }>(value: unknown, parse: (value: unknown) => T): T[] {
  if (!Array.isArray(value)) {
    throw new TypeError('Invalid library collection');
  }
  const items = value.map(parse);
  if (new Set(items.map(({ id }) => id)).size !== items.length) {
    throw new TypeError('Duplicate library resource ID');
  }
  return items;
}

function parseOptional<T>(value: unknown, parse: (value: unknown) => T): T | null | undefined {
  if (value == null) {
    return value === null ? null : undefined;
  }
  return parse(value);
}

export function parseLibraryBook(value: unknown): LibraryBook {
  const item = resource(value);
  const id = parseResourceId(item.id, 'book');
  const authorId = parseAssociationId(item.authorId, 'author');
  const author = parseOptional(item.author, parseLibraryAuthor);
  const editions = parseOptional(item.editions, (value) => parseLibraryCollection(value, parseLibraryEdition));
  if ((author && authorId && author.id !== authorId) ||
      editions?.some((edition) => edition.bookId && edition.bookId !== id)) {
    throw new TypeError('Mismatched library resource association');
  }
  return omitUndefined({
    ...item,
    ...('author' in item ? { author } : {}),
    ...('editions' in item ? { editions } : {}),
    id,
    authorId,
    title: optionalText(item.title),
    titleSlug: optionalText(item.titleSlug),
    foreignBookId: optionalText(item.foreignBookId),
    foreignEditionId: optionalText(item.foreignEditionId),
    disambiguation: optionalText(item.disambiguation),
    monitored: optionalFlag(item.monitored),
    anyEditionOk: optionalFlag(item.anyEditionOk),
    releaseDate: optionalText(item.releaseDate),
    lastSearchTime: optionalText(item.lastSearchTime),
    releaseDateIsPlaceholder: optionalFlag(item.releaseDateIsPlaceholder),
    pageCount: optionalNonnegativeInteger(item.pageCount),
    runtimeLengthMinutes: optionalNonnegativeInteger(item.runtimeLengthMinutes),
    runtimeLengthMs: optionalNonnegativeInteger(item.runtimeLengthMs),
    isAdult: optionalFlag(item.isAdult)
  });
}

export function libraryResponseParser<T extends { id: number }>(parse: (value: unknown) => T) {
  return (value: unknown, id?: number | string | null): T[] | T => {
    if (id == null) {
      return parseLibraryCollection(value, parse);
    }
    const item = parse(value);
    if (String(item.id) !== String(id)) {
      throw new TypeError('Library response identity does not match request');
    }
    return item;
  };
}
