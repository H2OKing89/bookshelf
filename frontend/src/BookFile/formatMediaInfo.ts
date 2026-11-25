import type { AudioStreamInfo, MediaInfoModel } from 'Book/LibraryModels';

function present(value: unknown): value is string {
  return typeof value === 'string' && value !== '';
}

function formatDuration(seconds: number | null | undefined) {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) {
    return '';
  }

  const total = Math.floor(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor(total / 60) % 60;
  const remainder = total % 60;
  return `${hours}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
}

export function getMediaInfoSource(mediaInfo: MediaInfoModel = {}) {
  if (mediaInfo.source === 'MyAnonaMouse') {
    return 'Reported by MAM';
  }

  if (mediaInfo.source !== 'MediaInfo' && mediaInfo.source !== 'TagLib') {
    return '';
  }

  const tool = mediaInfo.source === 'MediaInfo' && mediaInfo.sourceVersion ?
    `MediaInfo ${mediaInfo.sourceVersion}` : mediaInfo.source;

  if (mediaInfo.analysisStatus === 'stale') {
    return `Saved file analysis · ${tool} · Refresh needed`;
  }

  if (mediaInfo.analysisStatus === 'partial') {
    return `Partial file analysis · ${tool}`;
  }

  return `Read from file · ${tool}`;
}

export function getMediaInfoDetails(stream: AudioStreamInfo = {}) {
  let defaultStream = '';
  if (typeof stream.isDefault === 'boolean') {
    defaultStream = stream.isDefault ? 'Yes' : 'No';
  }

  const fields: [string, string | null | undefined][] = [
    ['Format', stream.audioFormat],
    ['Profile', stream.audioProfile],
    ['Features', stream.audioAdditionalFeatures],
    ['Commercial name', stream.audioCommercialName],
    ['Codec ID', stream.audioCodecId],
    ['Bitrate mode', stream.audioBitrateMode],
    ['Channel layout', stream.audioChannelLayout],
    ['Language', stream.language],
    ['Title', stream.title],
    ['Duration', formatDuration(stream.durationSeconds)],
    ['Default stream', defaultStream]
  ];
  return fields.filter((entry): entry is [string, string] => present(entry[1]));
}

function formatMediaInfo(mediaInfo: AudioStreamInfo = {}) {
  const codec = mediaInfo.audioCodec === 'Unknown' ? mediaInfo.audioFormat : mediaInfo.audioCodec;
  const channels = mediaInfo.audioChannels != null && mediaInfo.audioChannels > 0 ? `${mediaInfo.audioChannels} ${mediaInfo.audioChannels === 1 ? 'channel' : 'channels'}` : '';
  const bitrate = mediaInfo.audioBitRate && mediaInfo.audioBitRate !== '0 kbps' ?
    `${mediaInfo.audioBitRate}${mediaInfo.audioBitrateMode ? ` (${mediaInfo.audioBitrateMode})` : ''}` : '';
  const sampleRate = mediaInfo.audioSampleRate === '0kHz' ? '' : mediaInfo.audioSampleRate;
  return [codec, channels, bitrate, sampleRate, mediaInfo.audioBits].filter(present).join(' · ');
}

export default formatMediaInfo;
