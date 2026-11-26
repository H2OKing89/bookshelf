import _ from 'lodash';
import { Fragment } from 'react';
import type { AudioTags, ReleaseGroupEvidence } from 'Book/LibraryModels';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import DescriptionListItemDescription from 'Components/DescriptionList/DescriptionListItemDescription';
import DescriptionListItemTitle from 'Components/DescriptionList/DescriptionListItemTitle';
import Link from 'Components/Link/Link';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import styles from './FileDetails.css';

const releaseGroupSourceLabels = new Map<string, string>([
  ['manual', 'Explicit manual choice'],
  ['download', 'Download label'],
  ['folder', 'Folder label'],
  ['file', 'File label'],
  ['recordingFile', 'Exact recording file label'],
  ['recordingFolder', 'Exact recording folder label'],
  ['packUploader', 'Pack uploader'],
  ['unknown', 'Unknown']
]);

const releaseGroupReviewLabels = new Map<string, string>([
  ['conflictingRecordingLabels', 'File and recording folder labels conflict; no recording-level group was assumed.'],
  ['noRecordingLabel', 'No identified recording label was available.'],
  ['unverifiedPackRecording', 'This file could not be matched to an exact recording in the pack; ordinary precedence was retained.']
]);

function releaseGroupDetails(evidence: ReleaseGroupEvidence) {

  const labels: [string, string | null | undefined][] = [
    ['Group source', releaseGroupSourceLabels.get(evidence.source ?? '')],
    ['Group review', releaseGroupReviewLabels.get(evidence.reason ?? '')],
    ['Pack uploader', evidence.packUploaderGroup],
    ['Download label', evidence.downloadGroup],
    ['Folder label', evidence.folderGroup],
    ['Recording folder label', evidence.recordingFolderGroup],
    ['File label', evidence.fileGroup],
    ['Exact recording identity', evidence.recordingId]
  ];

  return labels.filter(([, value]) => value).map(([label, value]) => (
    <DescriptionListItem key={label} title={label} data={value} />
  ));
}

interface FileRejection {
  reason?: string | null;
}

function renderRejections(rejections: readonly FileRejection[]) {
  return (
    <span>
      <DescriptionListItemTitle>
        Rejections
      </DescriptionListItemTitle>
      {
        _.map(rejections, (item, key) => {
          return (
            <DescriptionListItemDescription key={key}>
              {item.reason}
            </DescriptionListItemDescription>
          );
        })
      }
    </span>
  );
}

interface FileDetailsProps {
  filename?: string | null;
  releaseGroup?: string | null;
  releaseGroupEvidence?: ReleaseGroupEvidence | null;
  audioTags?: AudioTags | null;
  rejections?: readonly FileRejection[] | null;
}

function FileDetails(props: FileDetailsProps) {

  const {
    filename,
    releaseGroup,
    releaseGroupEvidence,
    audioTags: tags,
    rejections
  } = props;
  const audioTags = tags ?? {};
  const trackNumber = audioTags.trackNumbers?.[0];

  return (
    <Fragment>
      <div className={styles.audioTags}>
        <DescriptionList>
          {
            filename &&
              <DescriptionListItem
                title={translate('Filename')}
                data={filename}
                descriptionClassName={styles.filename}
              />
          }
          {
            (releaseGroup || releaseGroupEvidence) &&
              <DescriptionListItem
                title={translate('ReleaseGroup')}
                data={releaseGroup || '—'}
              />
          }
          {releaseGroupEvidence && releaseGroupDetails(releaseGroupEvidence)}
          {
            audioTags.title != null &&
              <DescriptionListItem
                title={translate('TrackTitle')}
                data={audioTags.title}
              />
          }
          {
            (trackNumber ?? 0) > 0 &&
              <DescriptionListItem
                title={translate('TrackNumber')}
                data={trackNumber}
              />
          }
          {
            (audioTags.discNumber ?? 0) > 0 &&
              <DescriptionListItem
                title={translate('DiscNumber')}
                data={audioTags.discNumber}
              />
          }
          {
            (audioTags.discCount ?? 0) > 0 &&
              <DescriptionListItem
                title={translate('DiscCount')}
                data={audioTags.discCount}
              />
          }
          {
            audioTags.bookTitle != null &&
              <DescriptionListItem
                title={translate('Book')}
                data={audioTags.bookTitle}
              />
          }
          {
            audioTags.authorTitle != null &&
              <DescriptionListItem
                title={translate('Author')}
                data={audioTags.authorTitle}
              />
          }
          {
            audioTags.seriesTitle != null &&
              <DescriptionListItem
                title={translate('Series')}
                data={audioTags.seriesTitle}
              />
          }
          {
            audioTags.seriesIndex != null &&
              <DescriptionListItem
                title={translate('SeriesNumber')}
                data={audioTags.seriesIndex}
              />
          }
          {
            audioTags.country != null &&
              <DescriptionListItem
                title={translate('Country')}
                data={audioTags.country.name}
              />
          }
          {
            audioTags.language != null && audioTags.language !== 'UND' &&
              <DescriptionListItem
                title={translate('Language')}
                data={audioTags.language}
              />
          }
          {
            (audioTags.year ?? 0) > 0 &&
              <DescriptionListItem
                title={translate('Year')}
                data={audioTags.year}
              />
          }
          {
            audioTags.label != null &&
              <DescriptionListItem
                title={translate('Label')}
                data={audioTags.label}
              />
          }
          {
            audioTags.publisher != null &&
              <DescriptionListItem
                title={translate('Publisher')}
                data={audioTags.publisher}
              />
          }
          {
            audioTags.catalogNumber != null &&
              <DescriptionListItem
                title={translate('CatalogNumber')}
                data={audioTags.catalogNumber}
              />
          }
          {
            audioTags.disambiguation != null &&
              <DescriptionListItem
                title={translate('Overview')}
                data={stripHtml(audioTags.disambiguation)}
              />
          }
          {
            audioTags.isbn != null &&
              <DescriptionListItem
                title={translate('ISBN')}
                data={audioTags.isbn}
              />
          }
          {
            audioTags.asin != null &&
              <DescriptionListItem
                title={translate('ASIN')}
                data={audioTags.asin}
              />
          }       {
            audioTags.authorMBId != null &&
              <Link
                to={`https://musicbrainz.org/author/${audioTags.authorMBId}`}
              >
                <DescriptionListItem
                  title={translate('MusicBrainzAuthorID')}
                  data={audioTags.authorMBId}
                />
              </Link>
          }
          {
            audioTags.bookMBId != null &&
              <Link
                to={`https://musicbrainz.org/release-group/${audioTags.bookMBId}`}
              >
                <DescriptionListItem
                  title={translate('MusicBrainzBookID')}
                  data={audioTags.bookMBId}
                />
              </Link>
          }
          {
            audioTags.releaseMBId != null &&
              <Link
                to={`https://musicbrainz.org/release/${audioTags.releaseMBId}`}
              >
                <DescriptionListItem
                  title={translate('MusicBrainzReleaseID')}
                  data={audioTags.releaseMBId}
                />
              </Link>
          }
          {
            audioTags.recordingMBId != null &&
              <Link
                to={`https://musicbrainz.org/recording/${audioTags.recordingMBId}`}
              >
                <DescriptionListItem
                  title={translate('MusicBrainzRecordingID')}
                  data={audioTags.recordingMBId}
                />
              </Link>
          }
          {
            audioTags.trackMBId != null &&
              <Link
                to={`https://musicbrainz.org/track/${audioTags.trackMBId}`}
              >
                <DescriptionListItem
                  title={translate('MusicBrainzTrackID')}
                  data={audioTags.trackMBId}
                />
              </Link>
          }
          {
            !!rejections && rejections.length > 0 &&
              renderRejections(rejections)
          }
        </DescriptionList>
      </div>
    </Fragment>
  );
}


export default FileDetails;
