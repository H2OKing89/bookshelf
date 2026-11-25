import { batchActions } from 'redux-batched-actions';
import { isItemId, isStateRecord, ItemId, LegacyDispatch, LegacyRootState, StateRecord } from 'Store/StateTypes';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set, update, updateItem } from '../baseActions';

interface LegacyRequest {
  done(callback: (data: unknown) => void): void;
  fail(callback: (xhr: unknown) => void): void;
}

type ResponseParser = (data: unknown, id?: ItemId | null) => unknown;

export default function createFetchHandler(section: string, url: string, parseResponse?: ResponseParser) {
  return function(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch): () => void {
    dispatch(set({ section, isFetching: true }));

    const { id, ...otherPayload } = payload;
    const invalid = () => dispatch(set({
      section,
      isFetching: false,
      isPopulated: false,
      error: new ApiError({ kind: 'invalid-response' })
    }));

    if (id != null && !isItemId(id)) {
      invalid();
      return () => {};
    }

    // The Jquery transport remains legacy JS. New handlers see unknown
    // response data and validate it before updating typed collections.
    const { request, abortRequest }: { request: LegacyRequest; abortRequest: () => void } = createAjaxRequest({
      url: id == null ? url : `${url}/${id}`,
      data: otherPayload,
      traditional: true
    });

    request.done((data) => {
      let action: ReturnType<typeof update> | ReturnType<typeof updateItem> | null = null;
      try {
        const value = parseResponse ? parseResponse(data, isItemId(id) ? id : undefined) : data;
        if (id == null) {
          action = update({ section, data: value });
        } else {
          if (!isStateRecord(value) || !isItemId(value.id) || String(value.id) !== String(id)) {
            throw new Error('Invalid resource identity');
          }
          action = updateItem({ ...value, section, id: value.id });
        }
      } catch {
        invalid();
        return;
      }

      dispatch(batchActions([
        action,
        set({ section, isFetching: false, isPopulated: true, error: null })
      ]));
    });

    request.fail((xhr) => {
      dispatch(set({
        section,
        isFetching: false,
        isPopulated: false,
        error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr
      }));
    });

    return abortRequest;
  };
}
