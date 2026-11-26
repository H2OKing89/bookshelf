import Link, { type LinkProps } from 'Components/Link/Link';

interface AuthorNameLinkProps extends Omit<LinkProps, 'to' | 'children'> {
  titleSlug: string;
  authorName: string;
}

function AuthorNameLink({ titleSlug, authorName, ...otherProps }: AuthorNameLinkProps) {
  const link = `/author/${titleSlug}`;

  return (
    <Link to={link} {...otherProps}>
      {authorName}
    </Link>
  );
}

export default AuthorNameLink;
