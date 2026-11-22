import { createContext, useContext, useLayoutEffect, useRef } from 'react';

export const UnsavedNavigationContext = createContext(null);

export default function useUnsavedNavigation(isDirty, onDiscard) {
  const register = useContext(UnsavedNavigationContext);
  const discard = useRef(onDiscard);

  useLayoutEffect(() => {
    discard.current = onDiscard;
  }, [onDiscard]);

  useLayoutEffect(() => {
    if (isDirty) {
      return register(() => discard.current?.());
    }
  }, [isDirty, register]);
}
