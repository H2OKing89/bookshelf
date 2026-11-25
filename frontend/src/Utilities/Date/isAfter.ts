import moment from 'moment';
import { applyDateOffsets, type DateInput, type DateOffsets } from './dateTypes';

export default function isAfter(date: DateInput, offsets: DateOffsets = {}): boolean {
  return !!date && moment(date).isAfter(applyDateOffsets(moment(), offsets));
}
