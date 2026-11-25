import React from 'react';
import { type MediaInfoModel, parseMediaInfo } from 'Book/LibraryModels';
import formatMediaInfo, { getMediaInfoDetails, getMediaInfoSource } from './formatMediaInfo';
import * as mediaInfoTypes from './mediaInfoTypes';
import styles from './MediaInfo.css';

interface MediaInfoProps extends MediaInfoModel {
  type?: string | null;
}

function MediaInfo(props: MediaInfoProps) {
  if (props.type !== mediaInfoTypes.AUDIO) {
    return null;
  }

  // Releases and older JS connectors may supply media details before the
  // library file boundary has parsed them. Do not render malformed evidence.
  let mediaInfo: MediaInfoModel | null | undefined = undefined;
  try {
    mediaInfo = parseMediaInfo(props);
  } catch {
    return null;
  }
  if (!mediaInfo) {
    return null;
  }
  const { containerFormat } = mediaInfo;
  const audioStreams = mediaInfo.audioStreams ?? [];
  const summary = formatMediaInfo(mediaInfo);
  const streams = audioStreams.length ? audioStreams : [mediaInfo];
  const source = getMediaInfoSource(mediaInfo);
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

export default MediaInfo;
