import moment from 'moment';
import type { DateInput } from './dateTypes';

function isTomorrow(date: DateInput): boolean {
  if (!date) {
    return false;
  }

  return moment(date).isSame(moment().add(1, 'day'), 'day');
}

export default isTomorrow;
