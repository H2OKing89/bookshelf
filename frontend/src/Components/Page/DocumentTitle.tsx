import React, { createContext, useContext, useLayoutEffect, useMemo, useRef } from 'react';

interface TitleEntry {
  title: string;
  depth: number;
}

// React executes child layout effects before parent effects. Select the
// innermost scope explicitly instead of relying on effect ordering.
export function createTitleRegistry(initialTitle: string, apply: (title: string) => void) {
  let fallback = initialTitle;
  const entries = new Map<symbol, TitleEntry>();

  const update = () => {
    let active: TitleEntry | undefined = undefined;

    for (const entry of entries.values()) {
      if (!active || entry.depth >= active.depth) {
        active = entry;
      }
    }

    apply(active?.title ?? fallback);
  };

  return {
    setFallback(title: string) {
      fallback = title;
      update();
    },
    register(key: symbol, entry: TitleEntry) {
      entries.set(key, entry);
      update();

      return () => {
        entries.delete(key);
        update();
      };
    }
  };
}

type TitleRegistry = ReturnType<typeof createTitleRegistry>;

interface TitleScope {
  registry: TitleRegistry;
  depth: number;
}

const TitleContext = createContext<TitleScope | null>(null);

interface DocumentTitleProps {
  title: string;
  children: React.ReactNode;
}

export function DocumentTitleProvider({ title, children }: DocumentTitleProps) {
  const [scope] = React.useState(() => ({
    registry: createTitleRegistry(title, (value) => {
      document.title = value;
    }),
    depth: 0
  }));

  useLayoutEffect(() => {
    scope.registry.setFallback(title);
  }, [scope, title]);

  return <TitleContext.Provider value={scope}>{children}</TitleContext.Provider>;
}

function DocumentTitle({ title, children }: DocumentTitleProps) {
  const parent = useContext(TitleContext);
  const key = useRef(Symbol('page-title'));
  const scope = useMemo(() => parent && ({
    registry: parent.registry,
    depth: parent.depth + 1
  }), [parent]);

  useLayoutEffect(() => {
    if (scope) {
      return scope.registry.register(key.current, { title, depth: scope.depth });
    }

    // A standalone page can be rendered without the application provider.
    const previous = document.title;
    document.title = title;

    return () => {
      if (document.title === title) {
        document.title = previous;
      }
    };
  }, [scope, title]);

  return <TitleContext.Provider value={scope}>{children}</TitleContext.Provider>;
}

export default DocumentTitle;
