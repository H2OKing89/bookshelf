import { createContext, useContext, useLayoutEffect, useRef } from 'react';

export type DiscardChanges = () => void;
export type RegisterUnsavedChanges = (onDiscard?: DiscardChanges) => () => void;

export const UnsavedNavigationContext = createContext<RegisterUnsavedChanges | null>(null);

export default function useUnsavedNavigation(isDirty: boolean, onDiscard?: DiscardChanges) {
  const register = useContext(UnsavedNavigationContext);
  const discard = useRef(onDiscard);

  useLayoutEffect(() => {
    discard.current = onDiscard;
  }, [onDiscard]);

  useLayoutEffect(() => {
    if (!isDirty) {
      return undefined;
    }

    if (!register) {
      throw new Error('Unsaved navigation requires a provider');
    }

    return register(() => discard.current?.());
  }, [isDirty, register]);
}
