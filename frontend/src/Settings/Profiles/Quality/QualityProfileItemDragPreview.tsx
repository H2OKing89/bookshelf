import React from 'react';
import { useDragLayer } from 'react-dnd';
import DragPreviewLayer from 'Components/DragPreviewLayer';
import { QUALITY_PROFILE_ITEM } from 'Helpers/dragTypes';
import dimensions from 'Styles/Variables/dimensions.js';
import type { QualityProfileDragItem } from './qualityProfileDragTypes';
import QualityProfileItem from './QualityProfileItem';
import styles from './QualityProfileItemDragPreview.css';

const formGroupExtraSmallWidth = parseInt(dimensions.formGroupExtraSmallWidth);
const formLabelSmallWidth = parseInt(dimensions.formLabelSmallWidth);
const formLabelRightMarginWidth = parseInt(dimensions.formLabelRightMarginWidth);
const dragHandleWidth = parseInt(dimensions.dragHandleWidth);

function QualityProfileItemDragPreview() {
  const { item, currentOffset } = useDragLayer((monitor) => ({
    item: monitor.getItemType() === QUALITY_PROFILE_ITEM ? monitor.getItem<QualityProfileDragItem | null>() : null,
    currentOffset: monitor.getSourceClientOffset()
  }));

  if (!currentOffset || !item) {
    return null;
  }

  const previewId = item.qualityId ?? item.groupId;
  if (previewId == null) {
    return null;
  }

  const { x, y } = currentOffset;
  const handleOffset = formGroupExtraSmallWidth - formLabelSmallWidth - formLabelRightMarginWidth - dragHandleWidth;
  const transform = `translate3d(${x - handleOffset}px, ${y}px, 0)`;

  return (
    <DragPreviewLayer>
      <div
        className={styles.dragPreview}
        style={{ position: 'absolute', transform }}
      >
        <QualityProfileItem
          editGroups={item.editGroups}
          isPreview={true}
          qualityId={previewId}
          name={item.name}
          allowed={item.allowed}
          isDragging={false}
        />
      </div>
    </DragPreviewLayer>
  );
}

export default QualityProfileItemDragPreview;
