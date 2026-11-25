import React from 'react';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';

interface BookFormatsProps {
  formats?: readonly { id: number; name: string }[] | null;
}

function BookFormats({ formats }: BookFormatsProps) {
  return (
    <div>
      {
        (formats ?? []).map((format) => {
          return (
            <Label
              key={format.id}
              kind={kinds.INFO}
            >
              {format.name}
            </Label>
          );
        })
      }
    </div>
  );
}

export default BookFormats;
