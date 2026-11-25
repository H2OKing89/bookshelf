import type { RefCallback } from 'react';

export interface QualityProfileDragItem {
  editGroups: boolean;
  qualityIndex: string;
  groupId?: number | null;
  qualityId?: number | null;
  isGroup: boolean;
  name: string;
  allowed: boolean;
}

export interface QualityProfileGroupItem {
  quality: { id: number; name: string };
}

export interface QualityProfileDragMove {
  dragQualityIndex: string;
  dropQualityIndex: string;
  dropPosition: 'above' | 'below';
}

export type QualityDragHandle = RefCallback<HTMLDivElement>;
export type QualityItemAllowedChange = (qualityId: number, allowed: boolean) => void;
export type QualityDragMove = (options: QualityProfileDragMove) => void;
export type QualityDragEnd = (move: QualityProfileDragMove | null) => void;


export function parseIndex(index: string): [number | null, number] {
  const split = index.split('.');

  return split.length === 1 ? [null, parseInt(split[0]) - 1] : [parseInt(split[0]) - 1, parseInt(split[1]) - 1];
}

// Preserve the existing quality-list insertion semantics. A null move means
// adjacent/no-op; the provisional hover and final drop use exactly this rule.
export function adjustQualityDropMove(move: QualityProfileDragMove): QualityProfileDragMove | null {
  const { dragQualityIndex, dropQualityIndex, dropPosition } = move;
  const [dragGroupIndex, dragItemIndex] = parseIndex(dragQualityIndex);
  const [dropGroupIndex, dropItemIndex] = parseIndex(dropQualityIndex);

  if ((dropPosition === 'below' && dropItemIndex - 1 === dragItemIndex) ||
      (dropPosition === 'above' && dropItemIndex + 1 === dragItemIndex)) {
    return null;
  }

  let adjustedDropQualityIndex = dropQualityIndex;
  if (dropPosition === 'above' && dragGroupIndex !== dropGroupIndex) {
    adjustedDropQualityIndex = dropGroupIndex == null ? `${dropItemIndex + 2}` : `${dropGroupIndex + 1}.${dropItemIndex + 2}`;
  }

  if (dropPosition === 'below' && dragGroupIndex === dropGroupIndex && dropGroupIndex != null && dragItemIndex < dropItemIndex) {
    adjustedDropQualityIndex = `${dropGroupIndex + 1}.${dropItemIndex}`;
  }

  if (dropPosition === 'below' && dragGroupIndex === dropGroupIndex && dropGroupIndex == null && dragItemIndex < dropItemIndex) {
    adjustedDropQualityIndex = `${dropItemIndex}`;
  }

  return { dragQualityIndex, dropQualityIndex: adjustedDropQualityIndex, dropPosition };
}
