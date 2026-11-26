import AuthorHistoryContentConnector, { type AuthorHistoryScope } from 'Author/History/AuthorHistoryContentConnector';
import styles from './AuthorHistoryTable.css';

function AuthorHistoryTable(props: AuthorHistoryScope) {

  return (
    <div className={styles.container}>
      <AuthorHistoryContentConnector
        mode="table"
        {...props}
      />
    </div>
  );
}

export default AuthorHistoryTable;
