import PropTypes from 'prop-types';
import React from 'react';
import formatMediaInfo, { getMediaInfoDetails, getMediaInfoSource } from './formatMediaInfo';
import * as mediaInfoTypes from './mediaInfoTypes';
import styles from './MediaInfo.css';

function MediaInfo(props) {
  const { type, audioStreams = [], containerFormat } = props;
  if (type !== mediaInfoTypes.AUDIO) {
    return null;
  }

  const summary = formatMediaInfo(props);
  const streams = audioStreams.length ? audioStreams : [props];
  const source = getMediaInfoSource(props);
  const hasDetails = streams.some(stream => getMediaInfoDetails(stream).length);
  if (!summary && !hasDetails && !containerFormat) {
    return null;
  }

  return (
    <div className={styles.mediaInfo}>
      <details className={styles.details}>
        <summary className={styles.summary}>
          {summary || 'Audio details'}
          {audioStreams.length > 1 ? ` · ${audioStreams.length} audio streams` : ''}
        </summary>
        {containerFormat && <div className={styles.container}>Container: {containerFormat}</div>}
        {
          streams.map((stream, index) => (
            <div key={stream.index ?? index} className={styles.stream}>
              {
                audioStreams.length > 1 &&
                  <div className={styles.streamHeading}>
                    Audio stream {index + 1}{stream.isDefault ? ' (default)' : ''}
                  </div>
              }
              {audioStreams.length > 1 && <div>{formatMediaInfo(stream)}</div>}
              <dl className={styles.fields}>
                {getMediaInfoDetails(stream).map(([label, value]) => (
                  <div key={label} className={styles.field}>
                    <dt>{label}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ))
        }
      </details>
      {source && <div className={styles.source}>{source}</div>}
    </div>
  );
}

MediaInfo.propTypes = {
  type: PropTypes.string.isRequired,
  audioChannels: PropTypes.number,
  audioCodec: PropTypes.string,
  audioBitRate: PropTypes.string,
  audioBits: PropTypes.string,
  audioSampleRate: PropTypes.string,
  audioStreams: PropTypes.arrayOf(PropTypes.object),
  containerFormat: PropTypes.string
};

export default MediaInfo;
