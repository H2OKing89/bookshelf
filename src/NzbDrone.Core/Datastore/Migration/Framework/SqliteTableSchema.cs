using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    // Rebuild from the stored DDL, rather than an approximation of its columns.
    // SQLite constraints, expressions, collations and table options must survive.
    internal sealed class SqliteTableSchema
    {
        private static readonly HashSet<string> ConstraintKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CONSTRAINT", "PRIMARY", "NOT", "NULL", "UNIQUE", "CHECK", "DEFAULT", "COLLATE", "REFERENCES", "GENERATED", "AS"
        };

        private readonly List<string> _definitions;
        private readonly string _suffix;

        public SqliteTableSchema(string sql)
        {
            var start = FindOpeningParenthesis(sql);
            if (Tokens(sql.Substring(0, start)).Any(token => !token.Quoted && token.Value.Equals("VIRTUAL", StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException("A SQLite virtual table requires an explicit migration rather than an automatic rebuild.");
            }

            var end = FindClosingParenthesis(sql, start);
            _definitions = SplitDefinitions(sql.Substring(start + 1, end - start - 1));
            _suffix = sql.Substring(end + 1);
            ColumnNames = _definitions.Select(definition => Tokens(definition).First())
                .Where(token => token.Quoted || !IsTableConstraint(token.Value))
                .Select(token => token.Value).ToList();
        }

        public List<string> ColumnNames { get; }

        public List<string> WritableColumnNames => _definitions.Select(Tokens)
            .Where(tokens => (tokens[0].Quoted || !IsTableConstraint(tokens[0].Value)) && FindConstraint(tokens, "AS") < 0)
            .Select(tokens => tokens[0].Value).ToList();

        public string CreateSql(string quotedTableName)
        {
            return $"CREATE TABLE {quotedTableName} ({string.Join(", ", _definitions)}){_suffix}";
        }

        public void AlterColumn(string name, string generatedDefinition, bool replaceDefault)
        {
            var index = FindColumn(name);
            var original = _definitions[index];
            var oldTokens = Tokens(original);
            var generatedTokens = Tokens(generatedDefinition);
            foreach (var constraint in new[] { "PRIMARY", "UNIQUE", "COLLATE", "REFERENCES", "GENERATED", "AS" })
            {
                if (FindConstraint(generatedTokens, constraint) >= 0)
                {
                    throw new NotSupportedException("Changing SQLite key, collation, reference or generated-column constraints requires an explicit migration.");
                }
            }

            var oldTypeEnd = ConstraintStart(oldTokens);
            var generatedTypeEnd = ConstraintStart(generatedTokens);
            var type = generatedDefinition.Substring(generatedTokens[0].End, generatedTypeEnd - generatedTokens[0].End);
            var changed = original.Substring(0, oldTokens[0].End) + type + " " + original.Substring(oldTypeEnd);

            var wasNotNull = FindConstraint(oldTokens, "NOT", "NULL") >= 0;
            var isNotNull = FindConstraint(generatedTokens, "NOT", "NULL") >= 0;
            if (wasNotNull != isNotNull)
            {
                changed = RemoveConstraint(changed, "NOT", "NULL");
                changed = RemoveConstraint(changed, "NULL");
                if (isNotNull)
                {
                    changed += " NOT NULL";
                }
            }

            if (replaceDefault)
            {
                changed = ReplaceDefault(changed, DefaultClause(generatedDefinition));
            }

            _definitions[index] = changed;
        }

        public void ChangeDefault(string name, string generatedDefinition)
        {
            var index = FindColumn(name);
            _definitions[index] = ReplaceDefault(_definitions[index], DefaultClause(generatedDefinition));
        }

        public void DeleteDefault(string name)
        {
            var index = FindColumn(name);
            _definitions[index] = ReplaceDefault(_definitions[index], string.Empty);
        }

        public static string SingleColumnDefinition(string generatedCreateSql)
        {
            var schema = new SqliteTableSchema(generatedCreateSql);
            if (schema._definitions.Count != 1)
            {
                throw new NotSupportedException("The generated column definition is not a single SQLite column.");
            }

            return schema._definitions[0];
        }

        private int FindColumn(string name)
        {
            var index = _definitions.FindIndex(definition =>
            {
                var token = Tokens(definition).First();
                return (token.Quoted || !IsTableConstraint(token.Value)) && string.Equals(token.Value, name, StringComparison.OrdinalIgnoreCase);
            });
            if (index < 0)
            {
                throw new InvalidOperationException($"Column {name} does not exist in the SQLite table.");
            }

            return index;
        }

        private static bool IsTableConstraint(string value)
        {
            return value.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("CHECK", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("FOREIGN", StringComparison.OrdinalIgnoreCase);
        }

        private static int ConstraintStart(List<Token> tokens)
        {
            return tokens.Skip(1).FirstOrDefault(token => !token.Quoted && ConstraintKeywords.Contains(token.Value))?.Start ?? tokens.Last().End;
        }

        private static int FindConstraint(List<Token> tokens, params string[] words)
        {
            var start = ConstraintStart(tokens);
            for (var index = 1; index + words.Length <= tokens.Count; index++)
            {
                if (tokens[index].Start >= start && words.Select((word, offset) =>
                    !tokens[index + offset].Quoted && tokens[index + offset].Value.Equals(word, StringComparison.OrdinalIgnoreCase)).All(match => match))
                {
                    return index;
                }

                if (!tokens[index].Quoted && tokens[index].Value.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    // DEFAULT NULL is a value, not the column's nullability.
                    index++;
                }
            }

            return -1;
        }

        private static string RemoveConstraint(string definition, params string[] words)
        {
            var tokens = Tokens(definition);
            var index = FindConstraint(tokens, words);
            if (index < 0)
            {
                return definition;
            }

            var end = index + words.Length;
            if (end + 2 < tokens.Count && tokens[end].Value.Equals("ON", StringComparison.OrdinalIgnoreCase) &&
                tokens[end + 1].Value.Equals("CONFLICT", StringComparison.OrdinalIgnoreCase))
            {
                end += 3;
            }

            var start = NamedConstraintStart(tokens, index);
            return definition.Remove(tokens[start].Start, tokens[end - 1].End - tokens[start].Start);
        }

        private static string DefaultClause(string definition)
        {
            var tokens = Tokens(definition);
            var index = FindConstraint(tokens, "DEFAULT");
            if (index < 0)
            {
                throw new NotSupportedException("The SQLite generator did not supply a default expression.");
            }

            var end = DefaultEnd(tokens, index, definition.Length);
            return definition.Substring(tokens[index].Start, end - tokens[index].Start);
        }

        private static string ReplaceDefault(string definition, string replacement)
        {
            var tokens = Tokens(definition);
            var index = FindConstraint(tokens, "DEFAULT");
            if (index >= 0)
            {
                var end = DefaultEnd(tokens, index, definition.Length);
                var start = string.IsNullOrEmpty(replacement) ? NamedConstraintStart(tokens, index) : index;
                return definition.Remove(tokens[start].Start, end - tokens[start].Start).Insert(tokens[start].Start, replacement + " ");
            }

            return definition.TrimEnd() + (string.IsNullOrEmpty(replacement) ? string.Empty : " " + replacement);
        }

        private static int NamedConstraintStart(List<Token> tokens, int index)
        {
            return index >= 2 && !tokens[index - 2].Quoted && tokens[index - 2].Value.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ? index - 2 : index;
        }

        private static int DefaultEnd(List<Token> tokens, int index, int length)
        {
            // The first token after DEFAULT is its value, even DEFAULT NULL.
            return tokens.Skip(index + 2).FirstOrDefault(token => !token.Quoted && ConstraintKeywords.Contains(token.Value))?.Start ?? length;
        }

        private static int FindOpeningParenthesis(string sql)
        {
            var index = 0;
            while (index < sql.Length)
            {
                if (sql[index] == '(')
                {
                    return index;
                }

                ReadToken(sql, ref index);
            }

            throw new NotSupportedException("Expected a SQLite CREATE TABLE column list.");
        }

        private static int FindClosingParenthesis(string sql, int start)
        {
            var depth = 1;
            var index = start + 1;
            while (index < sql.Length)
            {
                if (sql[index] == '(')
                {
                    depth++;
                    index++;
                }
                else if (sql[index] == ')')
                {
                    if (--depth == 0)
                    {
                        return index;
                    }

                    index++;
                }
                else
                {
                    ReadToken(sql, ref index);
                }
            }

            throw new NotSupportedException("Unbalanced SQLite table definition.");
        }

        private static List<string> SplitDefinitions(string sql)
        {
            var definitions = new List<string>();
            var index = 0;
            var start = 0;
            while (index < sql.Length)
            {
                if (sql[index] == '(')
                {
                    index = FindClosingParenthesis(sql, index) + 1;
                }
                else if (sql[index] == ',')
                {
                    definitions.Add(sql.Substring(start, index - start).Trim());
                    start = ++index;
                }
                else
                {
                    ReadToken(sql, ref index);
                }
            }

            definitions.Add(sql.Substring(start).Trim());
            if (definitions.Any(string.IsNullOrWhiteSpace))
            {
                throw new NotSupportedException("An empty SQLite column definition cannot be rebuilt safely.");
            }

            return definitions;
        }

        private static List<Token> Tokens(string sql)
        {
            var tokens = new List<Token>();
            var index = 0;
            while (index < sql.Length)
            {
                if (char.IsWhiteSpace(sql[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                if (sql[index] == '(')
                {
                    index = FindClosingParenthesis(sql, index) + 1;
                    tokens.Add(new Token { Start = start, End = index, Value = sql.Substring(start, index - start), Quoted = true });
                    continue;
                }

                var value = ReadToken(sql, ref index);
                tokens.Add(new Token { Start = start, End = index, Value = value, Quoted = sql[start] == '\'' || sql[start] == '"' || sql[start] == '`' || sql[start] == '[' });
            }

            return tokens;
        }

        private static string ReadToken(string sql, ref int index)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
                return string.Empty;
            }

            if (sql[index] == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                throw new NotSupportedException("SQLite DDL comments require an explicit migration rather than an automatic table rebuild.");
            }

            if (sql[index] == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                throw new NotSupportedException("SQLite DDL comments require an explicit migration rather than an automatic table rebuild.");
            }

            var start = index;
            var quote = sql[index];
            if (quote == '\'' || quote == '"' || quote == '`' || quote == '[')
            {
                var terminator = quote == '[' ? ']' : quote;
                var value = new StringBuilder();
                index++;
                while (index < sql.Length)
                {
                    if (sql[index] == terminator)
                    {
                        index++;
                        if (terminator != ']' && index < sql.Length && sql[index] == terminator)
                        {
                            value.Append(terminator);
                            index++;
                            continue;
                        }

                        return value.ToString();
                    }

                    value.Append(sql[index++]);
                }

                throw new NotSupportedException("Unterminated SQLite identifier or literal.");
            }

            while (index < sql.Length && !char.IsWhiteSpace(sql[index]) && sql[index] != '(' && sql[index] != ')' && sql[index] != ',' &&
                sql[index] != '\'' && sql[index] != '"' && sql[index] != '`' && sql[index] != '[' &&
                !(index + 1 < sql.Length && ((sql[index] == '-' && sql[index + 1] == '-') || (sql[index] == '/' && sql[index + 1] == '*'))))
            {
                index++;
            }

            if (index == start)
            {
                index++;
            }

            return sql.Substring(start, index - start);
        }

        private sealed class Token
        {
            public int Start { get; set; }
            public int End { get; set; }
            public string Value { get; set; }
            public bool Quoted { get; set; }
        }
    }
}
