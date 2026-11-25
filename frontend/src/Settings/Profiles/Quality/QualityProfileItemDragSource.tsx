import classNames from 'classnames';
import React, { useCallback, useRef } from 'react';
import { type DropTargetMonitor, useDrag, useDrop } from 'react-dnd';
import { QUALITY_PROFILE_ITEM } from 'Helpers/dragTypes';
import { getDropPosition } from 'Utilities/Drag/reorderGeometry';
import type { QualityDragEnd, QualityDragMove, QualityItemAllowedChange, QualityProfileDragItem, QualityProfileDragMove, QualityProfileGroupItem } from './qualityProfileDragTypes';
import QualityProfileItem from './QualityProfileItem';
import QualityProfileItemGroup from './QualityProfileItemGroup';
import styles from './QualityProfileItemDragSource.css';

interface QualityProfileItemDragSourceProps extends Omit<QualityProfileDragItem, 'isGroup'> {
  items?: QualityProfileGroupItem[];
  isDragging?: boolean;
  isDraggingUp: boolean;
  isDraggingDown: boolean;
  onCreateGroupPress?: (qualityId: number) => void;
  onDeleteGroupPress?: (groupId: number) => void;
  onQualityProfileItemAllowedChange: QualityItemAllowedChange;
  onItemGroupAllowedChange?: (groupId: number, allowed: boolean) => void;
  onItemGroupNameChange?: (groupId: number, name: string) => void;
  onQualityProfileItemDragMove: QualityDragMove;
  onQualityProfileItemDragEnd: QualityDragEnd;
}

function QualityProfileItemDragSource(props: QualityProfileItemDragSourceProps) {
  const {
    editGroups, groupId, qualityId, name, allowed, items, qualityIndex,
    isDraggingUp, isDraggingDown, onCreateGroupPress, onDeleteGroupPress,
    onQualityProfileItemAllowedChange, onItemGroupAllowedChange, onItemGroupNameChange,
    onQualityProfileItemDragMove, onQualityProfileItemDragEnd
  } = props;

  const dropTargetRef = useRef<HTMLDivElement | null>(null);

  const [{ isDragging }, connectDragSource] = useDrag<QualityProfileDragItem, QualityProfileDragMove, { isDragging: boolean }>(() => ({
    type: QUALITY_PROFILE_ITEM,
    item: () => ({ editGroups, qualityIndex, groupId, qualityId, isGroup: qualityId == null, name, allowed }),
    end: (item, monitor) => {
      const result = monitor.getDropResult();
      const isValid = monitor.didDrop() &&
        result?.dragQualityIndex === item.qualityIndex &&
        typeof result.dropQualityIndex === 'string' &&
        (result.dropPosition === 'above' || result.dropPosition === 'below');
      onQualityProfileItemDragEnd(isValid ? result : null);
    },
    collect: (monitor) => ({ isDragging: monitor.isDragging() })
  }), [editGroups, qualityIndex, groupId, qualityId, name, allowed, onQualityProfileItemDragEnd]);

  const [{ isOverCurrent }, connectDropTarget] = useDrop<QualityProfileDragItem, QualityProfileDragMove, { isOverCurrent: boolean }>(() => {
    const getMove = (item: QualityProfileDragItem, monitor: DropTargetMonitor<QualityProfileDragItem, QualityProfileDragMove>): QualityProfileDragMove | undefined => {
      if (!monitor.isOver({ shallow: true }) || item.qualityIndex === qualityIndex) {
        return undefined;
      }
      if (item.isGroup && qualityId != null && groupId != null) {
        return undefined;
      }

      const node = dropTargetRef.current;
      const offset = monitor.getClientOffset();
      const row = node && Array.from(node.children).find((child) => !child.classList.contains(styles.qualityProfileItemPlaceholder));
      if (!row || !offset) {
        return undefined;
      }

      const position = getDropPosition(row.getBoundingClientRect(), offset.y);
      return position ? { dragQualityIndex: item.qualityIndex, dropQualityIndex: qualityIndex, dropPosition: position } : undefined;
    };

    return {
      accept: QUALITY_PROFILE_ITEM,
      hover: (item, monitor) => {
        const move = getMove(item, monitor);
        if (move) {
          onQualityProfileItemDragMove(move);
        }
      },
      // A nested target owns its result, even when it rejects the move.
      drop: (item, monitor) => monitor.didDrop() ? undefined : getMove(item, monitor),
      collect: (monitor) => ({ isOverCurrent: monitor.isOver({ shallow: true }) })
    };
  }, [qualityIndex, qualityId, groupId, onQualityProfileItemDragMove]);

  const setDragHandle = useCallback((node: HTMLDivElement | null) => {
    connectDragSource(node);
  }, [connectDragSource]);
  const setDropTarget = useCallback((node: HTMLDivElement | null) => {
    dropTargetRef.current = node;
    connectDropTarget(node);
  }, [connectDropTarget]);

  const isBefore = !isDragging && isDraggingUp && isOverCurrent;
  const isAfter = !isDragging && isDraggingDown && isOverCurrent;

  return (
    <div
      ref={setDropTarget}
      className={styles.qualityProfileItemDragSource}
    >
      {
        isBefore &&
          <div className={classNames(styles.qualityProfileItemPlaceholder, styles.qualityProfileItemPlaceholderBefore)} />
      }

      {
        groupId != null && qualityId == null && items &&
          <QualityProfileItemGroup
            editGroups={editGroups}
            groupId={groupId}
            name={name}
            allowed={allowed}
            items={items}
            qualityIndex={qualityIndex}
            isDragging={isDragging}
            isDraggingUp={isDraggingUp}
            isDraggingDown={isDraggingDown}
            dragHandleRef={setDragHandle}
            onDeleteGroupPress={onDeleteGroupPress}
            onQualityProfileItemAllowedChange={onQualityProfileItemAllowedChange}
            onItemGroupAllowedChange={onItemGroupAllowedChange}
            onItemGroupNameChange={onItemGroupNameChange}
            onQualityProfileItemDragMove={onQualityProfileItemDragMove}
            onQualityProfileItemDragEnd={onQualityProfileItemDragEnd}
          />
      }

      {
        qualityId != null &&
          <QualityProfileItem
            editGroups={editGroups}
            groupId={groupId}
            qualityId={qualityId}
            name={name}
            allowed={allowed}
            isDragging={isDragging}
            dragHandleRef={setDragHandle}
            onCreateGroupPress={onCreateGroupPress}
            onQualityProfileItemAllowedChange={onQualityProfileItemAllowedChange}
          />
      }

      {
        isAfter &&
          <div className={classNames(styles.qualityProfileItemPlaceholder, styles.qualityProfileItemPlaceholderAfter)} />
      }
    </div>
  );
}

export default QualityProfileItemDragSource;
