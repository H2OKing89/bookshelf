import moment from 'moment';
import type { DateInput } from './dateTypes';

function isSameWeek(date: DateInput): boolean {
  if (!date) {
    return false;
  }

  return moment(date).isSame(moment(), 'week');
}

export default isSameWeek;
