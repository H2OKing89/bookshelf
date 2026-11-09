import PropTypes from 'prop-types';
import React from 'react';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import formatChapterTime from './formatChapterTime';
import styles from './BookRecordingMetadata.css';

function BookRecordingMetadata({ book = {}, edition = {} }) {
  const contributors = edition.contributors || book.contributors || [];
  const roles = contributors.reduce((result, credit) => {
    if (credit.name) {
      const role = credit.role || 'Contributor';
      result[role] = [...(result[role] || []), credit.name];
    }
    return result;
  }, {});
  const classifications = book.classifications || [];
  const genres = classifications.filter((item) => item.type === 'genre').map((item) => item.name);
  const tags = classifications.filter((item) => item.type === 'tag').map((item) => item.name);
  const subjects = classifications.length ?
    classifications.filter((item) => !['genre', 'tag'].includes(item.type)).map((item) => item.name) :
    book.genres || [];
  const chapterMetadata = edition.chapterMetadata || {};
  const chapters = chapterMetadata.chapters || [];
  const publisher = edition.publisher || book.publisher;
  const languageCode = edition.language || book.language;
  let language = languageCode;
  if (languageCode && Intl.DisplayNames) {
    try {
      language = new Intl.DisplayNames([document.documentElement.lang || 'en'], { type: 'language' }).of(languageCode);
    } catch {
      // Preserve unrecognized provider language values for reference.
    }
  }
  const copyright = edition.copyrightYear || book.copyrightYear;
  const literatureType = edition.literatureType || book.literatureType;
  const isAdult = edition.isAdult ?? book.isAdult;
  const placeholder = edition.releaseDateIsPlaceholder || book.releaseDateIsPlaceholder;
  const information = [
    ...Object.keys(roles).map((role) => [translate(role), [...new Set(roles[role])].join(', ')]),
    [translate('Series'), book.seriesTitle],
    [translate('Publisher'), publisher],
    [translate('Language'), language],
    [translate('CopyrightYear'), copyright],
    [translate('LiteratureType'), titleCase(literatureType)],
    [translate('Genres'), [...new Set(genres)].join(', ')],
    [translate('Tags'), [...new Set(tags)].join(', ')],
    [translate('Subjects'), [...new Set(subjects)].join(', ')],
    [translate('AdultContent'), typeof isAdult === 'boolean' ? translate(isAdult ? 'Yes' : 'No') : null],
    [translate('Duration'), formatChapterTime(chapterMetadata.runtimeLengthMs)]
  ].filter(([, value]) => value !== null && value !== undefined && value !== '');

  if (!information.length && !chapters.length && !placeholder) {
    return null;
  }

  return (
    <section className={styles.metadata} aria-label={translate('RecordingMetadata')}>
      {placeholder && <p role="status">{translate('ReleaseDateUnconfirmed')}</p>}
      <dl className={styles.fields}>
        {information.map(([label, value]) => (
          <div key={label} className={styles.field}>
            <dt>{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
      {
        chapters.length > 0 &&
          <details className={styles.chapters}>
            <summary>{translate('Chapters')} ({chapters.length})</summary>
            <div className={styles.chapterList}>
              <table className={styles.chapterTable}>
                <thead>
                  <tr>
                    <th scope="col">#</th>
                    <th scope="col">{translate('Chapter')}</th>
                    <th scope="col">{translate('StartTime')}</th>
                    <th scope="col">{translate('Duration')}</th>
                  </tr>
                </thead>
                <tbody>
                  {chapters.map((chapter) => (
                    <tr key={chapter.ordinal}>
                      <td>{chapter.ordinal}</td>
                      <td>{chapter.title || translate('Unknown')}</td>
                      <td>{formatChapterTime(chapter.startOffsetMs)}</td>
                      <td>{formatChapterTime(chapter.lengthMs)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </details>
      }
    </section>
  );
}

BookRecordingMetadata.propTypes = {
  book: PropTypes.object,
  edition: PropTypes.object
};

export default BookRecordingMetadata;
