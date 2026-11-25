import moment, { type DurationInputArg1 } from 'moment';

function formatShortTimeSpan(timeSpan: DurationInputArg1 | null): string {
  if (!timeSpan) {
    return '';
  }

  const duration = moment.duration(timeSpan);

  const hours = Math.floor(duration.asHours());
  const minutes = Math.floor(duration.asMinutes());
  const seconds = Math.floor(duration.asSeconds());

  if (hours > 0) {
    return `${hours} hour(s)`;
  }

  if (minutes > 0) {
    return `${minutes} minute(s)`;
  }

  return `${seconds} second(s)`;
}

export default formatShortTimeSpan;
