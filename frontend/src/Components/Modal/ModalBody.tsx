import React from 'react';
import Scroller, { type ScrollerProps } from 'Components/Scroller/Scroller';
import { scrollDirections } from 'Helpers/Props';
import styles from './ModalBody.css';

export interface ModalBodyProps extends ScrollerProps {
  innerClassName?: string;
}

export default function ModalBody({
  className, innerClassName = styles.innerModalBody,
  scrollDirection = scrollDirections.VERTICAL, children, ...otherProps
}: ModalBodyProps) {
  const hasScroller = scrollDirection !== scrollDirections.NONE;
  return (
    <Scroller
      className={className || (hasScroller ? styles.modalScroller : styles.modalBody)}
      scrollDirection={scrollDirection}
      scrollTop={0}
      {...otherProps}
    >
      {hasScroller ? <div className={innerClassName}>{children}</div> : children}
    </Scroller>
  );
}
