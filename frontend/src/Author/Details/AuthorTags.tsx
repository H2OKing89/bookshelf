import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';

interface AuthorTagsProps {
  tags: readonly string[];
}

function AuthorTags({ tags }: AuthorTagsProps) {
  return (
    <div>
      {
        tags.map((tag) => {
          return (
            <Label
              key={tag}
              kind={kinds.INFO}
              size={sizes.LARGE}
            >
              {tag}
            </Label>
          );
        })
      }
    </div>
  );
}

export default AuthorTags;
