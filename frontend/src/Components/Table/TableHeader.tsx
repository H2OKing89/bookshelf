import React from 'react';

export default function TableHeader({ children }: { children?: React.ReactNode }) {
  return <thead><tr>{children}</tr></thead>;
}
