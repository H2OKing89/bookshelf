import React from 'react';
import type { LibraryLink } from 'Book/LibraryPresentation';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import styles from './BookDetailsLinks.css';

interface BookDetailsLinksProps {
  links: readonly LibraryLink[];
}

function BookDetailsLinks(props: BookDetailsLinksProps) {
  const {
    links
  } = props;

  return (
    <div className={styles.links}>

      {links.map((link, index) => {
        return (
          <span key={index}>
            <Link className={styles.link}
              to={link.url}
              key={index}
            >
              <Label
                className={styles.linkLabel}
                kind={kinds.INFO}
                size={sizes.LARGE}
              >
                {link.name}
              </Label>
            </Link>
            {(index > 0 && index % 5 === 0) &&
              <br />
            }

          </span>
        );
      })}

    </div>

  );
}

export default BookDetailsLinks;
