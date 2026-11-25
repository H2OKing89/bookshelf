export const LEFT = 'left';
export const CENTER = 'center';
export const RIGHT = 'right';

export const all = [LEFT, CENTER, RIGHT] as const;

export type Alignment = typeof all[number];
