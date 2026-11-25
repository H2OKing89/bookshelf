interface VerticalBounds {
  top: number;
  bottom: number;
}

export function getDropPosition(bounds: VerticalBounds, pointerY: number): 'above' | 'below' | null {
  const middle = (bounds.top + bounds.bottom) / 2;

  if (pointerY < middle) {
    return 'above';
  }

  if (pointerY > middle) {
    return 'below';
  }

  return null;
}

// Keep table-column and delay-profile index contracts distinct: columns use
// zero-based destination indices; delay profiles use one-based insertion order.
export function getColumnDropIndex(dragIndex: number, hoverIndex: number, position: ReturnType<typeof getDropPosition>): number | null {
  if ((dragIndex < hoverIndex && position !== 'above') ||
      (dragIndex > hoverIndex && position !== 'below')) {
    return hoverIndex;
  }

  return null;
}

export function getDelayProfileDropOrder(dragOrder: number, hoverOrder: number, position: ReturnType<typeof getDropPosition>): number | null {
  if (dragOrder < hoverOrder && position === 'below') {
    return hoverOrder + 1;
  }

  if (dragOrder > hoverOrder && position === 'above') {
    return hoverOrder;
  }

  return null;
}
