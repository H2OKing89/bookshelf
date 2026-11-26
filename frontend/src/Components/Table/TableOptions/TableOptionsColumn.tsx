import classNames from 'classnames';
import { type RefCallback } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import styles from './TableOptionsColumn.css';

export interface TableOptionsDragItem {
  name: string;
  label: string | (() => string);
  isVisible: boolean;
  isModifiable: boolean;
  index: number;
}

export interface TableOptionsColumnProps extends TableOptionsDragItem {
  isDragging?: boolean;
  dragHandleRef?: RefCallback<HTMLDivElement>;
  onVisibleChange?: (change: { name: string; value: boolean }) => void;
}

function TableOptionsColumn(props: TableOptionsColumnProps) {
  const {
    name,
    label,
    isVisible,
    isModifiable,
    isDragging,
    dragHandleRef,
    onVisibleChange
  } = props;

  return (
    <div className={isModifiable ? undefined : styles.notDragable}>
      <div
        className={classNames(
          styles.column,
          isDragging && styles.isDragging
        )}
      >
        <label
          className={styles.label}
        >
          <CheckInput
            containerClassName={styles.checkContainer}
            name={name}
            value={isVisible}
            isDisabled={!isModifiable || !onVisibleChange}
            onChange={(change: { name: string; value: boolean }) => onVisibleChange?.(change)}
          />
          {typeof label === 'function' ? label() : label}
        </label>

        {
          isModifiable &&
            <div ref={dragHandleRef} className={styles.dragHandle}>
              <Icon
                className={styles.dragIcon}
                name={icons.REORDER}
              />
            </div>
        }
      </div>
    </div>
  );
}

export default TableOptionsColumn;
