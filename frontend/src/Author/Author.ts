import ModelBase from 'App/ModelBase';
import type { AuthorId } from 'Book/BookIdentity';

export type AuthorStatus = 'continuing' | 'ended';

interface Author extends ModelBase {
  id: AuthorId;
  added: string;
  genres: string[];
  monitored: boolean;
  overview?: string | null;
  titleSlug?: string | null;
  foreignAuthorId?: string | null;
  path: string;
  qualityProfileId: number;
  metadataProfileId: number;
  rootFolderPath: string;
  sortName: string;
  status: AuthorStatus;
  tags: number[];
  authorName: string;
  isSaving?: boolean;
}

export default Author;
