export default function formatAge(age: number, ageHours: number | string | null | undefined, ageMinutes: number | string | null | undefined): string {
  const roundedAge = Math.round(age);
  const hours = parseFloat(String(ageHours));
  const minutes = ageMinutes && parseFloat(String(ageMinutes));
  if (roundedAge < 2 && hours) {
    if (hours < 2 && minutes) {
      return `${minutes.toFixed(0)} ${hours === 1 ? 'minute' : 'minutes'}`;
    }
    return `${hours.toFixed(1)} ${hours === 1 ? 'hour' : 'hours'}`;
  }
  return `${roundedAge} ${roundedAge === 1 ? 'day' : 'days'}`;
}
