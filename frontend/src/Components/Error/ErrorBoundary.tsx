import React, { Component } from 'react';

export interface BoundaryErrorProps {
  error: Error;
  info: React.ErrorInfo;
}

interface ErrorBoundaryProps {
  children?: React.ReactNode;
  renderError: (failure: BoundaryErrorProps) => React.ReactNode;
}

interface ErrorBoundaryState {
  failure: BoundaryErrorProps | null;
}

class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { failure: null };

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    this.setState({ failure: { error, info } });
  }

  render() {
    const { failure } = this.state;

    if (!failure) {
      return this.props.children;
    }
    return this.props.renderError(failure);
  }
}

export default ErrorBoundary;
