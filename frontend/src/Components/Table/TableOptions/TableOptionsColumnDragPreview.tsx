import React from 'react';
import { useDragLayer } from 'react-dnd';
import DragPreviewLayer from 'Components/DragPreviewLayer';
import { TABLE_COLUMN } from 'Helpers/dragTypes';
import dimensions from 'Styles/Variables/dimensions.js';
import TableOptionsColumn, { type TableOptionsDragItem } from './TableOptionsColumn';
import styles from './TableOptionsColumnDragPreview.css';

const formGroupSmallWidth = parseInt(dimensions.formGroupSmallWidth);
const formLabelLargeWidth = parseInt(dimensions.formLabelLargeWidth);
const formLabelRightMarginWidth = parseInt(dimensions.formLabelRightMarginWidth);
const dragHandleWidth = parseInt(dimensions.dragHandleWidth);

function TableOptionsColumnDragPreview() {
  const { item, currentOffset } = useDragLayer((monitor) => ({
    item: monitor.getItemType() === TABLE_COLUMN ? monitor.getItem<TableOptionsDragItem | null>() : null,
    currentOffset: monitor.getSourceClientOffset()
  }));

  if (!currentOffset || !item) {
    return null;
  }

  // Shift the row preview left of the source handle.
  const { x, y } = currentOffset;
  const handleOffset = formGroupSmallWidth - formLabelLargeWidth - formLabelRightMarginWidth - dragHandleWidth;
  const transform = `translate3d(${x - handleOffset}px, ${y}px, 0)`;

  return (
    <DragPreviewLayer>
      <div
        className={styles.dragPreview}
        style={{ position: 'absolute', transform }}
      >
        <TableOptionsColumn {...item} isDragging={false} />
      </div>
    </DragPreviewLayer>
  );
}

export default TableOptionsColumnDragPreview;
