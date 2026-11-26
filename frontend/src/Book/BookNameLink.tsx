import Link from 'Components/Link/Link';

interface BookNameLinkProps {
  titleSlug: string;
  title: string;
  displayTitle?: string | null;
}

function BookNameLink({ titleSlug, title, displayTitle }: BookNameLinkProps) {
  const link = `/book/${titleSlug}`;

  return (
    <Link to={link} title={title}>
      {displayTitle || title}
    </Link>
  );
}

export default BookNameLink;
