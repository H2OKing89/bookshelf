import { connect } from 'react-redux';
import { useNavigate } from 'react-router-dom';
import { createSelector } from 'reselect';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import SwipeHeader from './SwipeHeader';

function createMapStateToProps() {
  return createSelector(
    createDimensionsSelector(),
    (dimensions) => {
      return {
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const ConnectedSwipeHeader = connect(createMapStateToProps)(SwipeHeader);

export default function SwipeHeaderConnector(props) {
  const navigate = useNavigate();

  return <ConnectedSwipeHeader {...props} onGoTo={navigate} />;
}
