import { useLayoutEffect, useRef } from 'react';
import { useHistory } from 'react-router-dom';

export default function useUnsavedNavigation(isDirty, onDiscard) {
  const { unsavedChanges } = useHistory();
  const discard = useRef(onDiscard);

  useLayoutEffect(() => {
    discard.current = onDiscard;
  }, [onDiscard]);

  useLayoutEffect(() => {
    if (isDirty) {
      return unsavedChanges.register(() => discard.current?.());
    }
  }, [isDirty, unsavedChanges]);
}
