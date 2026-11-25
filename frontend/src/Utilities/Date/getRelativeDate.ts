import moment from 'moment';
import formatTime from 'Utilities/Date/formatTime';
import isInNextWeek from 'Utilities/Date/isInNextWeek';
import isToday from 'Utilities/Date/isToday';
import isTomorrow from 'Utilities/Date/isTomorrow';
import isYesterday from 'Utilities/Date/isYesterday';
import type { DateInput } from './dateTypes';

interface RelativeDateOptions {
  timeFormat?: string;
  includeSeconds?: boolean;
  timeForToday?: boolean;
}

function getRelativeDate(date: DateInput, shortDateFormat: string, showRelativeDates: boolean, { timeFormat, includeSeconds = false, timeForToday = false }: RelativeDateOptions = {}): string | null {
  if (!date) {
    return null;
  }

  const isTodayDate = isToday(date);

  if (isTodayDate && timeForToday && timeFormat) {
    return formatTime(date, timeFormat, { includeMinuteZero: true, includeSeconds });
  }

  if (!showRelativeDates) {
    return moment(date).format(shortDateFormat);
  }

  if (isYesterday(date)) {
    return 'Yesterday';
  }

  if (isTodayDate) {
    return 'Today';
  }

  if (isTomorrow(date)) {
    return 'Tomorrow';
  }

  if (isInNextWeek(date)) {
    return moment(date).format('dddd');
  }

  return moment(date).format(shortDateFormat);
}

export default getRelativeDate;
