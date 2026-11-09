export default function formatChapterTime(milliseconds) {
  if (!Number.isFinite(milliseconds) || milliseconds < 0) {
    return '';
  }

  const seconds = Math.floor(milliseconds / 1000);
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}
