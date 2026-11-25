import moment from 'moment';
import { applyDateOffsets, type DateInput, type DateOffsets } from './dateTypes';

export default function isBefore(date: DateInput, offsets: DateOffsets = {}): boolean {
  return !!date && moment(date).isBefore(applyDateOffsets(moment(), offsets));
}
