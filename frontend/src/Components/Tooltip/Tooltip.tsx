import type { Side } from '@floating-ui/react-dom';
import classNames from 'classnames';
import React, { useEffect, useRef, useState } from 'react';
import Portal from 'Components/Portal';
import AnchoredPopup, { popupMaxHeight } from 'Components/Positioning/AnchoredPopup';
import { kinds, tooltipPositions } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import { isMobile as isMobileUtil } from 'Utilities/browser';
import styles from './Tooltip.css';

function getMaxWidth() {
  const windowWidth = window.innerWidth;

  if (windowWidth >= parseInt(dimensions.breakpointLarge)) {
    return 800;
  }

  if (windowWidth >= parseInt(dimensions.breakpointMedium)) {
    return 650;
  }

  if (windowWidth >= parseInt(dimensions.breakpointSmall)) {
    return 500;
  }

  return 450;
}

export interface TooltipProps {
  className?: string;
  bodyClassName?: string;
  anchor: React.ReactNode;
  tooltip: React.ReactNode;
  kind?: typeof kinds.DEFAULT | typeof kinds.INVERSE;
  position?: Side;
  canFlip?: boolean;
}

function Tooltip({
  className,
  bodyClassName = styles.body,
  anchor,
  tooltip,
  kind = kinds.DEFAULT,
  position = tooltipPositions.TOP,
  canFlip = false
}: TooltipProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [reference, setReference] = useState<HTMLSpanElement | null>(null);
  const closeTimeout = useRef<ReturnType<typeof setTimeout>>();

  const clearCloseTimeout = () => {
    clearTimeout(closeTimeout.current);
    closeTimeout.current = undefined;
  };

  useEffect(() => () => clearTimeout(closeTimeout.current), []);

  const open = () => {
    clearCloseTimeout();
    setIsOpen(true);
  };

  const close = () => {
    clearCloseTimeout();
    closeTimeout.current = setTimeout(() => setIsOpen(false), 100);
  };

  const onClick = () => {
    if (isMobileUtil()) {
      clearCloseTimeout();
      setIsOpen((previous) => !previous);
    }
  };

  const onMouseEnter = () => {
    if (!isMobileUtil()) {
      open();
    }
  };

  const onMouseLeave = () => {
    if (!isMobileUtil()) {
      close();
    }
  };

  return (
    <>
      <span
        ref={setReference}
        className={className}
        onClick={onClick}
        onMouseEnter={onMouseEnter}
        onMouseLeave={onMouseLeave}
        onFocus={onMouseEnter}
        onBlur={onMouseLeave}
      >
        {anchor}
      </span>

      {isOpen &&
        <Portal>
          <AnchoredPopup
            reference={reference}
            placement={position}
            canFlip={canFlip}
            gutter={10}
            padding={10}
            fit={true}
            maxWidth={getMaxWidth()}
            maxHeight={position === 'top' || position === 'bottom' ? 300 : undefined}
            showArrow={true}
          >
            {({ ref, style, placement, arrowRef, arrowStyle }) => {
              // Tooltips use only physical side placements, including flips.
              const side = placement === 'top' || placement === 'right' ||
                placement === 'bottom' || placement === 'left' ? placement : position;
              const vertical = side === 'top' || side === 'bottom';

              return (
                <div
                  ref={ref}
                  className={classNames(
                    styles.tooltipContainer,
                    vertical ? styles.verticalContainer : styles.horizontalContainer
                  )}
                  style={style}
                  onMouseEnter={onMouseEnter}
                  onMouseLeave={onMouseLeave}
                  onFocus={onMouseEnter}
                  onBlur={onMouseLeave}
                >
                  <div
                    className={classNames(styles.arrow, styles[kind], styles[side])}
                    ref={arrowRef}
                    style={arrowStyle}
                  />
                  <div
                    className={classNames(styles.tooltip, styles[kind])}
                    style={{ maxHeight: popupMaxHeight }}
                  >
                    <div className={bodyClassName}>{tooltip}</div>
                  </div>
                </div>
              );
            }}
          </AnchoredPopup>
        </Portal>}
    </>
  );
}

export default Tooltip;
