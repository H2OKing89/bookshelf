using System;
using System.Collections.Generic;
using System.Linq;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using FluentMigrator.Postgres;
using FluentMigrator.Runner.Generators;
using FluentMigrator.Runner.Generators.Postgres;
using Microsoft.Extensions.Options;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public sealed class NzbDronePostgresGenerator : Postgres15_0Generator
    {
        public NzbDronePostgresGenerator(PostgresQuoter quoter, IOptions<GeneratorOptions> options)
            : base(quoter, options)
        {
        }

        public override string Generate(CreateTableExpression expression)
        {
            return WithCompatibleIdentity(expression.Columns, () => base.Generate(expression));
        }

        public override string Generate(CreateColumnExpression expression)
        {
            return WithCompatibleIdentity(new[] { expression.Column }, () => base.Generate(expression));
        }

        public override string Generate(AlterColumnExpression expression)
        {
            return WithCompatibleIdentity(new[] { expression.Column }, () => base.Generate(expression));
        }

        private static string WithCompatibleIdentity(IEnumerable<ColumnDefinition> columns, Func<string> generate)
        {
            // Older Bookshelf SERIAL columns accept explicit identities. Preserve
            // that contract for newly created tables without weakening an explicit
            // migration request for GENERATED ALWAYS.
            var adjusted = columns.Where(column => column.IsIdentity &&
                !column.AdditionalFeatures.ContainsKey(PostgresExtensions.IdentityGeneration)).Distinct().ToList();
            foreach (var column in adjusted)
            {
                column.AdditionalFeatures.Add(PostgresExtensions.IdentityGeneration, PostgresGenerationType.ByDefault);
            }

            try
            {
                return generate();
            }
            finally
            {
                foreach (var column in adjusted)
                {
                    column.AdditionalFeatures.Remove(PostgresExtensions.IdentityGeneration);
                }
            }
        }
    }
}
