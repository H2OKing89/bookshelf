export const ASCENDING = 'ascending';
export const DESCENDING = 'descending';

export const all = [ASCENDING, DESCENDING] as const;

export type SortDirection = typeof all[number];
