import classNames from 'classnames';
import React, { useCallback, useRef } from 'react';
import { type DropTargetMonitor, useDrag, useDrop } from 'react-dnd';
import { DELAY_PROFILE } from 'Helpers/dragTypes';
import { getDelayProfileDropOrder, getDropPosition } from 'Utilities/Drag/reorderGeometry';
import DelayProfile, { type DelayProfileDragItem } from './DelayProfile';
import styles from './DelayProfileDragSource.css';

interface DelayDropResult {
  dropOrder: number;
}

interface DelayProfileDragSourceProps extends DelayProfileDragItem {
  isDraggingUp: boolean;
  isDraggingDown: boolean;
  onConfirmDeleteDelayProfile: (id: number) => void;
  onDelayProfileDragMove: (dragOrder: number, dropOrder: number) => void;
  onDelayProfileDragEnd: (item: DelayProfileDragItem, dropOrder: number | null) => void;
}

function DelayProfileDragSource(props: DelayProfileDragSourceProps) {
  const {
    id, order, enableUsenet, enableTorrent, preferredProtocol, usenetDelay,
    torrentDelay, tags, tagList, isDraggingUp, isDraggingDown,
    onConfirmDeleteDelayProfile, onDelayProfileDragMove, onDelayProfileDragEnd
  } = props;

  const dropTargetRef = useRef<HTMLDivElement | null>(null);
  const [{ isDragging }, connectDragSource] = useDrag<DelayProfileDragItem, DelayDropResult, { isDragging: boolean }>(() => ({
    type: DELAY_PROFILE,
    item: () => ({ id, order, enableUsenet, enableTorrent, preferredProtocol, usenetDelay, torrentDelay, tags, tagList }),
    end: (item, monitor) => {
      const result = monitor.getDropResult();
      onDelayProfileDragEnd(item, monitor.didDrop() && typeof result?.dropOrder === 'number' ? result.dropOrder : null);
    },
    collect: (monitor) => ({ isDragging: monitor.isDragging() })
  }), [id, order, enableUsenet, enableTorrent, preferredProtocol, usenetDelay, torrentDelay, tags, tagList, onDelayProfileDragEnd]);

  const [{ isOver }, connectDropTarget] = useDrop<DelayProfileDragItem, DelayDropResult, { isOver: boolean }>(() => {
    const getDestination = (item: DelayProfileDragItem, monitor: DropTargetMonitor<DelayProfileDragItem, DelayDropResult>) => {
      const node = dropTargetRef.current;
      const offset = monitor.getClientOffset();
      const row = node && Array.from(node.children).find((child) => !child.classList.contains(styles.delayProfilePlaceholder));
      if (!row || !offset) {
        return null;
      }

      return getDelayProfileDropOrder(item.order, order, getDropPosition(row.getBoundingClientRect(), offset.y));
    };

    return {
      accept: DELAY_PROFILE,
      hover: (item, monitor) => {
        const destination = getDestination(item, monitor);
        if (destination !== null) {
          onDelayProfileDragMove(item.order, destination);
        }
      },
      drop: (item, monitor) => {
        const dropOrder = getDestination(item, monitor);
        return dropOrder === null ? undefined : { dropOrder };
      },
      collect: (monitor) => ({ isOver: monitor.isOver() })
    };
  }, [order, onDelayProfileDragMove]);

  const setDragHandle = useCallback((node: HTMLDivElement | null) => {
    connectDragSource(node);
  }, [connectDragSource]);
  const setDropTarget = useCallback((node: HTMLDivElement | null) => {
    dropTargetRef.current = node;
    connectDropTarget(node);
  }, [connectDropTarget]);

  const isBefore = !isDragging && isDraggingUp && isOver;
  const isAfter = !isDragging && isDraggingDown && isOver;

  return (
    <div ref={setDropTarget} className={styles.delayProfileDragSource}>
      {
        isBefore &&
          <div className={classNames(styles.delayProfilePlaceholder, styles.delayProfilePlaceholderBefore)} />
      }

      <DelayProfile
        id={id}
        order={order}
        enableUsenet={enableUsenet}
        enableTorrent={enableTorrent}
        preferredProtocol={preferredProtocol}
        usenetDelay={usenetDelay}
        torrentDelay={torrentDelay}
        tags={tags}
        tagList={tagList}
        isDragging={isDragging}
        dragHandleRef={setDragHandle}
        onConfirmDeleteDelayProfile={onConfirmDeleteDelayProfile}
      />

      {
        isAfter &&
          <div className={classNames(styles.delayProfilePlaceholder, styles.delayProfilePlaceholderAfter)} />
      }
    </div>
  );
}

export default DelayProfileDragSource;
