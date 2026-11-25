import React from 'react';
import { useDragLayer } from 'react-dnd';
import DragPreviewLayer from 'Components/DragPreviewLayer';
import { DELAY_PROFILE } from 'Helpers/dragTypes';
import dimensions from 'Styles/Variables/dimensions.js';
import DelayProfile, { type DelayProfileDragItem } from './DelayProfile';
import styles from './DelayProfileDragPreview.css';

const dragHandleWidth = parseInt(dimensions.dragHandleWidth);

function DelayProfileDragPreview({ width }: { width: number }) {
  const { item, currentOffset } = useDragLayer((monitor) => ({
    item: monitor.getItemType() === DELAY_PROFILE ? monitor.getItem<DelayProfileDragItem | null>() : null,
    currentOffset: monitor.getSourceClientOffset()
  }));

  if (!currentOffset || !item) {
    return null;
  }

  const { x, y } = currentOffset;
  const handleOffset = width - dragHandleWidth;
  const transform = `translate3d(${x - handleOffset}px, ${y}px, 0)`;

  return (
    <DragPreviewLayer>
      <div
        className={styles.dragPreview}
        style={{ width, position: 'absolute', transform }}
      >
        <DelayProfile {...item} isDragging={false} isPreview={true} />
      </div>
    </DragPreviewLayer>
  );
}

export default DelayProfileDragPreview;
