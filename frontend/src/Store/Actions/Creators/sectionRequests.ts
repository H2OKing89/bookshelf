import _ from 'lodash';
import type { LegacyRootState } from 'Store/StateTypes';

type GetState = () => LegacyRootState;

interface RequestAttempt {
  abort?: () => void;
}

interface SectionRequest {
  active?: RequestAttempt;
  failed?: unknown;
}

// Each operation family gets its own tracker. Isolate sections and store
// instances, and invalidate a superseded attempt before aborting it: a
// synchronous abort callback must never clear the newer operation's state.
export function createSectionRequests() {
  const stores = new WeakMap<GetState, Map<string, SectionRequest>>();

  function getSections(getState: GetState) {
    let sections = stores.get(getState);
    if (!sections) {
      sections = new Map();
      stores.set(getState, sections);
    }
    return sections;
  }

  function begin(getState: GetState, section: string, retryData?: unknown) {
    const sections = getSections(getState);
    const previous = sections.get(section);
    const attempt: RequestAttempt = {};
    const slot: SectionRequest = { active: attempt };
    const retry = previous?.failed !== undefined && _.isEqual(previous.failed, retryData);
    sections.set(section, slot);
    previous?.active?.abort?.();

    function finish(failedData?: unknown) {
      if (sections.get(section)?.active !== attempt) {
        return false;
      }
      // Do not retain a successful or cancelled provider payload. Keep only
      // the last failed attempt for an unchanged, explicit retry.
      if (failedData === undefined) {
        sections.delete(section);
      } else {
        slot.active = undefined;
        slot.failed = _.cloneDeep(failedData);
      }
      return true;
    }

    return {
      retry,
      attachAbort(abort: () => void) {
        if (sections.get(section)?.active !== attempt) {
          abort();
          return;
        }
        attempt.abort = abort;
      },
      finish
    };
  }

  function cancel(getState: GetState, section: string) {
    const sections = stores.get(getState);
    const slot = sections?.get(section);
    sections?.delete(section);
    slot?.active?.abort?.();
    return slot?.active !== undefined;
  }

  return { begin, cancel };
}
