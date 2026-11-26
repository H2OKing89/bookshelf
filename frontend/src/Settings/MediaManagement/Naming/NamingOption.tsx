import classNames from 'classnames';
import { Component } from 'react';
import Link from 'Components/Link/Link';
import { sizes } from 'Helpers/Props';
import { formatToken, type TokenCase } from './namingSettingsState';
import styles from './NamingOption.css';

export interface NamingOptionChange {
  isFullFilename: boolean;
  tokenValue: string;
}

interface Props {
  token: string;
  example: string;
  tokenSeparator: string;
  tokenCase: TokenCase;
  isFullFilename?: boolean;
  size?: typeof sizes.SMALL | typeof sizes.LARGE;
  onPress: (change: NamingOptionChange) => void;
}

class NamingOption extends Component<Props> {

  //
  // Listeners

  onPress = () => {
    const {
      token,
      tokenSeparator,
      tokenCase,
      isFullFilename = false,
      onPress
    } = this.props;

    const tokenValue = formatToken(token, tokenSeparator, tokenCase);
    onPress({ isFullFilename, tokenValue });
  };

  //
  // Render
  render() {
    const {
      token,
      tokenSeparator,
      example,
      tokenCase,
      isFullFilename,
      size = sizes.SMALL
    } = this.props;

    return (
      <Link
        className={classNames(
          styles.option,
          styles[size],
          tokenCase !== 'title' && styles[tokenCase],
          isFullFilename && styles.isFullFilename
        )}
        onPress={this.onPress}
      >
        <div className={styles.token}>
          {formatToken(token, tokenSeparator, tokenCase)}
        </div>

        <div className={styles.example}>
          {formatToken(example, tokenSeparator, tokenCase)}
        </div>
      </Link>
    );
  }
}

export default NamingOption;
