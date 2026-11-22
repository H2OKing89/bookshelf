import React from 'react';
import { useLocation, useNavigationType } from 'react-router-dom';
import scrollPositions from 'Store/scrollPositions';

function withScrollPosition(WrappedComponent, scrollPositionKey) {
  function ScrollPosition(props) {
    const navigationType = useNavigationType();
    const { state } = useLocation();

    const scrollTop = navigationType === 'POP' || state?.restoreScrollPosition ?
      scrollPositions[scrollPositionKey] :
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
