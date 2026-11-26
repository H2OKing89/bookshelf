import type { BookFileId } from 'Book/BookIdentity';
import BookQuality from 'Book/BookQuality';
import type { MediaInfoModel,QualityModel } from 'Book/LibraryModels';
import type { SelectionChange } from 'Book/LibraryPresentation';
import MediaInfo from 'BookFile/MediaInfo';
import { AUDIO } from 'BookFile/mediaInfoTypes';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import formatBytes from 'Utilities/Number/formatBytes';
import BookFileActionsCell from './BookFileActionsCell';
import styles from './BookFileEditorRow.css';

interface BookFileEditorRowProps {
  id: BookFileId;
  path?: string | null;
  size?: number | null;
  dateAdded?: string | null;
  quality?: QualityModel | null;
  releaseGroup?: string | null;
  qualityCutoffNotMet?: boolean | null;
  mediaInfo?: MediaInfoModel | null;
  isSelected?: boolean;
  onSelectedChange: (change: SelectionChange<BookFileId>) => void;
  deleteBookFile: (options: { id: BookFileId }) => void;
}

function BookFileEditorRow(props: BookFileEditorRowProps) {
  const {
    id,
    path,
    size,
    dateAdded,
    quality,
    releaseGroup,
    qualityCutoffNotMet,
    mediaInfo,
    isSelected,
    onSelectedChange,
    deleteBookFile
  } = props;

  return (
    <TableRow>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={onSelectedChange}
      />
      <TableRowCell
        className={styles.path}
      >
        <div className={styles.filePath}>{path}</div>
        {mediaInfo && <MediaInfo type={AUDIO} {...mediaInfo} />}
      </TableRowCell>

      <TableRowCell
        className={styles.size}
      >
        {formatBytes(size)}
      </TableRowCell>

      <RelativeDateCellConnector
        className={styles.dateAdded}
        date={dateAdded}
      />

      <TableRowCell
        className={styles.quality}
      >
        <BookQuality
          quality={quality}
          isCutoffNotMet={qualityCutoffNotMet === true}
        />
      </TableRowCell>

      <TableRowCell>
        {releaseGroup || '—'}
      </TableRowCell>

      <BookFileActionsCell
        id={id}
        path={path}
        deleteBookFile={deleteBookFile}
      />
    </TableRow>
  );
}

export default BookFileEditorRow;
