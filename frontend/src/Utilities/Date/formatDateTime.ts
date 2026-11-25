import moment from 'moment';
import translate from 'Utilities/String/translate';
import type { DateInput } from './dateTypes';
import formatTime from './formatTime';
import isToday from './isToday';
import isTomorrow from './isTomorrow';
import isYesterday from './isYesterday';

function getRelativeDay(date: DateInput, includeRelativeDate: boolean): string {
  if (!includeRelativeDate) {
    return '';
  }

  if (isYesterday(date)) {
    return translate('Yesterday');
  }

  if (isToday(date)) {
    return translate('Today');
  }

  if (isTomorrow(date)) {
    return translate('Tomorrow');
  }

  return '';
}

interface DateTimeFormatOptions {
  includeSeconds?: boolean;
  includeRelativeDay?: boolean;
}

function formatDateTime(date: DateInput, dateFormat: string, timeFormat: string, { includeSeconds = false, includeRelativeDay = false }: DateTimeFormatOptions = {}): string {
  if (!date) {
    return '';
  }

  const relativeDay = getRelativeDay(date, includeRelativeDay);
  const formattedDate = moment(date).format(dateFormat);
  const formattedTime = formatTime(date, timeFormat, { includeMinuteZero: true, includeSeconds });

  return `${relativeDay}${formattedDate} ${formattedTime}`;
}

export default formatDateTime;
