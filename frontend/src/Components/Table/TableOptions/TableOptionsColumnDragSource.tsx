import classNames from 'classnames';
import React, { useCallback, useRef } from 'react';
import { type DropTargetMonitor, useDrag, useDrop } from 'react-dnd';
import { TABLE_COLUMN } from 'Helpers/dragTypes';
import { getColumnDropIndex, getDropPosition } from 'Utilities/Drag/reorderGeometry';
import TableOptionsColumn, { type TableOptionsColumnProps, type TableOptionsDragItem } from './TableOptionsColumn';
import styles from './TableOptionsColumnDragSource.css';

interface ColumnDropResult {
  name: string;
}

interface TableOptionsColumnDragSourceProps extends TableOptionsColumnProps {
  isDraggingUp: boolean;
  isDraggingDown: boolean;
  onColumnDragMove: (dragIndex: number, dropIndex: number) => void;
  onColumnDragEnd: (item: TableOptionsDragItem, targetName: string | null) => void;
}

function TableOptionsColumnDragSource(props: TableOptionsColumnDragSourceProps) {
  const {
    name, label, isVisible, isModifiable, index, isDraggingUp, isDraggingDown,
    onVisibleChange, onColumnDragMove, onColumnDragEnd
  } = props;
  const dropTargetRef = useRef<HTMLDivElement | null>(null);

  const [{ isDragging }, connectDragSource] = useDrag<TableOptionsDragItem, ColumnDropResult, { isDragging: boolean }>(() => ({
    type: TABLE_COLUMN,
    item: () => ({ name, label, isVisible, isModifiable, index }),
    end: (item, monitor) => {
      const result = monitor.getDropResult();
      onColumnDragEnd(item, monitor.didDrop() && typeof result?.name === 'string' ? result.name : null);
    },
    collect: (monitor) => ({ isDragging: monitor.isDragging() })
  }), [name, label, isVisible, isModifiable, index, onColumnDragEnd]);

  const [{ isOver }, connectDropTarget] = useDrop<TableOptionsDragItem, ColumnDropResult, { isOver: boolean }>(() => {
    const getDestination = (item: TableOptionsDragItem, monitor: DropTargetMonitor<TableOptionsDragItem, ColumnDropResult>) => {
      const node = dropTargetRef.current;
      const offset = monitor.getClientOffset();
      const row = node && Array.from(node.children).find((child) => !child.classList.contains(styles.columnPlaceholder));
      if (!row || !offset || !isModifiable) {
        return null;
      }

      return getColumnDropIndex(item.index, index, getDropPosition(row.getBoundingClientRect(), offset.y));
    };

    return {
      accept: TABLE_COLUMN,
      hover: (item, monitor) => {
        const destination = getDestination(item, monitor);
        if (destination !== null) {
          onColumnDragMove(item.index, destination);
        }
      },
      // The final destination comes from the actual drop, not React's last
      // rendered hover state (which may still be batched).
      drop: (item, monitor) => getDestination(item, monitor) === null ? undefined : { name },
      collect: (monitor) => ({ isOver: monitor.isOver() })
    };
  }, [index, isModifiable, name, onColumnDragMove]);

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
    <div
      ref={setDropTarget}
      className={styles.columnDragSource}
    >
      {
        isBefore &&
          <div className={classNames(styles.columnPlaceholder, styles.columnPlaceholderBefore)} />
      }

      <TableOptionsColumn
        name={name}
        label={label}
        isVisible={isVisible}
        isModifiable={isModifiable}
        index={index}
        isDragging={isDragging}
        dragHandleRef={setDragHandle}
        onVisibleChange={onVisibleChange}
      />

      {
        isAfter &&
          <div className={classNames(styles.columnPlaceholder, styles.columnPlaceholderAfter)} />
      }
    </div>
  );
}

export default TableOptionsColumnDragSource;
