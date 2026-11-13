function present(value) {
  return value !== null && value !== undefined && value !== '';
}

function formatDuration(seconds) {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) {
    return '';
  }

  const total = Math.floor(seconds);
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor(total / 60) % 60;
  const remainder = total % 60;
  return `${hours}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
}

export function getMediaInfoSource(mediaInfo = {}) {
  if (mediaInfo.source === 'MyAnonaMouse') {
    return 'Reported by MAM';
  }

  if (mediaInfo.source === 'MediaInfo') {
    return `Read from file · MediaInfo${mediaInfo.sourceVersion ? ` ${mediaInfo.sourceVersion}` : ''}`;
  }

  if (mediaInfo.source === 'TagLib') {
    return 'Read from file · TagLib';
  }

  return '';
}

export function getMediaInfoDetails(stream = {}) {
  let defaultStream = '';
  if (typeof stream.isDefault === 'boolean') {
    defaultStream = stream.isDefault ? 'Yes' : 'No';
  }

  return [
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
  ].filter(([, value]) => present(value));
}

function formatMediaInfo(mediaInfo = {}) {
  const codec = mediaInfo.audioCodec === 'Unknown' ? mediaInfo.audioFormat : mediaInfo.audioCodec;
  const channels = mediaInfo.audioChannels > 0 ? `${mediaInfo.audioChannels} ${mediaInfo.audioChannels === 1 ? 'channel' : 'channels'}` : '';
  const bitrate = mediaInfo.audioBitRate && mediaInfo.audioBitRate !== '0 kbps' ?
    `${mediaInfo.audioBitRate}${mediaInfo.audioBitrateMode ? ` (${mediaInfo.audioBitrateMode})` : ''}` : '';
  const sampleRate = mediaInfo.audioSampleRate === '0kHz' ? '' : mediaInfo.audioSampleRate;
  return [codec, channels, bitrate, sampleRate, mediaInfo.audioBits].filter(present).join(' · ');
}

export default formatMediaInfo;
