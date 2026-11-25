import moment from 'moment';
import type { DateInput } from './dateTypes';

function isYesterday(date: DateInput): boolean {
  if (!date) {
    return false;
  }

  return moment(date).isSame(moment().subtract(1, 'day'), 'day');
}

export default isYesterday;
