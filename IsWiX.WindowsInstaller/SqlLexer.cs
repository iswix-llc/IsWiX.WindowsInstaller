namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
    internal enum SqlTokenKind
    {
        Identifier,
        String,
        Integer,
        Marker,
        Symbol,
        End,
    }

    internal sealed record SqlToken(SqlTokenKind Kind, string Text, bool Quoted = false, int Number = 0);

    internal static class SqlLexer
    {
        public static List<SqlToken> Tokenize(string sql)
        {
            var tokens = new List<SqlToken>();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c == '`')
                {
                    int end = sql.IndexOf('`', i + 1);
                    if (end < 0)
                    {
                        throw SqlErrors.Syntax();
                    }
                    tokens.Add(new SqlToken(SqlTokenKind.Identifier, sql.Substring(i + 1, end - i - 1), Quoted: true));
                    i = end + 1;
                }
                else if (c == '\'')
                {
                    int end = sql.IndexOf('\'', i + 1);
                    if (end < 0)
                    {
                        throw SqlErrors.Syntax();
                    }
                    tokens.Add(new SqlToken(SqlTokenKind.String, sql.Substring(i + 1, end - i - 1)));
                    i = end + 1;
                }
                else if (char.IsDigit(c) || (c == '-' && i + 1 < sql.Length && char.IsDigit(sql[i + 1])))
                {
                    int start = i++;
                    while (i < sql.Length && char.IsDigit(sql[i]))
                    {
                        i++;
                    }
                    string text = sql.Substring(start, i - start);
                    if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                    {
                        throw SqlErrors.Syntax();
                    }
                    tokens.Add(new SqlToken(SqlTokenKind.Integer, text, Number: value));
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    int start = i++;
                    while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                    {
                        i++;
                    }
                    tokens.Add(new SqlToken(SqlTokenKind.Identifier, sql.Substring(start, i - start)));
                }
                else if (c == '?')
                {
                    tokens.Add(new SqlToken(SqlTokenKind.Marker, "?"));
                    i++;
                }
                else
                {
                    string two = i + 1 < sql.Length ? sql.Substring(i, 2) : string.Empty;
                    if (two == "<>" || two == "<=" || two == ">=")
                    {
                        tokens.Add(new SqlToken(SqlTokenKind.Symbol, two));
                        i += 2;
                    }
                    else if ("=<>,.()*".IndexOf(c) >= 0)
                    {
                        tokens.Add(new SqlToken(SqlTokenKind.Symbol, c.ToString()));
                        i++;
                    }
                    else
                    {
                        throw SqlErrors.Syntax();
                    }
                }
            }

            tokens.Add(new SqlToken(SqlTokenKind.End, string.Empty));
            return tokens;
        }
    }
}
