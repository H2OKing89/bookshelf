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

export interface RecordingContributor {
  name?: string | null;
  asin?: string | null;
  role?: string | null;
}

export interface RecordingClassification {
  name?: string | null;
  asin?: string | null;
  type?: string | null;
}

export interface RecordingChapter {
  ordinal: number;
  title?: string | null;
  startOffsetMs?: number | null;
  lengthMs?: number | null;
}

export interface RecordingChapterMetadata {
  runtimeLengthMs?: number | null;
  chapters?: RecordingChapter[] | null;
  brandIntroDurationMs?: number | null;
  brandOutroDurationMs?: number | null;
  isAccurate?: boolean | null;
}

export interface RecordingMetadata {
  contributors?: RecordingContributor[] | null;
  publisher?: string | null;
  language?: string | null;
  copyrightYear?: number | null;
  literatureType?: string | null;
  isAdult?: boolean | null;
  releaseDateIsPlaceholder?: boolean | null;
}

export interface LibraryEdition extends RecordingMetadata {
  id: EditionId;
  bookId?: Association<BookId>;
  title?: string | null;
  titleSlug?: string | null;
  foreignEditionId?: string | null;
  asin?: string | null;
  audibleRegion?: string | null;
  isbn13?: string | null;
  disambiguation?: string | null;
  chapterMetadata?: RecordingChapterMetadata | null;
  format?: string | null;
  isEbook?: boolean | null;
  pageCount?: number | null;
  monitored?: boolean | null;
  runtimeLengthMinutes?: number | null;
  isAdult?: boolean | null;
}

export interface LibraryBook extends RecordingMetadata {
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
  seriesTitle?: string | null;
  classifications?: RecordingClassification[] | null;
  genres?: string[] | null;
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

// ParsedTrackInfo is distinct from MediaInfoModel; its optional tags can also
// describe ebooks and files not associated with an Audible edition.
export interface AudioTags {
  title?: string | null;
  bookTitle?: string | null;
  authorTitle?: string | null;
  seriesTitle?: string | null;
  seriesIndex?: string | null;
  language?: string | null;
  label?: string | null;
  publisher?: string | null;
  catalogNumber?: string | null;
  disambiguation?: string | null;
  isbn?: string | null;
  asin?: string | null;
  authorMBId?: string | null;
  bookMBId?: string | null;
  releaseMBId?: string | null;
  recordingMBId?: string | null;
  trackMBId?: string | null;
  trackNumbers?: number[] | null;
  discNumber?: number | null;
  discCount?: number | null;
  year?: number | null;
  country?: { name?: string | null } | null;
}

export interface ReleaseGroupEvidence {
  source?: string | null;
  reason?: string | null;
  packUploaderGroup?: string | null;
  downloadGroup?: string | null;
  folderGroup?: string | null;
  recordingFolderGroup?: string | null;
  fileGroup?: string | null;
  recordingId?: string | null;
  resolvedGroup?: string | null;
  packRecordingIds?: string[] | null;
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
  audioTags?: AudioTags | null;
  releaseGroup?: string | null;
  releaseGroupEvidence?: ReleaseGroupEvidence | null;
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

function parseOptional<T>(value: unknown, parse: (value: unknown) => T): T | null | undefined {
  if (value == null) {
    return value === null ? null : undefined;
  }
  return parse(value);
}

function parseOptionalList<T>(value: unknown, parse: (item: unknown) => T): T[] | null | undefined {
  return parseOptional(value, (items) => {
    if (!Array.isArray(items)) {
      throw new TypeError('Invalid library list');
    }
    return items.map(parse);
  });
}

function parseRecordingContributor(value: unknown): RecordingContributor {
  const item = resource(value);
  return omitUndefined({
    ...item,
    name: optionalText(item.name),
    asin: optionalText(item.asin),
    role: optionalText(item.role)
  });
}

function parseRecordingClassification(value: unknown): RecordingClassification {
  const item = resource(value);
  return omitUndefined({
    ...item,
    name: optionalText(item.name),
    asin: optionalText(item.asin),
    type: optionalText(item.type)
  });
}

function parseRecordingChapter(value: unknown): RecordingChapter {
  const item = resource(value);
  const ordinal = optionalNonnegativeInteger(item.ordinal);
  if (ordinal == null) {
    throw new TypeError('Invalid chapter ordinal');
  }
  return omitUndefined({
    ...item,
    ordinal,
    title: optionalText(item.title),
    startOffsetMs: optionalNonnegativeInteger(item.startOffsetMs),
    lengthMs: optionalNonnegativeInteger(item.lengthMs)
  });
}

function parseRecordingChapterMetadata(value: unknown): RecordingChapterMetadata | null | undefined {
  return parseOptional(value, (data) => {
    const item = resource(data);
    return omitUndefined({
      ...item,
      runtimeLengthMs: optionalNonnegativeInteger(item.runtimeLengthMs),
      brandIntroDurationMs: optionalNonnegativeInteger(item.brandIntroDurationMs),
      brandOutroDurationMs: optionalNonnegativeInteger(item.brandOutroDurationMs),
      isAccurate: optionalFlag(item.isAccurate),
      chapters: parseOptionalList(item.chapters, parseRecordingChapter)
    });
  });
}

function parseRecordingMetadata(item: Record<string, unknown>): RecordingMetadata {
  return omitUndefined({
    contributors: parseOptionalList(item.contributors, parseRecordingContributor),
    publisher: optionalText(item.publisher),
    language: optionalText(item.language),
    copyrightYear: optionalNonnegativeInteger(item.copyrightYear),
    literatureType: optionalText(item.literatureType),
    isAdult: optionalFlag(item.isAdult),
    releaseDateIsPlaceholder: optionalFlag(item.releaseDateIsPlaceholder)
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
    ...parseRecordingMetadata(item),
    disambiguation: optionalText(item.disambiguation),
    chapterMetadata: parseRecordingChapterMetadata(item.chapterMetadata),
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

function parseAudioTags(value: unknown): AudioTags | null | undefined {
  return parseOptional(value, (data) => {
    const item = resource(data);
    return omitUndefined({
      ...item,
      title: optionalText(item.title),
      bookTitle: optionalText(item.bookTitle),
      authorTitle: optionalText(item.authorTitle),
      seriesTitle: optionalText(item.seriesTitle),
      seriesIndex: optionalText(item.seriesIndex),
      language: optionalText(item.language),
      label: optionalText(item.label),
      publisher: optionalText(item.publisher),
      catalogNumber: optionalText(item.catalogNumber),
      disambiguation: optionalText(item.disambiguation),
      isbn: optionalText(item.isbn),
      asin: optionalText(item.asin),
      authorMBId: optionalText(item.authorMBId),
      bookMBId: optionalText(item.bookMBId),
      releaseMBId: optionalText(item.releaseMBId),
      recordingMBId: optionalText(item.recordingMBId),
      trackMBId: optionalText(item.trackMBId),
      trackNumbers: parseOptionalList(item.trackNumbers, (value) => {
        const number = optionalQuantity(value);
        if (number == null || !Number.isInteger(number)) {
          throw new TypeError('Invalid track number');
        }
        return number;
      }),
      discNumber: optionalQuantity(item.discNumber),
      discCount: optionalQuantity(item.discCount),
      year: optionalNonnegativeInteger(item.year),
      country: parseOptional(item.country, (value) => {
        const country = resource(value);
        return omitUndefined({ ...country, name: optionalText(country.name) });
      })
    });
  });
}

function parseReleaseGroupEvidence(value: unknown): ReleaseGroupEvidence | null | undefined {
  return parseOptional(value, (data) => {
    const item = resource(data);
    return omitUndefined({
      ...item,
      source: optionalText(item.source),
      reason: optionalText(item.reason),
      packUploaderGroup: optionalText(item.packUploaderGroup),
      downloadGroup: optionalText(item.downloadGroup),
      folderGroup: optionalText(item.folderGroup),
      recordingFolderGroup: optionalText(item.recordingFolderGroup),
      fileGroup: optionalText(item.fileGroup),
      recordingId: optionalText(item.recordingId),
      resolvedGroup: optionalText(item.resolvedGroup),
      packRecordingIds: parseOptionalList(item.packRecordingIds, requiredText)
    });
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
    mediaInfo: parseMediaInfo(item.mediaInfo),
    audioTags: parseAudioTags(item.audioTags),
    releaseGroup: optionalText(item.releaseGroup),
    releaseGroupEvidence: parseReleaseGroupEvidence(item.releaseGroupEvidence)
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
    ...parseRecordingMetadata(item),
    seriesTitle: optionalText(item.seriesTitle),
    classifications: parseOptionalList(item.classifications, parseRecordingClassification),
    genres: parseOptionalList(item.genres, requiredText)
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
