import { useLayoutEffect, useMemo, useRef, useState } from 'react';
import { releaseIndexAt, releaseOffsets, releaseWindowEntries, releaseWindowIndices } from './releaseWindow';

const cardBreakpoint = 980;
const windowThreshold = 40;
const overscan = 450;

function sameOrder(left, right) {
  return left.length === right.length && left.every((key, index) => key === right[index]);
}

function listTop(viewport) {
  const list = viewport.querySelector('[data-release-list]');
  return list ? list.getBoundingClientRect().top - viewport.getBoundingClientRect().top + viewport.scrollTop : 0;
}

export function useSearchWidth() {
  const ref = useRef(null);
  const [width, setWidth] = useState(0);

  useLayoutEffect(() => {
    const element = ref.current;
    const measure = () => setWidth(element.clientWidth);
    measure();

    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return { ref, width, cards: width < cardBreakpoint };
}

export function useReleaseWindow(keys, width, cards, scope, confirmationKey) {
  const ref = useRef(null);
  const previous = useRef(null);
  const [, setRevision] = useState(0);
  const [view, setView] = useState({ top: 0, height: 600 });
  const [focusedKey, setFocusedKey] = useState(null);
  const { heights } = useMemo(() => ({ width, cards, scope, heights: new Map() }), [width, cards, scope]);
  const estimate = cards ? 240 : 84;
  const offsets = releaseOffsets(keys, heights, estimate);
  const virtualized = keys.length > windowThreshold;
  const indices = virtualized ?
    releaseWindowIndices(offsets, view.top, view.height, overscan, [keys.indexOf(focusedKey), keys.indexOf(confirmationKey)]) :
    keys.map((_key, index) => index);

  useLayoutEffect(() => {
    const viewport = ref.current;
    const last = previous.current;
    previous.current = { keys, offsets, heights, scope };

    if (!viewport || !virtualized) {
      return undefined;
    }

    if (last && (last.scope !== scope || !sameOrder(last.keys, keys))) {
      viewport.scrollTop = 0;
    } else if (last && last.heights !== heights && viewport.scrollTop > listTop(viewport)) {
      // Keep the release at the visible edge when a resize changes the layout.
      const anchor = releaseIndexAt(last.offsets, view.top);
      viewport.scrollTop = listTop(viewport) + offsets[anchor];
    }

    function updateView() {
      const top = Math.max(0, viewport.scrollTop - listTop(viewport));
      const height = viewport.clientHeight;
      setView((current) => (current.top === top && current.height === height ? current : { top, height }));
    }

    const rows = Array.from(viewport.querySelectorAll('[data-release-row]'));
    function measureRows() {
      const top = Math.max(0, viewport.scrollTop - listTop(viewport));
      const before = releaseOffsets(keys, heights, estimate);
      const anchor = releaseIndexAt(before, top);
      let changed = false;

      rows.forEach((row) => {
        const height = row.getBoundingClientRect().height;
        const key = row.dataset.releaseRow;
        if (height && Math.abs((heights.get(key) ?? estimate) - height) > 0.5) {
          heights.set(key, height);
          changed = true;
        }
      });

      if (changed) {
        const after = releaseOffsets(keys, heights, estimate);
        // Rows above the visible edge may expand or be measured for the first
        // time. Compensate for that change before replacing the spacers.
        if (top > 0) {
          viewport.scrollTop += after[anchor] - before[anchor];
        }
        setRevision((current) => current + 1);
      }

      updateView();
    }

    let frame = 0;
    const scheduleMeasure = () => {
      if (!frame) {
        frame = requestAnimationFrame(() => {
          frame = 0;
          measureRows();
        });
      }
    };

    measureRows();
    const observer = new ResizeObserver(scheduleMeasure);
    observer.observe(viewport);
    rows.forEach((row) => observer.observe(row));
    viewport.addEventListener('scroll', updateView, { passive: true });

    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      viewport.removeEventListener('scroll', updateView);
    };
  });

  return {
    ref,
    virtualized,
    entries: releaseWindowEntries(offsets, indices),
    onFocusCapture: (event) => setFocusedKey(event.target.closest('[data-release-row]')?.dataset.releaseRow ?? null),
    onBlurCapture: (event) => {
      if (!ref.current?.contains(event.relatedTarget)) {
        setFocusedKey(null);
      }
    }
  };
}
