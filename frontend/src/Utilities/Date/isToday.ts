import moment from 'moment';
import type { DateInput } from './dateTypes';

function isToday(date: DateInput): boolean {
  if (!date) {
    return false;
  }

  return moment(date).isSame(moment(), 'day');
}

export default isToday;
