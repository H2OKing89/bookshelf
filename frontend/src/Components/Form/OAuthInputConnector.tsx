import React, { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { resetOAuth, startOAuth } from 'Store/Actions/oAuthActions';
import { isStateRecord, type LegacyRootState } from 'Store/StateTypes';
import OAuthInput from './OAuthInput';
import { type ProviderControlProps } from './providerControlTypes';

interface OwnProps extends ProviderControlProps {
  label?: string;
  section: string;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.oAuth,
    (oAuth) => {
      return {
        result: isStateRecord(oAuth.result) ? oAuth.result : null,
        authorizing: oAuth.authorizing === true,
        error: oAuth.error
      };
    }
  );
}

const mapDispatchToProps = {
  startOAuth,
  resetOAuth
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class OAuthInputConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidUpdate(prevProps: Props) {
    const {
      result,
      onChange
    } = this.props;

    if (!result || result === prevProps.result) {
      return;
    }

    Object.keys(result).forEach((key) => {
      onChange({ name: key, value: result[key] });
    });
  }

  componentWillUnmount = () => {
    this.props.resetOAuth();
  };

  //
  // Listeners

  onPress = () => {
    const {
      name,
      provider,
      providerData,
      section
    } = this.props;

    this.props.startOAuth({
      name,
      provider,
      providerData,
      section
    });
  };

  //
  // Render

  render() {
    return (
      <OAuthInput
        {...this.props}
        onPress={this.onPress}
      />
    );
  }
}

export default connector(OAuthInputConnector);
