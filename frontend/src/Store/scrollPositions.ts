const scrollPositions = {
  authorIndex: 0,
  bookIndex: 0
};

export type ScrollPositionKey = keyof typeof scrollPositions;

export function getScrollPosition(key: ScrollPositionKey): number {
  return scrollPositions[key];
}

export function saveScrollPosition(key: ScrollPositionKey, value: number) {
  if (Number.isFinite(value) && value >= 0) {
    scrollPositions[key] = value;
  }
}
