import { ComponentType } from 'react';
import { useLocation, useNavigationType } from 'react-router-dom';
import { getScrollPosition, ScrollPositionKey } from 'Store/scrollPositions';
import { isStateRecord } from 'Store/StateTypes';

function withScrollPosition<Props extends object>(
  WrappedComponent: ComponentType<Props & { scrollTop: number }>,
  scrollPositionKey: ScrollPositionKey
) {
  function ScrollPosition(props: Props) {
    const navigationType = useNavigationType();
    const state: unknown = useLocation().state;

    const scrollTop = navigationType === 'POP' || (isStateRecord(state) && state.restoreScrollPosition === true) ?
      getScrollPosition(scrollPositionKey) :
      0;

    return (
      <WrappedComponent
        {...props}
        scrollTop={scrollTop}
      />
    );
  }

  return ScrollPosition;
}

export default withScrollPosition;
