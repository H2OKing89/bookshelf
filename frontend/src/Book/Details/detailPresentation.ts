import type { AuthorStatus } from 'Author/Author';
import type { LibraryImage, LibraryLink, LibraryStatistics } from 'Book/LibraryPresentation';
import { asStateRecord } from 'Store/StateTypes';

export function optionalText(value: unknown): string | null | undefined {
  if (value === null) {
    return null;
  }

  return typeof value === 'string' ? value : undefined;
}

export function optionalNumber(value: unknown): number | null | undefined {
  if (value === null) {
    return null;
  }

  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

export function textList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item: unknown): item is string => typeof item === 'string') : [];
}

export function imageList(value: unknown): LibraryImage[] {
  return Array.isArray(value) ? value.flatMap((entry: unknown) => {
    const image = asStateRecord(entry);
    return typeof image.coverType === 'string' ? [{ coverType: image.coverType, url: optionalText(image.url) }] : [];
  }) : [];
}

export function linkList(value: unknown): LibraryLink[] {
  return Array.isArray(value) ? value.flatMap((entry: unknown) => {
    const link = asStateRecord(entry);
    return typeof link.url === 'string' && typeof link.name === 'string' ? [{ url: link.url, name: link.name }] : [];
  }) : [];
}

export function detailStatistics(value: unknown): LibraryStatistics {
  const statistics = asStateRecord(value);
  return {
    bookCount: optionalNumber(statistics.bookCount) ?? undefined,
    availableBookCount: optionalNumber(statistics.availableBookCount) ?? undefined,
    bookFileCount: optionalNumber(statistics.bookFileCount) ?? undefined,
    totalBookCount: optionalNumber(statistics.totalBookCount) ?? undefined,
    sizeOnDisk: optionalNumber(statistics.sizeOnDisk) ?? undefined,
    fileQualityRank: optionalNumber(statistics.fileQualityRank)
  };
}

function commonDetail(value: unknown) {
  const item = asStateRecord(value);
  return {
    titleSlug: optionalText(item.titleSlug) ?? '',
    ratings: { value: optionalNumber(asStateRecord(item.ratings).value) ?? 0 },
    overview: optionalText(item.overview),
    statistics: detailStatistics(item.statistics),
    monitored: item.monitored === true,
    isSaving: item.isSaving === true,
    images: imageList(item.images),
    links: linkList(item.links)
  };
}

export function authorReference(value: unknown) {
  const author = asStateRecord(value);
  return {
    titleSlug: optionalText(author.titleSlug) ?? '',
    authorName: optionalText(author.authorName) ?? '',
    images: imageList(author.images)
  };
}

export function authorHeaderData(value: unknown) {
  const author = asStateRecord(value);
  const alternateTitles = Array.isArray(author.alternateTitles) ?
    author.alternateTitles.flatMap((entry: unknown) => {
      const title = asStateRecord(entry);
      const wholeAuthor = (title.seasonNumber === -1 || title.seasonNumber === undefined) &&
        (title.sceneSeasonNumber === -1 || title.sceneSeasonNumber === undefined);
      return wholeAuthor && typeof title.title === 'string' ? [title.title] : [];
    }) : [];

  const status: AuthorStatus = author.status === 'ended' ? 'ended' : 'continuing';

  return {
    ...commonDetail(author),
    authorName: optionalText(author.authorName) ?? '',
    path: optionalText(author.path) ?? '',
    qualityProfileId: optionalNumber(author.qualityProfileId) ?? 0,
    status,
    alternateTitles,
    tags: Array.isArray(author.tags) ? author.tags.filter((tag: unknown): tag is number => typeof tag === 'number') : []
  };
}

export function bookHeaderData(value: unknown) {
  const book = asStateRecord(value);
  return {
    ...commonDetail(book),
    title: optionalText(book.title) ?? '',
    displayTitle: optionalText(book.displayTitle),
    subtitle: optionalText(book.subtitle),
    displaySubtitle: optionalText(book.displaySubtitle),
    seriesTitle: optionalText(book.seriesTitle),
    displaySeriesTitle: optionalText(book.displaySeriesTitle),
    metadataLabels: textList(book.metadataLabels),
    metadataWarnings: textList(book.metadataWarnings),
    pageCount: optionalNumber(book.pageCount),
    narrators: textList(book.narrators),
    runtimeLengthMinutes: optionalNumber(book.runtimeLengthMinutes),
    releaseDate: optionalText(book.releaseDate)
  };
}
