import BookFileEditorTableContentConnector, { type FileEditorScope } from './BookFileEditorTableContentConnector';
import styles from './BookFileEditorTable.css';

function BookFileEditorTable(props: FileEditorScope) {

  return (
    <div className={styles.container}>
      <BookFileEditorTableContentConnector
        {...props}
      />
    </div>
  );
}

export default BookFileEditorTable;
