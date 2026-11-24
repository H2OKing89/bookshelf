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
  const pendingAdjustment = useRef(0);
  const [, setRevision] = useState(0);
  const [view, setView] = useState({ top: 0, height: 600, atEnd: false });
  const [focusedKey, setFocusedKey] = useState(null);
  const { heights } = useMemo(() => ({ width, cards, scope, heights: new Map() }), [width, cards, scope]);
  const estimate = cards ? 240 : 84;
  const offsets = releaseOffsets(keys, heights, estimate);
  const virtualized = keys.length > windowThreshold;
  const last = previous.current;
  const reset = last && (last.scope !== scope || !sameOrder(last.keys, keys));
  let windowTop = view.top;
  if (reset) {
    windowTop = 0;
  } else if (view.atEnd) {
    windowTop = Math.max(0, offsets[offsets.length - 1] - view.height);
  }
  const indices = virtualized ?
    releaseWindowIndices(offsets, windowTop, view.height, overscan, [keys.indexOf(focusedKey), keys.indexOf(confirmationKey)]) :
    keys.map((_key, index) => index);

  useLayoutEffect(() => {
    const viewport = ref.current;
    previous.current = { keys, offsets, heights, scope };

    if (!viewport || !virtualized) {
      return undefined;
    }

    if (reset) {
      viewport.scrollTop = 0;
    } else if (view.atEnd) {
      // The last rows may be taller or shorter than their estimates. Apply
      // the new maximum after the measured spacers have been committed.
      viewport.scrollTop = viewport.scrollHeight;
    } else if (last && last.heights !== heights && viewport.scrollTop > listTop(viewport)) {
      // Keep the release at the visible edge when a resize changes the layout.
      const anchor = releaseIndexAt(last.offsets, view.top);
      viewport.scrollTop = listTop(viewport) + offsets[anchor];
    } else if (pendingAdjustment.current) {
      viewport.scrollTop += pendingAdjustment.current;
    }
    pendingAdjustment.current = 0;

    function updateView() {
      const top = Math.max(0, Math.round(viewport.scrollTop - listTop(viewport)));
      const height = viewport.clientHeight;
      const maximum = viewport.scrollHeight - height;
      const atEnd = maximum > 2 && viewport.scrollTop >= maximum - 2;
      setView((current) => (current.top === top && current.height === height && current.atEnd === atEnd ?
        current : { top, height, atEnd }));
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
        // time. Restore that edge after the next spacer layout is committed.
        if (top > 0) {
          pendingAdjustment.current += after[anchor] - before[anchor];
        }
        setRevision((current) => current + 1);
      } else {
        updateView();
      }
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

    // Measuring newly mounted rows can change the window again. Schedule it
    // outside the layout-effect commit so several estimates can converge
    // without recursively nesting React updates in one render cycle.
    scheduleMeasure();
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
