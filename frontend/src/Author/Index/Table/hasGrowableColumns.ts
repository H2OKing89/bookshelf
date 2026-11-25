import type { LibraryColumn } from 'Book/LibraryPresentation';

const growableColumns = [
  'qualityProfileId',
  'path',
  'tags'
];

export default function hasGrowableColumns(columns: readonly LibraryColumn[]): boolean {
  return columns.some((column) => {
    const {
      name,
      isVisible
    } = column;

    return growableColumns.includes(name) && isVisible;
  });
}
