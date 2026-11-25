import moment from 'moment';
import type { DateInput } from './dateTypes';

function isInNextWeek(date: DateInput): boolean {
  if (!date) {
    return false;
  }
  const now = moment();
  return moment(date).isBetween(now, now.clone().add(6, 'days').endOf('day'));
}

export default isInNextWeek;
