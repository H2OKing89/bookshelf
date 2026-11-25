import React from 'react';
import styles from './AuthorAlternateTitles.css';

interface AuthorAlternateTitlesProps {
  alternateTitles: readonly string[];
}

function AuthorAlternateTitles({ alternateTitles }: AuthorAlternateTitlesProps) {
  return (
    <ul>
      {
        alternateTitles.map((alternateTitle) => {
          return (
            <li
              key={alternateTitle}
              className={styles.alternateTitle}
            >
              {alternateTitle}
            </li>
          );
        })
      }
    </ul>
  );
}

export default AuthorAlternateTitles;
