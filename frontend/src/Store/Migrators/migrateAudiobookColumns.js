// Only replace the untouched legacy author-book layout. Customized column
// visibility and ordering, including Pages for retained ebooks, stay intact.
const legacyColumns = [
  ['select', true],
  ['monitored', true],
  ['title', true],
  ['series', false],
  ['releaseDate', true],
  ['pageCount', true],
  ['rating', true],
  ['indexerFlags', false],
  ['status', true],
  ['actions', true]
];

export default function migrateAudiobookColumns(persistedState) {
  const columns = persistedState?.books?.columns;

  if (!Array.isArray(columns) || columns.length !== legacyColumns.length) {
    return;
  }

  const isLegacyDefault = columns.every((column, index) => {
    const [name, isVisible] = legacyColumns[index];
    return column?.name === name && column.isVisible === isVisible;
  });

  if (isLegacyDefault) {
    columns.find((column) => column.name === 'pageCount').isVisible = false;
  }
}
