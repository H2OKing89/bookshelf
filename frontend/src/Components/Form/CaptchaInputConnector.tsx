import React, { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { getCaptchaCookie, refreshCaptcha, resetCaptcha } from 'Store/Actions/captchaActions';
import type { LegacyRootState } from 'Store/StateTypes';
import CaptchaInput from './CaptchaInput';
import { type ProviderControlProps } from './providerControlTypes';

type OwnProps = ProviderControlProps;

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.captcha,
    (captcha) => {
      return {
        refreshing: captcha.refreshing === true,
        token: typeof captcha.token === 'string' ? captcha.token : null,
        siteKey: typeof captcha.siteKey === 'string' ? captcha.siteKey : null,
        secretToken: typeof captcha.secretToken === 'string' ? captcha.secretToken : null
      };
    }
  );
}

const mapDispatchToProps = {
  refreshCaptcha,
  getCaptchaCookie,
  resetCaptcha
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class CaptchaInputConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidUpdate(prevProps: Props) {
    const {
      name,
      token,
      onChange
    } = this.props;

    if (token && token !== prevProps.token) {
      onChange({ name, value: token });
    }
  }

  componentWillUnmount = () => {
    this.props.resetCaptcha();
  };

  //
  // Listeners

  onRefreshPress = () => {
    const {
      provider,
      providerData
    } = this.props;

    this.props.refreshCaptcha({ provider, providerData });
  };

  onCaptchaChange = (captchaResponse: string | null) => {
    // If the captcha has expired `captchaResponse` will be null.
    // In the event it's null don't try to get the captchaCookie.
    // TODO: Should we clear the cookie? or reset the captcha?

    if (!captchaResponse) {
      return;
    }

    const {
      provider,
      providerData
    } = this.props;

    this.props.getCaptchaCookie({ provider, providerData, captchaResponse });
  };

  //
  // Render

  render() {
    const { value } = this.props;
    let captchaValue: string | null | undefined = undefined;

    if (typeof value === 'string') {
      captchaValue = value;
    } else if (value === null) {
      captchaValue = null;
    }

    return (
      <CaptchaInput
        {...this.props}
        value={captchaValue}
        onRefreshPress={this.onRefreshPress}
        onCaptchaChange={this.onCaptchaChange}
      />
    );
  }
}

export default connector(CaptchaInputConnector);
