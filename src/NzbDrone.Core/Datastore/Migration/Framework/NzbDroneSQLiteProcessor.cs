using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using FluentMigrator.Runner.Generators.SQLite;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using FluentMigrator.Runner.Processors.SQLite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public class NzbDroneSQLiteProcessor : SQLiteProcessor
    {
        private readonly SQLiteQuoter _quoter;
        private readonly SQLiteGenerator _generator;

        public NzbDroneSQLiteProcessor(SQLiteDbFactory factory,
                                       SQLiteGenerator generator,
                                       ILogger<NzbDroneSQLiteProcessor> logger,
                                       IOptionsSnapshot<ProcessorOptions> options,
                                       IConnectionStringAccessor connectionStringAccessor,
                                       IServiceProvider serviceProvider,
                                       SQLiteQuoter quoter)
            : base(factory, generator, logger, options, connectionStringAccessor, serviceProvider, quoter)
        {
            _quoter = quoter;
            _generator = generator;
        }

        public override void Process(AlterColumnExpression expression)
        {
            var schema = ReadRawSchema(expression.TableName);
            schema.AlterColumn(
                expression.Column.Name,
                GenerateColumn(expression.Column),
                expression.Column.DefaultValue is not ColumnDefinition.UndefinedDefaultValue);
            RebuildTable(expression.TableName, schema);
        }

        public override void Process(AlterDefaultConstraintExpression expression)
        {
            var schema = ReadRawSchema(expression.TableName);
            schema.ChangeDefault(expression.ColumnName, GenerateColumn(new ColumnDefinition
            {
                Name = expression.ColumnName,
                Type = DbType.String,
                IsNullable = true,
                DefaultValue = expression.DefaultValue
            }));
            RebuildTable(expression.TableName, schema);
        }

        public override void Process(DeleteDefaultConstraintExpression expression)
        {
            var schema = ReadRawSchema(expression.TableName);
            schema.DeleteDefault(expression.ColumnName);
            RebuildTable(expression.TableName, schema);
        }

        public override void Process(DeleteColumnExpression expression)
        {
            RequireNativeAlter(new Version(3, 35));
            foreach (var column in expression.ColumnNames)
            {
                // SQLite retains unrelated constraints, expressions, triggers and
                // sequence state, and rejects a referenced column rather than
                // silently dropping its dependencies.
                Process($"ALTER TABLE {_quoter.QuoteTableName(expression.TableName)} DROP COLUMN {_quoter.QuoteColumnName(column)}");
            }
        }

        public override void Process(RenameColumnExpression expression)
        {
            RequireNativeAlter(new Version(3, 25));
            Process($"ALTER TABLE {_quoter.QuoteTableName(expression.TableName)} RENAME COLUMN {_quoter.QuoteColumnName(expression.OldName)} TO {_quoter.QuoteColumnName(expression.NewName)}");
        }

        private void RequireNativeAlter(Version minimum)
        {
            var value = Read("SELECT sqlite_version()").Tables[0].Rows[0][0].ToString();
            if (!Version.TryParse(value, out var current) || current < minimum)
            {
                throw new NotSupportedException($"This SQLite schema change requires SQLite {minimum} or later.");
            }
        }

        private string GenerateColumn(ColumnDefinition definition)
        {
            var column = (ColumnDefinition)definition.Clone();
            column.TableName = "migration_column_definition";
            column.ModificationType = ColumnModificationType.Create;
            var sql = _generator.Generate(new CreateTableExpression
            {
                TableName = column.TableName,
                Columns = new List<ColumnDefinition> { column }
            });
            return SqliteTableSchema.SingleColumnDefinition(sql);
        }

        private SqliteTableSchema ReadRawSchema(string tableName)
        {
            var rows = Read($"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = {Literal(tableName)}").Tables[0].Rows;
            if (rows.Count != 1 || rows[0][0] == DBNull.Value)
            {
                throw new InvalidOperationException($"SQLite table {tableName} does not exist or has no ordinary table definition.");
            }

            return new SqliteTableSchema(rows[0][0].ToString());
        }

        private void RebuildTable(string tableName, SqliteTableSchema schema)
        {
            GuardForeignKeyRebuild(tableName);
            var objects = Read($"SELECT sql FROM sqlite_master WHERE tbl_name = {Literal(tableName)} AND type IN ('index', 'trigger') AND sql IS NOT NULL ORDER BY type, name")
                .Tables[0].Rows.Cast<DataRow>().Select(row => row[0].ToString()).ToList();
            var sequence = ReadSequence(tableName);
            var tempTableName = tableName + "_temp";
            var uid = 0;
            while (TableExists(null, tempTableName))
            {
                tempTableName = tableName + "_temp" + uid++;
            }

            var columns = string.Join(", ", schema.WritableColumnNames.Select(_quoter.QuoteColumnName));
            if (string.IsNullOrEmpty(columns))
            {
                throw new NotSupportedException("A SQLite table without writable columns requires an explicit migration.");
            }

            var legacyAlter = Convert.ToInt32(Read("PRAGMA legacy_alter_table").Tables[0].Rows[0][0], CultureInfo.InvariantCulture);
            Process("PRAGMA legacy_alter_table = ON");
            try
            {
                Process(schema.CreateSql(_quoter.QuoteTableName(tempTableName)));
                Process($"INSERT INTO {_quoter.QuoteTableName(tempTableName)} ({columns}) SELECT {columns} FROM {_quoter.QuoteTableName(tableName)}");
                Process($"DROP TABLE {_quoter.QuoteTableName(tableName)}");

                // Existing views and incoming foreign keys must keep referencing
                // the original identity during the temporary table replacement.
                Process($"ALTER TABLE {_quoter.QuoteTableName(tempTableName)} RENAME TO {_quoter.QuoteTableName(tableName)}");
            }
            finally
            {
                Process($"PRAGMA legacy_alter_table = {legacyAlter.ToString(CultureInfo.InvariantCulture)}");
            }

            foreach (var sql in objects)
            {
                Process(sql);
            }

            if (sequence.HasValue)
            {
                Process($"UPDATE sqlite_sequence SET seq = MAX(seq, {sequence.Value.ToString(CultureInfo.InvariantCulture)}) WHERE name = {Literal(tableName)}");
                Process($"INSERT INTO sqlite_sequence (name, seq) SELECT {Literal(tableName)}, {sequence.Value.ToString(CultureInfo.InvariantCulture)} WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = {Literal(tableName)})");
            }
        }

        private long? ReadSequence(string tableName)
        {
            if (!TableExists(null, "sqlite_sequence"))
            {
                return null;
            }

            var rows = Read($"SELECT seq FROM sqlite_sequence WHERE name = {Literal(tableName)}").Tables[0].Rows;
            return rows.Count == 0 ? null : Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture);
        }

        private void GuardForeignKeyRebuild(string tableName)
        {
            var foreignKeys = Convert.ToInt32(Read("PRAGMA foreign_keys").Tables[0].Rows[0][0], CultureInfo.InvariantCulture);
            if (foreignKeys == 0)
            {
                return;
            }

            var tables = Read("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").Tables[0];
            foreach (DataRow table in tables.Rows)
            {
                var keys = Read($"PRAGMA foreign_key_list({Literal(table[0].ToString())})").Tables[0];
                if (keys.Rows.Cast<DataRow>().Any(row => string.Equals(row["table"].ToString(), tableName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Disabling foreign_keys inside an active migration transaction
                    // has no effect. Dropping this table could cascade into children.
                    throw new NotSupportedException("Rebuilding a referenced SQLite table with foreign keys enabled requires an explicit migration.");
                }
            }
        }

        private static string Literal(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
