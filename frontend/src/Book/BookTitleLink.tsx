import Link from 'Components/Link/Link';

interface BookTitleLinkProps {
  titleSlug: string;
  title: string;
  displayTitle?: string | null;
  disambiguation?: string | null;
}

function BookTitleLink({ titleSlug, title, displayTitle, disambiguation }: BookTitleLinkProps) {
  const link = `/book/${titleSlug}`;

  return (
    <Link to={link} title={title}>
      {displayTitle || title}{disambiguation ? ` (${disambiguation})` : ''}
    </Link>
  );
}

export default BookTitleLink;
