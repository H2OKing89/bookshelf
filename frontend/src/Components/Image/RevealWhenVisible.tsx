import React, { useEffect, useRef, useState } from 'react';

interface VisibilityOptions extends IntersectionObserverInit {
  // Recent browsers also support expanding clipped scroll ancestors. Older
  // implementations ignore this optional dictionary member.
  scrollMargin?: string;
}

type VisibilityEntry = Pick<IntersectionObserverEntry, 'target' | 'isIntersecting'>;
type ObserverFactory = (
  callback: (entries: readonly VisibilityEntry[]) => void,
  options: VisibilityOptions
) => Pick<IntersectionObserver, 'observe' | 'disconnect'>;

export function observeVisibility(
  element: Element,
  onReveal: () => void,
  createObserver: ObserverFactory | undefined,
  prefetchClipped: boolean
) {
  if (!createObserver) {
    onReveal();
    return () => {};
  }

  let active = true;
  let observer: Pick<IntersectionObserver, 'observe' | 'disconnect'> | undefined = undefined;
  const stop = () => {
    if (active) {
      active = false;
      observer?.disconnect();
    }
  };
  observer = createObserver((entries) => {
    if (active && entries.some((entry) => entry.target === element && entry.isIntersecting)) {
      stop();
      onReveal();
    }
  }, {
    rootMargin: '100px',
    scrollMargin: prefetchClipped ? '100px' : undefined
  });

  if (active) {
    observer.observe(element);
  } else {
    observer.disconnect();
  }
  return stop;
}

interface RevealWhenVisibleProps {
  children: React.ReactNode;
  placeholder: React.ReactNode;
  prefetchClipped?: boolean;
}

// Keep the placeholder in layout until the element approaches visibility.
// Reveal only once, even when it scrolls out of view again. Native observation
// handles clipping and nested scroll containers without document listeners.
function RevealWhenVisible({ children, placeholder, prefetchClipped = false }: RevealWhenVisibleProps) {
  const ref = useRef<HTMLDivElement>(null);
  const [revealed, setRevealed] = useState(false);

  useEffect(() => {
    if (revealed || !ref.current) {
      return undefined;
    }

    const createObserver: ObserverFactory | undefined = typeof IntersectionObserver === 'undefined' ?
      undefined :
      (callback, options) => new IntersectionObserver(callback, options);

    return observeVisibility(ref.current, () => setRevealed(true), createObserver, prefetchClipped);
  }, [revealed, prefetchClipped]);

  return <div ref={ref}>{revealed ? children : placeholder}</div>;
}

export default RevealWhenVisible;
