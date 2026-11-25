import React, { Component } from 'react';
import LazyLoad from 'react-lazyload';
import type { LibraryImage } from 'Book/LibraryPresentation';

export interface AuthorImageProps {
  className?: string;
  style?: React.CSSProperties;
  images: readonly LibraryImage[];
  coverType: string;
  placeholder: string;
  size: number;
  lazy: boolean;
  overflow: boolean;
  blurBackground: boolean;
  onError?: () => void;
  onLoad?: () => void;
}

interface AuthorImageState {
  pixelRatio: number;
  image?: LibraryImage;
  url?: string;
  isLoaded: boolean;
  hasError: boolean;
}

function findImage(images: readonly LibraryImage[], coverType: string) {
  return images.find((image) => image.coverType === coverType);
}

function getUrl(image: LibraryImage | undefined, coverType: string, size: number): string | undefined {
  const imageUrl = image?.url;

  if (imageUrl) {
    return imageUrl.replace(`${coverType}.jpg`, `${coverType}-${size}.jpg`);
  }
  return undefined;
}

class AuthorImage extends Component<AuthorImageProps, AuthorImageState> {
  static defaultProps = {
    size: 250,
    lazy: true,
    overflow: false,
    blurBackground: false
  };

  //
  // Lifecycle

  constructor(props: AuthorImageProps) {
    super(props);

    const pixelRatio = Math.ceil(window.devicePixelRatio);

    const {
      images,
      coverType,
      size
    } = props;

    const image = findImage(images, coverType);

    this.state = {
      pixelRatio,
      image,
      url: getUrl(image, coverType, pixelRatio * size),
      isLoaded: false,
      hasError: false
    };
  }

  componentDidMount() {
    if (!this.state.url && this.props.onError) {
      this.props.onError();
    }
  }

  componentDidUpdate() {
    const {
      images,
      coverType,
      placeholder,
      size,
      onError
    } = this.props;

    const {
      image,
      pixelRatio
    } = this.state;

    const nextImage = findImage(images, coverType);

    if (nextImage && (!image || nextImage.url !== image.url)) {
      this.setState({
        image: nextImage,
        url: getUrl(nextImage, coverType, pixelRatio * size),
        hasError: false
        // Don't reset isLoaded, as we want to immediately try to
        // show the new image, whether an image was shown previously
        // or the placeholder was shown.
      });
    } else if (!nextImage && image) {
      this.setState({
        image: nextImage,
        url: placeholder,
        hasError: false
      });

      if (onError) {
        onError();
      }
    }
  }

  //
  // Listeners

  onError = () => {
    this.setState({
      hasError: true
    });

    if (this.props.onError) {
      this.props.onError();
    }
  };

  onLoad = () => {
    this.setState({
      isLoaded: true,
      hasError: false
    });

    if (this.props.onLoad) {
      this.props.onLoad();
    }
  };

  //
  // Render

  render() {
    const {
      className,
      style,
      placeholder,
      size,
      lazy,
      overflow,
      blurBackground
    } = this.props;

    const blurStyle: React.CSSProperties = {
      ...style,
      objectFit: 'fill',
      filter: 'blur(8px)',
      WebkitFilter: 'blur(8px)'
    };

    const {
      url,
      hasError,
      isLoaded
    } = this.state;

    if (hasError || !url) {
      return (
        <img
          className={className}
          style={style}
          src={placeholder}
        />
      );
    }

    if (lazy) {
      return (
        <LazyLoad
          height={size}
          offset={100}
          overflow={overflow}
          placeholder={
            <img
              className={className}
              style={style}
              src={placeholder}
            />
          }
        >
          <img
            className={className}
            style={style}
            src={url}
            onError={this.onError}
            onLoad={this.onLoad}
          />
        </LazyLoad>
      );
    }

    return (
      <>
        {
          blurBackground ?
            <img
              style={blurStyle}
              src={isLoaded ? url : placeholder}
              onError={this.onError}
              onLoad={this.onLoad}
            /> :
            null
        }

        <img
          className={className}
          style={style}
          src={isLoaded ? url : placeholder}
          onError={this.onError}
          onLoad={this.onLoad}
        />
      </>
    );
  }
}

export default AuthorImage;
