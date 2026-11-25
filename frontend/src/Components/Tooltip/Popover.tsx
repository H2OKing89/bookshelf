import React from 'react';
import Tooltip, { type TooltipProps } from './Tooltip';
import styles from './Popover.css';

interface PopoverProps extends Omit<TooltipProps, 'tooltip'> {
  title: React.ReactNode;
  body: React.ReactNode;
}

function Popover({
  title,
  body,
  ...otherProps
}: PopoverProps) {
  return (
    <Tooltip
      {...otherProps}
      bodyClassName={styles.tooltipBody}
      tooltip={
        <div>
          <div className={styles.title}>{title}</div>
          <div className={styles.body}>{body}</div>
        </div>
      }
    />
  );
}

export default Popover;
