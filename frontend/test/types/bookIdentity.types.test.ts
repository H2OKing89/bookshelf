import styles from 'Author/Details/BookRow.css';
import { BookId, BookIdentity, EditionIdentity, LocalRecordingId, MarketplaceRecordingId } from 'Book/BookIdentity';
import { summarizeSeriesRecordings } from 'Utilities/Book/recordingIdentity';

export function checkBookIdentityTypes(book: BookIdentity, edition: EditionIdentity) {
  const bookId: BookId = edition.bookId;
  // @ts-expect-error An edition ID cannot identify its parent book.
  const wrongId: BookId = edition.id;
  // @ts-expect-error A book can have no selected edition.
  const requiredEdition: string = book.foreignEditionId;
  // @ts-expect-error A foreign book identifier is not a numeric database ID.
  const foreignId: BookId = book.foreignBookId;
  // @ts-expect-error This is not a Book API field.
  const invalidField = book.recordingId;

  const market: MarketplaceRecordingId<'us'> = 'audible:us:B0F14RPXHR';
  // @ts-expect-error The recording belongs to the UK marketplace.
  const wrongMarket: MarketplaceRecordingId<'us'> = 'audible:uk:B0F14RPXHR';
  // @ts-expect-error A marketplace identity cannot identify a local recording.
  const wrongSource: LocalRecordingId = market;

  const cssClass = styles.recordingIdentity;
  // @ts-expect-error The generated CSS declarations contain the exact class names.
  const staleCssClass = styles.recordingIdentities;
  const summary = summarizeSeriesRecordings([book], { [bookId]: 1 });

  return { bookId, wrongId, requiredEdition, foreignId, invalidField, market, wrongMarket, wrongSource, cssClass, staleCssClass, summary };
}
