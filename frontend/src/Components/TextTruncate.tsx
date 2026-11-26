import React from 'react';

interface TextTruncateProps {
  text?: string | null;
  line?: number;
}

// The browser clamps at its actual wrapped line layout (including fonts,
// width changes and non-Latin text). Preserve the complete text in the DOM.
export default function TextTruncate({ text, line = 1 }: TextTruncateProps) {
  const lines = Number.isFinite(line) ? Math.floor(line) : 0;

  if (!text || lines < 1) {
    return null;
  }

  return (
    <div
      style={{
        display: '-webkit-box',
        WebkitBoxOrient: 'vertical',
        WebkitLineClamp: lines,
        overflow: 'hidden',
        overflowWrap: 'anywhere'
      }}
    >
      {text}
    </div>
  );
}
