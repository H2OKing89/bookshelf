import moment, { type unitOfTime } from 'moment';
import * as filterTypes from 'Helpers/Props/filterTypes';
import { type DateInput, isDateOffsetUnit } from './dateTypes';
import isAfter from './isAfter';
import isBefore from './isBefore';

interface RelativeFilter {
  time: unitOfTime.DurationConstructor;
  value: number;
}

function absoluteDate(value: unknown): DateInput {
  return typeof value === 'string' || typeof value === 'number' || value instanceof Date || moment.isMoment(value) ? value : undefined;
}

function relativeFilter(value: unknown): RelativeFilter | undefined {
  if (typeof value !== 'object' || value === null || !('time' in value) || !('value' in value) || !isDateOffsetUnit(value.time)) {
    return undefined;
  }
  // Saved filter input may still arrive as a numeric string from a legacy form.
  let quantity = NaN;
  if (typeof value.value === 'number') {
    quantity = value.value;
  } else if (typeof value.value === 'string' && value.value.trim()) {
    quantity = Number(value.value);
  }
  return Number.isFinite(quantity) ? { time: value.time, value: quantity } : undefined;
}

export default function dateFilterPredicate(itemValue: DateInput, filterValue: unknown, type: string): boolean {
  if (!itemValue) {
    return false;
  }

  const absolute = absoluteDate(filterValue);
  const relative = relativeFilter(filterValue);
  switch (type) {
    case filterTypes.LESS_THAN:
      return absolute != null && moment(itemValue).isBefore(absolute);
    case filterTypes.GREATER_THAN:
      return absolute != null && moment(itemValue).isAfter(absolute);
    case filterTypes.IN_LAST:
      return relative != null && isAfter(itemValue, { [relative.time]: -relative.value }) && isBefore(itemValue);
    case filterTypes.NOT_IN_LAST:
      return relative != null && isBefore(itemValue, { [relative.time]: -relative.value });
    case filterTypes.IN_NEXT:
      return relative != null && isAfter(itemValue) && isBefore(itemValue, { [relative.time]: relative.value });
    case filterTypes.NOT_IN_NEXT:
      return relative != null && isAfter(itemValue, { [relative.time]: relative.value });
    default:
      return false;
  }
}
