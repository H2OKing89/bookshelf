import {
  arrow,
  autoUpdate,
  flip,
  offset,
  type Placement,
  shift,
  size,
  type Strategy,
  useFloating
} from '@floating-ui/react-dom';
import React, { useCallback, useEffect, useRef } from 'react';

interface PopupPosition {
  ref: (element: HTMLElement | null) => void;
  style: React.CSSProperties;
  placement: Placement;
  arrowRef: React.RefObject<HTMLDivElement>;
  arrowStyle: React.CSSProperties;
}

interface AnchoredPopupProps {
  reference: HTMLElement | null;
  placement: Placement;
  open?: boolean;
  strategy?: Strategy;
  canFlip?: boolean;
  gutter?: number;
  padding?: number;
  flipPadding?: number;
  fit?: boolean;
  matchWidth?: 'exact' | 'minimum';
  maxHeight?: number;
  maxWidth?: number;
  showArrow?: boolean;
  onFloatingChange?: (element: HTMLElement | null) => void;
  onPositioned?: () => void;
  children: (position: PopupPosition) => React.ReactNode;
}

export const popupMaxHeight = 'var(--popup-available-height)';
export const popupMaxWidth = 'var(--popup-available-width)';

// Keep interaction and focus ownership with the caller. Floating UI handles
// positioning only; the existing menu, select and tooltip controls retain
// their own open/close behavior and portal locations.
function AnchoredPopup({
  reference,
  placement,
  open = true,
  strategy = 'absolute',
  canFlip = true,
  gutter = 0,
  padding = 8,
  flipPadding = padding,
  fit = false,
  matchWidth,
  maxHeight,
  maxWidth,
  showArrow = false,
  onFloatingChange,
  onPositioned,
  children
}: AnchoredPopupProps) {
  const arrowRef = useRef<HTMLDivElement>(null);
  const geometry = useFloating({
    open,
    placement,
    strategy,
    elements: { reference },
    middleware: [
      offset(gutter),
      canFlip && flip({ padding: flipPadding }),
      shift({ padding }),
      (fit || matchWidth !== undefined) && size({
        padding,
        apply({ availableWidth, availableHeight, elements, rects }) {
          const width = Math.max(0, Math.min(maxWidth ?? Infinity, availableWidth));
          const height = Math.max(0, Math.min(maxHeight ?? Infinity, availableHeight));
          const style = elements.floating.style;

          // The popup's scrollable child also needs the available height.
          style.setProperty('--popup-available-height', `${height}px`);
          style.setProperty('--popup-available-width', `${width}px`);

          if (matchWidth) {
            style[matchWidth === 'exact' ? 'width' : 'minWidth'] =
              `${Math.min(width, rects.reference.width)}px`;
          }
        }
      }, [padding, matchWidth, maxHeight, maxWidth]),
      showArrow && arrow({ element: arrowRef, padding: 2 })
    ]
  });

  const { floating, reference: anchor } = geometry.elements;
  const { setFloating } = geometry.refs;
  const { update, isPositioned } = geometry;
  const ref = useCallback((element: HTMLElement | null) => {
    setFloating(element);
    onFloatingChange?.(element);
  }, [setFloating, onFloatingChange]);

  // Never subscribe closed/hidden popups (e.g. many tooltip cells in a
  // virtualized table). Floating UI removes its observers and listeners.
  useEffect(() => {
    if (open && anchor && floating) {
      return autoUpdate(anchor, floating, update);
    }

    return undefined;
  }, [anchor, floating, open, update]);

  useEffect(() => {
    if (open && isPositioned) {
      onPositioned?.();
    }
  }, [isPositioned, onPositioned, open]);

  const arrowData = geometry.middlewareData.arrow;

  return (
    <>
      {children({
        ref,
        style: {
          ...geometry.floatingStyles,
          maxHeight: fit ? popupMaxHeight : undefined,
          maxWidth: fit ? popupMaxWidth : undefined,
          visibility: open && !isPositioned ? 'hidden' : undefined
        },
        placement: geometry.placement,
        arrowRef,
        arrowStyle: {
          left: arrowData?.x,
          top: arrowData?.y
        }
      })}
    </>
  );
}

export default AnchoredPopup;
