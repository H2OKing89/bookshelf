import React from 'react';
import Scroller from 'Components/Scroller/Scroller';
import styles from './MenuContent.css';

interface MenuContentProps extends React.HTMLAttributes<HTMLDivElement> {
  forwardedRef?: React.Ref<HTMLDivElement>;
  children: React.ReactNode;
}

function MenuContent({
  forwardedRef,
  className = styles.menuContent,
  children,
  ...otherProps
}: MenuContentProps) {
  return (
    <div
      ref={forwardedRef}
      className={className}
      role="menu"
      tabIndex={-1}
      {...otherProps}
    >
      <Scroller className={styles.scroller} autoFocus={false}>
        {children}
      </Scroller>
    </div>
  );
}

export default MenuContent;
