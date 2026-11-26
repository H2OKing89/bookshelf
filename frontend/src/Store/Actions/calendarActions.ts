import _ from 'lodash';
import moment from 'moment';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import * as calendarViews from 'Calendar/calendarViews';
import * as commandNames from 'Commands/commandNames';
import { filterTypes } from 'Helpers/Props';
import { asStateRecord, isStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set, update } from './baseActions';
import { executeCommandHelper } from './commandActions';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';
import { positiveInteger } from './Creators/pagedCollection';
import createClearReducer from './Creators/Reducers/createClearReducer';
import { createSectionRequests } from './Creators/sectionRequests';

//
// Variables

export const section = 'calendar';

type CalendarView = typeof calendarViews.DAY | typeof calendarViews.WEEK | typeof calendarViews.MONTH |
  typeof calendarViews.FORECAST | typeof calendarViews.AGENDA;
const requests = createSectionRequests();
const viewRanges: Record<CalendarView, moment.unitOfTime.DurationConstructor> = {
  [calendarViews.DAY]: 'day',
  [calendarViews.WEEK]: 'week',
  [calendarViews.MONTH]: 'month',
  [calendarViews.FORECAST]: 'day',
  [calendarViews.AGENDA]: 'month'
};

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  start: null,
  end: null,
  dates: [],
  dayCount: 7,
  view: window.innerWidth > 768 ? 'week' : 'day',
  showUpcoming: true,
  error: null,
  items: [],
  searchMissingCommandId: null,

  options: {
    collapseMultipleBooks: false,
    showCutoffUnmetIcon: false
  },

  selectedFilterKey: 'monitored',

  filters: [
    {
      key: 'all',
      label: 'All',
      filters: [
        {
          key: 'monitored',
          value: false,
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'monitored',
      label: 'Monitored Only',
      filters: [
        {
          key: 'monitored',
          value: true,
          type: filterTypes.EQUAL
        }
      ]
    }
  ]
};

export const persistState = [
  'calendar.view',
  'calendar.selectedFilterKey',
  'calendar.options'
];

//
// Actions Types

export const FETCH_CALENDAR = 'calendar/fetchCalendar';
export const SET_CALENDAR_DAYS_COUNT = 'calendar/setCalendarDaysCount';
export const SET_CALENDAR_FILTER = 'calendar/setCalendarFilter';
export const SET_CALENDAR_VIEW = 'calendar/setCalendarView';
export const GOTO_CALENDAR_TODAY = 'calendar/gotoCalendarToday';
export const GOTO_CALENDAR_NEXT_RANGE = 'calendar/gotoCalendarNextRange';
export const CLEAR_CALENDAR = 'calendar/clearCalendar';
export const SET_CALENDAR_OPTION = 'calendar/setCalendarOption';
export const SEARCH_MISSING = 'calendar/searchMissing';
export const GOTO_CALENDAR_PREVIOUS_RANGE = 'calendar/gotoCalendarPreviousRange';

//
// Helpers

function getDays(start: moment.Moment, end: moment.Moment) {
  const startTime = moment(start);
  const endTime = moment(end);
  const difference = endTime.diff(startTime, 'days');

  // Difference is one less than the number of days we need to account for.
  return _.times(difference + 1, (i) => {
    return startTime.clone().add(i, 'days').toISOString();
  });
}

function getDates(time: moment.Moment, view: CalendarView, firstDayOfWeek: number, dayCount: number) {
  const weekName = firstDayOfWeek === 0 ? 'week' : 'isoWeek';

  let start = time.clone().startOf('day');
  let end = time.clone().endOf('day');

  if (view === calendarViews.WEEK) {
    start = time.clone().startOf(weekName);
    end = time.clone().endOf(weekName);
  }

  if (view === calendarViews.FORECAST) {
    start = time.clone().subtract(1, 'day').startOf('day');
    end = time.clone().add(dayCount - 2, 'days').endOf('day');
  }

  if (view === calendarViews.MONTH) {
    start = time.clone().startOf('month').startOf(weekName);
    end = time.clone().endOf('month').endOf(weekName);
  }

  if (view === calendarViews.AGENDA) {
    start = time.clone().subtract(1, 'day').startOf('day');
    end = time.clone().add(1, 'month').endOf('day');
  }

  return {
    start: start.toISOString(),
    end: end.toISOString(),
    time: time.toISOString(),
    dates: getDays(start, end)
  };
}

function getPopulatableRange(startDate: string, endDate: string, view: CalendarView) {
  switch (view) {
    case calendarViews.DAY:
      return {
        start: moment(startDate).subtract(1, 'day').toISOString(),
        end: moment(endDate).add(1, 'day').toISOString()
      };
    case calendarViews.WEEK:
    case calendarViews.FORECAST:
      return {
        start: moment(startDate).subtract(1, 'week').toISOString(),
        end: moment(endDate).add(1, 'week').toISOString()
      };
    default:
      return {
        start: startDate,
        end: endDate
      };
  }
}

function isCalendarView(value: unknown): value is CalendarView {
  return typeof value === 'string' && Object.hasOwn(viewRanges, value);
}

function isRangePopulated(start: string, end: string, state: StateRecord) {
  const {
    start: currentStart,
    end: currentEnd,
    view: currentView
  } = state;

  if (typeof currentStart !== 'string' || typeof currentEnd !== 'string' || !isCalendarView(currentView)) {
    return false;
  }

  const {
    start: currentPopulatedStart,
    end: currentPopulatedEnd
  } = getPopulatableRange(currentStart, currentEnd, currentView);

  if (
    moment(start).isSameOrAfter(currentPopulatedStart) &&
    moment(end).isSameOrBefore(currentPopulatedEnd)
  ) {
    return true;
  }

  return false;
}

function calendarView(value: unknown): CalendarView {
  return isCalendarView(value) ? value : 'week';
}

function calendarTime(value: unknown) {
  let time = moment();
  if (moment.isMoment(value)) {
    time = value.clone();
  } else if (typeof value === 'string' || value instanceof Date || typeof value === 'number') {
    time = moment(value);
  }
  return time.isValid() ? time : moment();
}

function calendarDayCount(state: StateRecord) {
  return positiveInteger(state.dayCount) ?? defaultState.dayCount;
}

function firstDay(state: LegacyRootState) {
  const ui = asStateRecord(asStateRecord(state.settings.ui).item);
  return ui.firstDayOfWeek === 0 ? 0 : 1;
}

//
// Action Creators

export const fetchCalendar = createThunk(FETCH_CALENDAR);
export const setCalendarDaysCount = createThunk(SET_CALENDAR_DAYS_COUNT);
export const setCalendarFilter = createThunk(SET_CALENDAR_FILTER);
export const setCalendarView = createThunk(SET_CALENDAR_VIEW);
export const gotoCalendarToday = createThunk(GOTO_CALENDAR_TODAY);
export const gotoCalendarPreviousRange = createThunk(GOTO_CALENDAR_PREVIOUS_RANGE);
export const gotoCalendarNextRange = createThunk(GOTO_CALENDAR_NEXT_RANGE);
export const clearCalendar = createThunk(CLEAR_CALENDAR);
const RESET_CALENDAR = 'calendar/resetCalendar';
const resetCalendar = createAction<void>(RESET_CALENDAR);
export const setCalendarOption = createAction<StateRecord>(SET_CALENDAR_OPTION);
export const searchMissing = createThunk(SEARCH_MISSING);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_CALENDAR]: function(getState, payload, dispatch) {
    const state = getState();
    const calendar = state.calendar;
    const unmonitored = calendar.selectedFilterKey === 'all';
    const view = calendarView(payload.view ?? calendar.view);
    const time = calendarTime(payload.time ?? calendar.time);
    const dates = getDates(time, view, firstDay(state), calendarDayCount(calendar));
    const { start, end } = getPopulatableRange(dates.start, dates.end, view);
    const isPrePopulated = isRangePopulated(start, end, calendar);

    const attempt = requests.begin(getState, section);
    dispatch(set({ section, isFetching: true, ...(isPrePopulated && { view, ...dates }) }));
    const { request, abortRequest } = requestLegacy({
      url: '/calendar',
      data: { unmonitored, start: moment(start).format('YYYY-MM-DD'), end: moment(end).format('YYYY-MM-DD') }
    });
    attempt.attachAbort(abortRequest);

    request.done((data) => {
      if (!attempt.finish()) {
        return;
      }
      if (!Array.isArray(data) || !data.every((item: unknown) => isStateRecord(item) && positiveInteger(item.id))) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-response' }) }));
        return;
      }
      dispatch(batchActions([
        update({ section, data }),
        set({ section, view, ...dates, isFetching: false, isPopulated: true, error: null })
      ]));
    });

    request.fail((xhr) => {
      if (attempt.finish()) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr }));
      }
    });
  },

  [SET_CALENDAR_DAYS_COUNT]: function(getState, payload, dispatch) {
    if (!positiveInteger(payload.dayCount) || payload.dayCount === getState().calendar.dayCount) {
      return;
    }
    dispatch(set({ section, dayCount: payload.dayCount }));
    const { time, view } = getState().calendar;
    dispatch(fetchCalendar({ time, view }));
  },

  [SET_CALENDAR_FILTER]: function(getState, payload, dispatch) {
    if (payload.selectedFilterKey !== 'all' && payload.selectedFilterKey !== 'monitored') {
      return;
    }
    dispatch(set({ section, selectedFilterKey: payload.selectedFilterKey }));
    const { time, view } = getState().calendar;
    dispatch(fetchCalendar({ time, view }));
  },

  [SET_CALENDAR_VIEW]: function(getState, payload, dispatch) {
    if (!isCalendarView(payload.view)) {
      return;
    }
    const view = payload.view;
    const time = view === calendarViews.FORECAST || view === calendarViews.AGENDA ? moment() : getState().calendar.time;
    dispatch(fetchCalendar({ time, view }));
  },

  [GOTO_CALENDAR_TODAY]: function(getState, _payload, dispatch) {
    dispatch(fetchCalendar({ time: moment(), view: getState().calendar.view }));
  },

  [GOTO_CALENDAR_PREVIOUS_RANGE]: function(getState, _payload, dispatch) {
    const calendar = getState().calendar;
    const view = calendarView(calendar.view);
    const amount = view === calendarViews.FORECAST ? calendarDayCount(calendar) : 1;
    dispatch(fetchCalendar({ time: calendarTime(calendar.time).subtract(amount, viewRanges[view]), view }));
  },

  [GOTO_CALENDAR_NEXT_RANGE]: function(getState, _payload, dispatch) {
    const calendar = getState().calendar;
    const view = calendarView(calendar.view);
    const amount = view === calendarViews.FORECAST ? calendarDayCount(calendar) : 1;
    dispatch(fetchCalendar({ time: calendarTime(calendar.time).add(amount, viewRanges[view]), view }));
  },

  [SEARCH_MISSING]: function(_getState, payload, dispatch) {
    const { bookIds } = payload;
    if (!Array.isArray(bookIds) || !bookIds.every(positiveInteger)) {
      return;
    }
    return executeCommandHelper({ name: commandNames.BOOK_SEARCH, bookIds }, dispatch)
      .then((data) => dispatch(set({ section, searchMissingCommandId: data.id })))
      .catch((error: unknown) => dispatch(set({ section, error })));
  },

  [CLEAR_CALENDAR]: function(getState, _payload, dispatch) {
    requests.cancel(getState, section);
    dispatch(resetCalendar());
  }

});

//
// Reducers

export const reducers = createHandleActions({

  [RESET_CALENDAR]: createClearReducer(section, {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: []
  }),

  [SET_CALENDAR_OPTION]: function(state, { payload }) {
    const options = asStateRecord(state.options);

    return {
      ...state,
      options: {
        ...options,
        ...asStateRecord(payload)
      }
    };
  }

}, defaultState, section);
