import moment from 'moment';
import type { DateInput } from './dateTypes';

export default function formatDate(date: DateInput, dateFormat?: string): string {
  return date ? moment(date).format(dateFormat) : '';
}
