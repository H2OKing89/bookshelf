import moment from 'moment';
import type { DateInput, TimeFormatOptions } from './dateTypes';

export default function formatTime(date: DateInput, timeFormat: string, { includeMinuteZero = false, includeSeconds = false }: TimeFormatOptions = {}): string {
  if (!date) {
    return '';
  }

  const time = moment(date);

  if (includeSeconds) {
    timeFormat = timeFormat.replace(/\(?:mm\)?/, ':mm:ss');
  } else if (includeMinuteZero || time.minute() !== 0) {
    timeFormat = timeFormat.replace('(:mm)', ':mm');
  } else {
    timeFormat = timeFormat.replace('(:mm)', '');
  }

  return time.format(timeFormat);
}
