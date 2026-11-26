import { type AuthorId, type BookId,parseAssociationId } from 'Book/BookIdentity';
import { parseQualityModel, type QualityModel } from 'Book/LibraryModels';
import { asStateRecord, type StateRecord } from 'Store/StateTypes';

export interface AuthorHistoryItem {
  id: number;
  authorId: AuthorId | 0 | null | undefined;
  bookId: BookId | 0 | null | undefined;
  eventType: string;
  sourceTitle: string;
  date: string;
  quality?: QualityModel | null;
  qualityCutoffNotMet: boolean;
  customFormats: { id: number; name: string }[];
  customFormatScore: number;
  data: StateRecord;
}

function association(value: unknown) {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
}

function qualityModel(value: unknown) {
  try {
    return parseQualityModel(value);
  } catch {
    return undefined;
  }
}

export function projectAuthorHistory(source: unknown) {
  const section = asStateRecord(source);
  const items: AuthorHistoryItem[] = Array.isArray(section.items) ?
    section.items.flatMap((entry: unknown) => {
      const item = asStateRecord(entry);
      if (typeof item.id !== 'number' || !Number.isSafeInteger(item.id) || item.id <= 0) {
        return [];
      }

      const customFormats = Array.isArray(item.customFormats) ? item.customFormats.flatMap((value: unknown) => {
        const format = asStateRecord(value);
        return typeof format.id === 'number' && typeof format.name === 'string' ? [{ id: format.id, name: format.name }] : [];
      }) : [];

      return [{
        id: item.id,
        authorId: parseAssociationId(association(item.authorId), 'author'),
        bookId: parseAssociationId(association(item.bookId), 'book'),
        eventType: typeof item.eventType === 'string' ? item.eventType : 'unknown',
        sourceTitle: typeof item.sourceTitle === 'string' ? item.sourceTitle : '',
        date: typeof item.date === 'string' ? item.date : '',
        quality: qualityModel(item.quality),
        qualityCutoffNotMet: item.qualityCutoffNotMet === true,
        customFormats,
        customFormatScore: typeof item.customFormatScore === 'number' ? item.customFormatScore : 0,
        data: asStateRecord(item.data)
      }];
    }) : [];

  return {
    isFetching: section.isFetching === true,
    isPopulated: section.isPopulated === true,
    error: section.error,
    items
  };
}

export type AuthorHistoryViewProps = ReturnType<typeof projectAuthorHistory> & {
  bookId?: BookId | null;
  onMarkAsFailedPress: (historyId: number) => void;
};
