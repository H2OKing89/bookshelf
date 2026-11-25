interface GridOffsets {
  getOffsetForCell(options: { rowIndex: number; columnIndex: number; alignment: 'start' }): { scrollTop: number };
}

type ScrollChild = (position: { scrollTop: number }) => void;

// In an auto-height Grid, WindowScroller owns the real vertical offset.
// Imperatively updating Grid's controlled scrollTop can be ignored or reset
// by the next render. Request the external scroll instead.
export function scrollVirtualizedWindow(onChildScroll: ScrollChild | null | undefined, scrollTop: number | null | undefined) {
  if (onChildScroll && typeof scrollTop === 'number' && Number.isFinite(scrollTop) && scrollTop >= 0) {
    onChildScroll({ scrollTop });
  }
}

export function scrollVirtualizedWindowToRow(grid: GridOffsets | null | undefined, onChildScroll: ScrollChild | null | undefined, rowIndex: number) {
  if (grid && onChildScroll && Number.isSafeInteger(rowIndex) && rowIndex >= 0) {
    const { scrollTop } = grid.getOffsetForCell({ rowIndex, columnIndex: 0, alignment: 'start' });
    scrollVirtualizedWindow(onChildScroll, scrollTop);
  }
}
