import { useQuery } from '@tanstack/react-query';
import PropTypes from 'prop-types';
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import { libraryStatisticsQuery } from './libraryStatisticsQuery';
import styles from './LibraryStatisticsPage.css';

function number(value) {
  return value.toLocaleString();
}

function Breakdown({ title, rows, scope }) {
  const [showAll, setShowAll] = useState(false);
  const displayed = showAll ? rows : rows.slice(0, 25);
  return (
    <section className={styles.breakdown}>
      <h2>{title}</h2>
      <p>{scope}</p>
      {rows.length ?
        <table className={styles.table}>
          <thead><tr><th scope="col">Category</th><th scope="col">Count</th></tr></thead>
          <tbody>
            {displayed.map(({ label, count }) => <tr key={label}><td>{label}</td><td>{number(count)}</td></tr>)}
          </tbody>
        </table> :
        <p>No matching records.</p>}
      {rows.length > 25 && <Button onPress={() => setShowAll(value => !value)}>{showAll ? 'Show fewer' : `Show all ${number(rows.length)}`}</Button>}
    </section>
  );
}

Breakdown.propTypes = {
  title: PropTypes.string.isRequired,
  rows: PropTypes.arrayOf(PropTypes.shape({ label: PropTypes.string.isRequired, count: PropTypes.number.isRequired })).isRequired,
  scope: PropTypes.string.isRequired
};

function Metric({ label, value, detail }) {
  return (
    <div className={styles.metric}>
      <dt>{label}</dt>
      <dd>{value}</dd>
      {detail && <dd className={styles.detail}>{detail}</dd>}
    </div>
  );
}

Metric.propTypes = {
  label: PropTypes.string.isRequired,
  value: PropTypes.string.isRequired,
  detail: PropTypes.string
};

function Statistics({ data }) {
  const hours = data.knownAudioDurationSeconds === null ? 'Unknown' : `${(data.knownAudioDurationSeconds / 3600).toLocaleString(undefined, { maximumFractionDigits: 2 })} h`;
  return (
    <>
      <h2>Catalog and ownership</h2>
      <dl className={styles.metrics} aria-label="Catalog and ownership">
        <Metric label="Authors" value={number(data.authorCount)} />
        <Metric label="Books" value={number(data.bookCount)} detail="Library book entries" />
        <Metric label="Audiobook recordings" value={number(data.audioRecordingCount)} detail={`${number(data.ownedAudioRecordingCount)} with associated files`} />
        <Metric label="Ebook editions" value={number(data.ebookEditionCount)} detail={`${number(data.ownedEbookEditionCount)} with associated files`} />
        <Metric label="Confirmed works" value={number(data.confirmedWorkCount)} detail={`${number(data.ownedConfirmedWorkCount)} with associated files`} />
      </dl>
      <p>Recordings and works are different counts. A pack can cover several reviewed works; associated files are not proof of a complete recording.</p>
      <h2>Tracked storage and duration</h2>
      <dl className={styles.metrics} aria-label="Tracked storage and duration">
        <Metric label="Tracked files" value={number(data.fileCount)} detail={`${number(data.audioFileCount)} audio · ${number(data.ebookFileCount)} ebook · ${number(data.otherFileCount)} other`} />
        <Metric label="Tracked size" value={formatBytes(data.sizeOnDisk)} detail="Based on stored file records" />
        <Metric label="Mapped files" value={number(data.mappedFileCount)} detail={formatBytes(data.mappedSizeOnDisk)} />
        <Metric label="Unresolved files" value={number(data.unresolvedFileCount)} detail={formatBytes(data.unresolvedSizeOnDisk)} />
        <Metric label="Known audio duration" value={hours} detail={`${number(data.audioFilesWithKnownDuration)} of ${number(data.audioFileCount)} audio files measured`} />
      </dl>
      <p>Duration excludes stale or advertised measurements. Storage counts each tracked file once, including separate regional copies; it does not measure physical disk blocks or hard links.</p>
      <h2>Audio diagnostics</h2>
      <p>{number(data.audioFileCount - data.audioFilesWithKnownDuration)} audio files lack a reliable measured duration. <Link to="/unmapped">Review unmapped files</Link>.</p>
      <div className={styles.breakdowns}>
        <Breakdown title="MediaInfo status" rows={data.mediaInfoStatus} scope="Audio files" />
        <Breakdown title="Codec and profile" rows={data.codecs} scope="Audio files, primary stream" />
        <Breakdown title="Bitrate" rows={data.bitrates} scope="Audio files, primary stream" />
      </div>
      <h2>Recording metadata</h2>
      <p>Catalogued audiobook recordings, whether owned or missing. A recording with several narrators or languages contributes once per distinct credit.</p>
      <div className={styles.breakdowns}>
        <Breakdown title="Catalog source" rows={data.sources} scope="Audiobook recordings" />
        <Breakdown title="Language" rows={data.languages} scope="Audiobook recordings" />
        <Breakdown title="Narrators" rows={data.narrators} scope="Audiobook recordings" />
      </div>
    </>
  );
}

Statistics.propTypes = { data: PropTypes.object.isRequired };

export default function LibraryStatisticsPage() {
  const [authorId, setAuthorId] = useState('');
  const [media, setMedia] = useState('all');
  const [source, setSource] = useState('all');
  const query = useQuery(libraryStatisticsQuery({ authorId: authorId ? Number(authorId) : undefined, media, source }));
  const data = query.data;
  return (
    <PageContent title="Library statistics">
      <PageContentBody>
        <h1>Library statistics</h1>
        <div className={styles.filters}>
          <label className={styles.field}>
            Author
            <select value={authorId} onChange={event => setAuthorId(event.target.value)}>
              <option value="">All authors</option>
              {(data?.authors ?? []).map(author => <option key={author.id} value={author.id}>{author.name}</option>)}
            </select>
          </label>
          <label className={styles.field}>
            Media
            <select value={media} onChange={event => setMedia(event.target.value)}>
              <option value="all">All media</option><option value="audiobook">Audiobooks</option><option value="ebook">Ebooks</option>
            </select>
          </label>
          <label className={styles.field}>
            Source
            <select value={source} onChange={event => setSource(event.target.value)}>
              <option value="all">All sources</option><option value="audible">Audible</option><option value="local">Local recordings</option><option value="other">Other or unknown</option>
            </select>
          </label>
          <Button isDisabled={query.isFetching} onPress={() => query.refetch()}>Refresh</Button>
        </div>
        {(authorId || source !== 'all') && <p>Unresolved files are excluded from author and source filters because they have no confirmed association.</p>}
        {query.isError && <Alert kind={kinds.DANGER} role="alert">Statistics could not be loaded. Refresh to try again.</Alert>}
        {query.isFetching && <p role="status">Updating statistics…</p>}
        {query.isPending && <LoadingIndicator />}
        {data && !query.isPlaceholderData && <Statistics data={data} />}
      </PageContentBody>
    </PageContent>
  );
}
