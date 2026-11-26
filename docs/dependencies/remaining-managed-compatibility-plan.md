# Remaining managed compatibility gates

Registry and compatibility review: 2026-11-25. This is a staged follow-up for
P09, not a recommendation to switch the application to .NET 11.

## StyleCop 1.3.180

The official [package history](https://www.nuget.org/packages/StyleCop.Analyzers)
has no stable 1.2 release between 1.1.118 and 1.3.180. The 1.2 versions are
prereleases. Keep the currently verified 1.1.118 until the following migration
can land atomically without reducing diagnostics.

A fresh isolated trial against the current .NET 10 source found a rule
compatibility boundary: 1.1.118 requires a space after a target-typed `new`,
whereas 1.3.180 requires its removal. A syntax-tree-based transformation of
130 expressions in 47 files preserved every lexical token; reverting it from
the maintained branch is necessary because the old rule rejects the new
spacing. Do not merge this formatting separately from the analyzer upgrade.

The retained preparation makes conditional expressions and multiline argument
lists consistent and explicitly groups the existing revision hash shift.
No domain names, API fields, runtime types, or rule severities are changed.
The maintained solution builds, and focused network, composition, redaction
and revision suites pass.

After the syntax-only preparation, the 1.3.180 trial still reports ten
direct Common diagnostics: eight inherited-parameter-name mismatches
([SA1315](https://dotnetanalyzers.github.io/StyleCopAnalyzers/SA1315.html)) and
two native integer alias diagnostics. The earlier trial that advanced past
Common surfaced another 149 direct Core/platform diagnostics, including
84 inherited-parameter-name mismatches. These counts describe actual build
stages, rather than every diagnostic in the solution at once.

Recommended migration sequence:

1. Inventory inherited parameter names at the Common, Core, platform, API
   and test boundaries. Resolve internal domain terminology at its owning
   interface rather than reintroducing historical donor vocabulary. Review
   named argument call sites, `nameof` results and any public CLR/source
   compatibility impact. Do not perform blanket semantic renames.
2. Resolve non-naming diagnostics using syntax-aware edits: aliases,
   conditional and argument formatting, accessor consistency, tuple element
   names, and explicit operator precedence. Verify token/value equivalence
   and preserve native declaration semantics.
3. Atomically pin the stable analyzer, regenerate all locked graphs, and
   normalize target-typed creation spacing. Preserve the existing enabled
   diagnostics; do not suppress a rule simply to make the build pass.
4. Require clean solution and Windows GUI locked restores, a clean Release
   build, source regressions, and hosted API/package contract checks before
   adopting the new analyzer.

## DryIoc 6 and adapter 8

The [DryIoc 6 package](https://www.nuget.org/packages/DryIoc.dll/6.0.0)
targets .NET 10 but adds optional compile-time DI scaffolding. Excluding its
optional build assets in an isolated trial kept the source clean across two
Release builds; the runtime/compile assets and analyzers were not suppressed.
The two obsolete runtime rule calls were migrated to the exact upstream
non-keyed base-rule equivalent. The existing Host suite passed 193 cases,
which alone was insufficient.

A new regression fixture exercises the actual
`DryIocServiceProviderFactory.CreateBuilder(IServiceCollection)` path,
including singleton/scoped/transient identities, disposal, registration
ordering, and missing interface resolution. Both tests fail on DryIoc 6.0.0
with adapter 6.2.0 at runtime: that adapter calls an
`IContainer.With(Rules, IScopeContext, RegistrySharing, IScope)` method
signature that the new core assembly no longer contains. Both tests pass on
the maintained 5.4.3/6.2.0 pair. The mixed-major pair is not compatible and
must not be published merely because it compiles.

[Adapter 8.0.0](https://www.nuget.org/packages/DryIoc.Microsoft.DependencyInjection/8.0.0)
declares Microsoft DI abstractions 11 for its .NET 10 target; the independent
[Npgsql 11](https://www.nuget.org/packages/Npgsql/11.0.0) line also declares
DI and logging abstractions 11. Keep the current tested runtime generation
rather than forcing mixed major assemblies. A future container migration
must explicitly preserve the runtime DI factory and its lifetimes, decide
whether compile-time DI is wanted, and pass both the new lifetime regression
fixture and the actual owned host startup/package matrix.
