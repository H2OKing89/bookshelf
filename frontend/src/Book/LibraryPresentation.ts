import type { AuthorId } from './BookIdentity';

export interface LibraryImage {
  coverType: string;
  url?: string | null;
}

export interface LibraryLink {
  url: string;
  name: string;
}

export interface LibraryStatistics {
  bookCount?: number;
  availableBookCount?: number;
  bookFileCount?: number;
  totalBookCount?: number;
  sizeOnDisk?: number;
  fileQualityRank?: number | null;
}

export interface NamedProfile {
  name: string;
}

export interface BookReference {
  title: string;
  titleSlug: string;
  disambiguation?: string | null;
  releaseDate?: string | null;
}

export interface AuthorReference {
  id: AuthorId;
  titleSlug: string;
  authorName: string;
  path: string;
}

export interface LibraryColumn {
  name: string;
  isVisible?: boolean;
  isGrowable?: boolean;
}

export interface SelectionChange<Id extends number> {
  id: Id;
  value: boolean;
  shiftKey?: boolean;
}

export interface CheckboxChange {
  value: boolean;
  shiftKey?: boolean;
}
