import classNames from 'classnames';
import React, { useCallback, useLayoutEffect, useRef } from 'react';
import { scrollDirections } from 'Helpers/Props';
import type { ScrollDirection } from 'Helpers/Props/scrollDirections';
import styles from './OverlayScroller.css';

interface ScrollOffset {
  scrollTop: number;
  scrollLeft: number;
}

interface OverlayScrollerProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'onScroll'> {
  scrollDirection?: ScrollDirection;
  autoScroll?: boolean;
  scrollTop?: number | null;
  registerScroller?: (element: HTMLDivElement | null) => void;
  onScroll?: (offset: ScrollOffset) => void;
}

const directionClasses: Record<ScrollDirection, string> = {
  none: styles.none,
  vertical: styles.vertical,
  horizontal: styles.horizontal,
  both: styles.both
};

// Browser-native scrolling retains the actual view element for measurements,
// keyboard/touch and scroll restoration without a second virtual scrollbar.
export default function OverlayScroller({
  className,
  scrollDirection = scrollDirections.VERTICAL,
  autoScroll = true,
  scrollTop,
  registerScroller,
  onScroll,
  children,
  ...otherProps
}: OverlayScrollerProps) {
  const view = useRef<HTMLDivElement | null>(null);
  const setView = useCallback((element: HTMLDivElement | null) => {
    view.current = element;
    registerScroller?.(element);
  }, [registerScroller]);

  useLayoutEffect(() => {
    if (view.current && scrollTop != null && Number.isFinite(scrollTop) && scrollTop !== view.current.scrollTop) {
      view.current.scrollTop = scrollTop;
    }
  }, [scrollTop]);

  const onViewScroll = useCallback((event: React.UIEvent<HTMLDivElement>) => {
    const { scrollTop: top, scrollLeft } = event.currentTarget;
    onScroll?.({ scrollTop: top, scrollLeft });
  }, [onScroll]);

  return (
    <div
      {...otherProps}
      ref={setView}
      className={classNames(className, styles.scroller, directionClasses[scrollDirection], autoScroll && styles.autoScroll)}
      onScroll={onViewScroll}
    >
      {children}
    </div>
  );
}
