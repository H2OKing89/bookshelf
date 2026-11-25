import type { Moment, MomentInput, unitOfTime } from 'moment';

export type DateInput = MomentInput | null;
export type DateOffsets = Partial<Record<unitOfTime.DurationConstructor, number>>;

export interface TimeFormatOptions {
  includeMinuteZero?: boolean;
  includeSeconds?: boolean;
}

const offsetUnits: ReadonlySet<string> = new Set<unitOfTime.DurationConstructor>([
  'year', 'years', 'y', 'month', 'months', 'M', 'week', 'weeks', 'w',
  'day', 'days', 'd', 'hour', 'hours', 'h', 'minute', 'minutes', 'm',
  'second', 'seconds', 's', 'millisecond', 'milliseconds', 'ms',
  'quarter', 'quarters', 'Q'
]);

export function isDateOffsetUnit(value: unknown): value is unitOfTime.DurationConstructor {
  return typeof value === 'string' && offsetUnits.has(value);
}

// Apply in insertion order, as the existing calendar filters did. Calendar
// months/days are not interchangeable fixed-duration milliseconds.
export function applyDateOffsets(date: Moment, offsets: DateOffsets): Moment {
  for (const [unit, value] of Object.entries(offsets)) {
    if (isDateOffsetUnit(unit) && typeof value === 'number' && Number.isFinite(value)) {
      date.add(value, unit);
    }
  }
  return date;
}
