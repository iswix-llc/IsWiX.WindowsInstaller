// The SELECT subset of the Windows Installer SQL dialect, evaluated over in-memory tables.
//   SELECT [DISTINCT] (* | column[, column]...) FROM table[, table]...
//       [WHERE condition] [ORDER BY column[, column]...]
// A condition is operand op operand, or column IS [NOT] NULL, joined with AND, OR and parentheses.
// Operands are columns (optionally table.column), 'strings', integers or ? parameter markers.
// Tables in the FROM list are joined as inner joins; join conditions are written in WHERE.
//   INSERT INTO table (column[, column]...) VALUES (value[, value]...)
//   UPDATE table SET column = value[, column = value]... [WHERE condition]
//   DELETE FROM table [WHERE condition]
// A value is a number, a 'string' or a ? parameter marker. CREATE, DROP and ALTER are not supported yet.
namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using MsiReader;

    internal static class SqlErrors
    {
        public static MsiErrorException Syntax()
        {
            return new MsiErrorException((uint) MsiError.BAD_QUERY_SYNTAX);
        }
    }

    internal sealed record SqlColumn(string Table, string Name, MsiColumnType Type, int Size,
        bool IsNullable, bool IsPrimaryKey, bool IsLocalizable)
    {
        /// <summary>The Windows Installer column type code: i2 or i4, s&lt;size&gt;, l&lt;size&gt;, or v0; uppercase when nullable.</summary>
        public string TypeCode
        {
            get
            {
                char letter;
                int size;
                switch (this.Type)
                {
                    case MsiColumnType.Integer:
                        letter = 'i';
                        size = this.Size > 0 ? this.Size : 2;
                        break;
                    case MsiColumnType.String:
                        letter = this.IsLocalizable ? 'l' : 's';
                        size = this.Size;
                        break;
                    default:
                        letter = 'v';
                        size = 0;
                        break;
                }
                if (this.IsNullable)
                {
                    letter = char.ToUpperInvariant(letter);
                }
                return letter.ToString() + size.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    internal sealed record SqlTable(string Name, IReadOnlyList<SqlColumn> Columns, IReadOnlyList<object?[]> Rows);

    internal sealed record SqlResult(IReadOnlyList<SqlColumn> Columns, IReadOnlyList<object?[]> Rows);

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

    internal sealed record SqlColumnRef(string? Table, string Name);

    internal enum SqlOperator
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
    }

    internal abstract record SqlOperand;

    internal sealed record SqlColumnOperand(SqlColumnRef Reference) : SqlOperand;

    internal sealed record SqlConstantOperand(object? Value) : SqlOperand;

    internal sealed record SqlMarkerOperand(int Index) : SqlOperand;

    internal abstract record SqlCondition;

    internal sealed record SqlAndCondition(SqlCondition Left, SqlCondition Right) : SqlCondition;

    internal sealed record SqlOrCondition(SqlCondition Left, SqlCondition Right) : SqlCondition;

    internal sealed record SqlCompareCondition(SqlOperator Operator, SqlOperand Left, SqlOperand Right) : SqlCondition;

    internal sealed record SqlNullCondition(SqlColumnRef Column, bool Negated) : SqlCondition;

    /// <summary>A parsed statement. MarkerCount is the number of ? parameters it uses.</summary>
    internal abstract class SqlStatement
    {
        public int MarkerCount { get; set; }
    }

    internal sealed class SqlInsert : SqlStatement
    {
        public string Table { get; set; } = string.Empty;

        public List<string> Columns { get; } = new List<string>();

        public List<SqlOperand> Values { get; } = new List<SqlOperand>();
    }

    internal sealed class SqlUpdate : SqlStatement
    {
        public string Table { get; set; } = string.Empty;

        public List<(string Column, SqlOperand Value)> Assignments { get; } = new List<(string, SqlOperand)>();

        public SqlCondition? Where { get; set; }
    }

    internal sealed class SqlDelete : SqlStatement
    {
        public string Table { get; set; } = string.Empty;

        public SqlCondition? Where { get; set; }
    }

    internal sealed class SqlSelect : SqlStatement
    {
        public bool Distinct { get; set; }

        /// <summary>The selected columns, or null for SELECT *.</summary>
        public List<SqlColumnRef>? Columns { get; set; }

        public List<string> Tables { get; } = new List<string>();

        public SqlCondition? Where { get; set; }

        public List<SqlColumnRef> OrderBy { get; } = new List<SqlColumnRef>();

    }

    /// <summary>A column definition of CREATE TABLE or ALTER TABLE ... ADD.</summary>
    internal sealed class SqlColumnDefinition
    {
        public string Name { get; set; } = string.Empty;

        public MsiColumnType Type { get; set; }

        /// <summary>The size: characters for strings (0 is unlimited), bytes for integers.</summary>
        public int Size { get; set; }

        public bool NotNull { get; set; }

        public bool Localizable { get; set; }
    }

    internal sealed class SqlCreateTable : SqlStatement
    {
        public string Table { get; set; } = string.Empty;

        public List<SqlColumnDefinition> Columns { get; } = new List<SqlColumnDefinition>();

        public List<string> PrimaryKeys { get; } = new List<string>();
    }

    internal sealed class SqlDropTable : SqlStatement
    {
        public string Table { get; set; } = string.Empty;
    }

    internal sealed class SqlAlterTableAdd : SqlStatement
    {
        public string Table { get; set; } = string.Empty;

        public SqlColumnDefinition Column { get; set; } = new SqlColumnDefinition();
    }

    internal sealed class SqlParser
    {
        private readonly List<SqlToken> tokens;
        private int position;
        private int markers;

        public SqlParser(string sql)
        {
            this.tokens = SqlLexer.Tokenize(sql);
        }

        public SqlStatement ParseStatement()
        {
            if (this.IsKeyword("SELECT"))
            {
                return this.ParseSelect();
            }
            if (this.IsKeyword("INSERT"))
            {
                return this.ParseInsert();
            }
            if (this.IsKeyword("UPDATE"))
            {
                return this.ParseUpdate();
            }
            if (this.IsKeyword("DELETE"))
            {
                return this.ParseDelete();
            }
            if (this.IsKeyword("CREATE"))
            {
                return this.ParseCreateTable();
            }
            if (this.IsKeyword("DROP"))
            {
                return this.ParseDropTable();
            }
            if (this.IsKeyword("ALTER"))
            {
                return this.ParseAlterTable();
            }
            throw SqlErrors.Syntax();
        }

        private SqlCreateTable ParseCreateTable()
        {
            this.ExpectKeyword("CREATE");
            this.ExpectKeyword("TABLE");
            var create = new SqlCreateTable { Table = this.ExpectName() };
            this.ExpectSymbol("(");
            while (true)
            {
                if (this.TryKeyword("PRIMARY"))
                {
                    this.ExpectKeyword("KEY");
                    create.PrimaryKeys.Add(this.ExpectName());
                    while (this.TrySymbol(","))
                    {
                        create.PrimaryKeys.Add(this.ExpectName());
                    }
                    break;
                }

                create.Columns.Add(this.ParseColumnDefinition());
                if (this.IsKeyword("PRIMARY"))
                {
                    continue;
                }
                this.ExpectSymbol(",");
            }
            this.ExpectSymbol(")");
            this.ExpectEnd();
            if (create.PrimaryKeys.Count == 0)
            {
                throw SqlErrors.Syntax();
            }
            return create;
        }

        private SqlDropTable ParseDropTable()
        {
            this.ExpectKeyword("DROP");
            this.ExpectKeyword("TABLE");
            var drop = new SqlDropTable { Table = this.ExpectName() };
            this.ExpectEnd();
            return drop;
        }

        private SqlStatement ParseAlterTable()
        {
            this.ExpectKeyword("ALTER");
            this.ExpectKeyword("TABLE");
            string table = this.ExpectName();
            if (this.IsKeyword("HOLD") || this.IsKeyword("FREE"))
            {
                // HOLD and FREE manage temporary columns, which are not supported.
                throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
            }
            this.ExpectKeyword("ADD");
            var alter = new SqlAlterTableAdd { Table = table, Column = this.ParseColumnDefinition() };
            this.ExpectEnd();
            return alter;
        }

        /// <summary>A column name, its type, and its modifiers: NOT NULL, LOCALIZABLE, or TEMPORARY (not supported).</summary>
        private SqlColumnDefinition ParseColumnDefinition()
        {
            var column = new SqlColumnDefinition { Name = this.ExpectName() };
            SqlToken type = this.Current;
            if (type.Kind != SqlTokenKind.Identifier || type.Quoted)
            {
                throw SqlErrors.Syntax();
            }
            this.position++;
            switch (type.Text.ToUpperInvariant())
            {
                case "CHAR":
                case "CHARACTER":
                    column.Type = MsiColumnType.String;
                    column.Size = this.OptionalSize(255);
                    break;
                case "LONGCHAR":
                    column.Type = MsiColumnType.String;
                    column.Size = 0;
                    break;
                case "SHORT":
                case "INT":
                case "INTEGER":
                    column.Type = MsiColumnType.Integer;
                    column.Size = 2;
                    break;
                case "LONG":
                    column.Type = MsiColumnType.Integer;
                    column.Size = 4;
                    break;
                case "OBJECT":
                    column.Type = MsiColumnType.Binary;
                    break;
                default:
                    throw SqlErrors.Syntax();
            }

            while (true)
            {
                if (this.TryKeyword("NOT"))
                {
                    this.ExpectKeyword("NULL");
                    column.NotNull = true;
                }
                else if (this.TryKeyword("LOCALIZABLE"))
                {
                    column.Localizable = true;
                }
                else if (this.IsKeyword("TEMPORARY"))
                {
                    throw new MsiErrorException((uint) MsiError.CALL_NOT_IMPLEMENTED);
                }
                else
                {
                    break;
                }
            }
            if (column.Localizable && column.Type != MsiColumnType.String)
            {
                throw SqlErrors.Syntax();
            }
            return column;
        }

        /// <summary>An optional "(size)" after a type name. Without one the size is 0, which is unlimited.</summary>
        private int OptionalSize(int max)
        {
            if (!this.TrySymbol("("))
            {
                return 0;
            }
            SqlToken number = this.Current;
            if (number.Kind != SqlTokenKind.Integer || number.Number < 0 || number.Number > max)
            {
                throw SqlErrors.Syntax();
            }
            this.position++;
            this.ExpectSymbol(")");
            return number.Number;
        }

        private SqlInsert ParseInsert()
        {
            this.ExpectKeyword("INSERT");
            this.ExpectKeyword("INTO");
            var insert = new SqlInsert { Table = this.ExpectName() };
            this.ExpectSymbol("(");
            insert.Columns.Add(this.ExpectName());
            while (this.TrySymbol(","))
            {
                insert.Columns.Add(this.ExpectName());
            }
            this.ExpectSymbol(")");
            this.ExpectKeyword("VALUES");
            this.ExpectSymbol("(");
            insert.Values.Add(this.ParseValue());
            while (this.TrySymbol(","))
            {
                insert.Values.Add(this.ParseValue());
            }
            this.ExpectSymbol(")");
            this.ExpectEnd();
            if (insert.Columns.Count != insert.Values.Count)
            {
                throw SqlErrors.Syntax();
            }
            insert.MarkerCount = this.markers;
            return insert;
        }

        private SqlUpdate ParseUpdate()
        {
            this.ExpectKeyword("UPDATE");
            var update = new SqlUpdate { Table = this.ExpectName() };
            this.ExpectKeyword("SET");
            do
            {
                string column = this.ExpectName();
                this.ExpectSymbol("=");
                update.Assignments.Add((column, this.ParseValue()));
            }
            while (this.TrySymbol(","));
            if (this.TryKeyword("WHERE"))
            {
                update.Where = this.ParseOr();
            }
            this.ExpectEnd();
            update.MarkerCount = this.markers;
            return update;
        }

        private SqlDelete ParseDelete()
        {
            this.ExpectKeyword("DELETE");
            this.ExpectKeyword("FROM");
            var delete = new SqlDelete { Table = this.ExpectName() };
            if (this.TryKeyword("WHERE"))
            {
                delete.Where = this.ParseOr();
            }
            this.ExpectEnd();
            delete.MarkerCount = this.markers;
            return delete;
        }

        /// <summary>A value to store: a literal or a parameter marker, never a column.</summary>
        private SqlOperand ParseValue()
        {
            if (this.Current.Kind == SqlTokenKind.Identifier)
            {
                throw SqlErrors.Syntax();
            }
            return this.ParseOperand();
        }

        private void ExpectEnd()
        {
            if (this.Current.Kind != SqlTokenKind.End)
            {
                throw SqlErrors.Syntax();
            }
        }

        private SqlSelect ParseSelect()
        {
            this.ExpectKeyword("SELECT");
            var select = new SqlSelect { Distinct = this.TryKeyword("DISTINCT") };

            if (this.TrySymbol("*"))
            {
                select.Columns = null;
            }
            else
            {
                var columns = new List<SqlColumnRef> { this.ParseColumnRef() };
                while (this.TrySymbol(","))
                {
                    columns.Add(this.ParseColumnRef());
                }
                select.Columns = columns;
            }

            this.ExpectKeyword("FROM");
            select.Tables.Add(this.ExpectName());
            while (this.TrySymbol(","))
            {
                select.Tables.Add(this.ExpectName());
            }

            if (this.TryKeyword("WHERE"))
            {
                select.Where = this.ParseOr();
            }

            if (this.TryKeyword("ORDER"))
            {
                this.ExpectKeyword("BY");
                select.OrderBy.Add(this.ParseColumnRef());
                while (this.TrySymbol(","))
                {
                    select.OrderBy.Add(this.ParseColumnRef());
                }
            }

            this.ExpectEnd();
            select.MarkerCount = this.markers;
            return select;
        }

        private SqlCondition ParseOr()
        {
            SqlCondition left = this.ParseAnd();
            while (this.TryKeyword("OR"))
            {
                left = new SqlOrCondition(left, this.ParseAnd());
            }
            return left;
        }

        private SqlCondition ParseAnd()
        {
            SqlCondition left = this.ParsePrimary();
            while (this.TryKeyword("AND"))
            {
                left = new SqlAndCondition(left, this.ParsePrimary());
            }
            return left;
        }

        private SqlCondition ParsePrimary()
        {
            if (this.TrySymbol("("))
            {
                SqlCondition inner = this.ParseOr();
                this.ExpectSymbol(")");
                return inner;
            }

            SqlOperand left = this.ParseOperand();
            if (this.TryKeyword("IS"))
            {
                if (left is not SqlColumnOperand column)
                {
                    throw SqlErrors.Syntax();
                }
                bool negated = this.TryKeyword("NOT");
                this.ExpectKeyword("NULL");
                return new SqlNullCondition(column.Reference, negated);
            }

            SqlOperator op = this.ParseOperator();
            return new SqlCompareCondition(op, left, this.ParseOperand());
        }

        private SqlOperator ParseOperator()
        {
            if (this.Current.Kind == SqlTokenKind.Symbol)
            {
                switch (this.Current.Text)
                {
                    case "=":
                        this.position++;
                        return SqlOperator.Equal;
                    case "<>":
                        this.position++;
                        return SqlOperator.NotEqual;
                    case "<":
                        this.position++;
                        return SqlOperator.Less;
                    case "<=":
                        this.position++;
                        return SqlOperator.LessOrEqual;
                    case ">":
                        this.position++;
                        return SqlOperator.Greater;
                    case ">=":
                        this.position++;
                        return SqlOperator.GreaterOrEqual;
                }
            }
            throw SqlErrors.Syntax();
        }

        private SqlOperand ParseOperand()
        {
            SqlToken token = this.Current;
            switch (token.Kind)
            {
                case SqlTokenKind.Identifier:
                    return new SqlColumnOperand(this.ParseColumnRef());
                case SqlTokenKind.String:
                    this.position++;
                    return new SqlConstantOperand(token.Text);
                case SqlTokenKind.Integer:
                    this.position++;
                    return new SqlConstantOperand(token.Number);
                case SqlTokenKind.Marker:
                    this.position++;
                    return new SqlMarkerOperand(this.markers++);
                default:
                    throw SqlErrors.Syntax();
            }
        }

        private SqlColumnRef ParseColumnRef()
        {
            string first = this.ExpectName();
            if (this.TrySymbol("."))
            {
                return new SqlColumnRef(first, this.ExpectName());
            }
            return new SqlColumnRef(null, first);
        }

        private SqlToken Current => this.tokens[this.position];

        private bool IsKeyword(string keyword)
        {
            SqlToken token = this.Current;
            return token.Kind == SqlTokenKind.Identifier && !token.Quoted
                && string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryKeyword(string keyword)
        {
            if (this.IsKeyword(keyword))
            {
                this.position++;
                return true;
            }
            return false;
        }

        private void ExpectKeyword(string keyword)
        {
            if (!this.TryKeyword(keyword))
            {
                throw SqlErrors.Syntax();
            }
        }

        private bool TrySymbol(string symbol)
        {
            if (this.Current.Kind == SqlTokenKind.Symbol && this.Current.Text == symbol)
            {
                this.position++;
                return true;
            }
            return false;
        }

        private void ExpectSymbol(string symbol)
        {
            if (!this.TrySymbol(symbol))
            {
                throw SqlErrors.Syntax();
            }
        }

        private string ExpectName()
        {
            SqlToken token = this.Current;
            if (token.Kind != SqlTokenKind.Identifier || (!token.Quoted && IsReserved(token.Text)))
            {
                throw SqlErrors.Syntax();
            }
            this.position++;
            return token.Text;
        }

        private static bool IsReserved(string word)
        {
            switch (word.ToUpperInvariant())
            {
                case "SELECT":
                case "DISTINCT":
                case "FROM":
                case "WHERE":
                case "AND":
                case "OR":
                case "IS":
                case "NOT":
                case "NULL":
                case "ORDER":
                case "BY":
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>A SELECT bound to a database's tables, ready to execute with parameter values.</summary>
    internal sealed class BoundSelect
    {
        private readonly List<SqlTable> tables;
        private readonly int[]? projection;
        private readonly Func<object?[], object?[], bool>? filter;
        private readonly int[] orderKeys;
        private readonly bool distinct;
        private readonly List<SqlColumn> outputColumns;

        private BoundSelect(List<SqlTable> tables, int[]? projection, Func<object?[], object?[], bool>? filter,
            int[] orderKeys, bool distinct, List<SqlColumn> outputColumns, int markerCount)
        {
            this.tables = tables;
            this.projection = projection;
            this.filter = filter;
            this.orderKeys = orderKeys;
            this.distinct = distinct;
            this.outputColumns = outputColumns;
            this.MarkerCount = markerCount;
        }

        public int MarkerCount { get; }

        /// <summary>True for a query over one table with no WHERE, ORDER BY or DISTINCT. Such views can be modified.</summary>
        public bool IsSingleTableWithoutFilter =>
            this.tables.Count == 1 && this.filter == null && this.orderKeys.Length == 0 && !this.distinct;

        /// <summary>The one table of a single-table query.</summary>
        public SqlTable SingleTable => this.tables[0];

        /// <summary>For each output column, the index of its column in the table (single-table queries only).</summary>
        public int[] TableColumnIndexes => this.projection
            ?? Enumerable.Range(0, this.tables[0].Columns.Count).ToArray();

        /// <summary>Binds a WHERE condition over the columns of one table. The result takes a row and the markers.</summary>
        public static Func<object?[], object?[], bool> BindTableCondition(SqlCondition condition, SqlTable table)
        {
            int Resolve(SqlColumnRef reference)
            {
                int found = -1;
                int count = 0;
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    if (table.Columns[i].Name != reference.Name || (reference.Table != null && reference.Table != table.Name))
                    {
                        continue;
                    }
                    found = i;
                    count++;
                }
                if (count != 1)
                {
                    throw SqlErrors.Syntax();
                }
                return found;
            }
            return BindCondition(condition, Resolve);
        }

        /// <summary>The columns the query returns, known from the statement before it is executed.</summary>
        public IReadOnlyList<SqlColumn> OutputColumns => this.outputColumns;

        public static BoundSelect Bind(SqlSelect select, EngineDatabase database)
        {
            var tables = new List<SqlTable>();
            foreach (string name in select.Tables)
            {
                if (tables.Any(t => t.Name == name))
                {
                    throw SqlErrors.Syntax();
                }
                tables.Add(database.FindTable(name) ?? throw SqlErrors.Syntax());
            }

            // Every column of every FROM table, in join order. Offsets index into a joined row.
            var combined = new List<(string Table, SqlColumn Column)>();
            foreach (SqlTable table in tables)
            {
                foreach (SqlColumn column in table.Columns)
                {
                    combined.Add((table.Name, column));
                }
            }

            int Resolve(SqlColumnRef reference)
            {
                int found = -1;
                int count = 0;
                for (int i = 0; i < combined.Count; i++)
                {
                    (string table, SqlColumn column) = combined[i];
                    if (column.Name != reference.Name || (reference.Table != null && table != reference.Table))
                    {
                        continue;
                    }
                    found = i;
                    count++;
                }
                if (count != 1)
                {
                    throw SqlErrors.Syntax();
                }
                return found;
            }

            int[]? projection = null;
            List<SqlColumn> outputColumns;
            if (select.Columns == null)
            {
                outputColumns = combined.Select(c => c.Column).ToList();
            }
            else
            {
                projection = select.Columns.Select(Resolve).ToArray();
                outputColumns = projection.Select(offset => combined[offset].Column).ToList();
            }

            Func<object?[], object?[], bool>? filter = select.Where == null
                ? null
                : BindCondition(select.Where, Resolve);

            int[] orderKeys = select.OrderBy.Select(Resolve).ToArray();

            return new BoundSelect(tables, projection, filter, orderKeys, select.Distinct, outputColumns, select.MarkerCount);
        }

        public SqlResult Execute(object?[] markers)
        {
            if (markers.Length < this.MarkerCount)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
            object?[] parameters = markers.Take(this.MarkerCount).ToArray();

            IEnumerable<object?[]> rows = CrossJoin(this.tables);
            if (this.filter != null)
            {
                Func<object?[], object?[], bool> test = this.filter;
                rows = rows.Where(row => test(row, parameters));
            }

            List<object?[]> matched = rows.ToList();
            if (this.orderKeys.Length > 0)
            {
                int[] keys = this.orderKeys;
                matched = matched
                    .OrderBy(row => row, Comparer<object?[]>.Create((a, b) => CompareKeys(a, b, keys)))
                    .ToList();
            }

            IEnumerable<object?[]> output = matched;
            if (this.projection != null)
            {
                int[] columns = this.projection;
                output = matched.Select(row => columns.Select(offset => LazyBinary.Resolve(row[offset])).ToArray());
            }
            else
            {
                output = matched.Select(row => row.Select(LazyBinary.Resolve).ToArray());
            }
            if (this.distinct)
            {
                output = output.Distinct(RowComparer.Instance);
            }

            return new SqlResult(this.outputColumns, output.ToList());
        }

        private static IEnumerable<object?[]> CrossJoin(IReadOnlyList<SqlTable> tables)
        {
            IEnumerable<object?[]> joined = new[] { Array.Empty<object?>() };
            foreach (SqlTable table in tables)
            {
                SqlTable current = table;
                joined = joined.SelectMany(prefix => current.Rows.Select(row => prefix.Concat(row).ToArray()));
            }
            return joined;
        }

        private static int CompareKeys(object?[] a, object?[] b, int[] keys)
        {
            foreach (int key in keys)
            {
                int result = SqlValues.Order(a[key], b[key]);
                if (result != 0)
                {
                    return result;
                }
            }
            return 0;
        }

        private static Func<object?[], object?[], bool> BindCondition(SqlCondition condition, Func<SqlColumnRef, int> resolve)
        {
            switch (condition)
            {
                case SqlAndCondition andCondition:
                {
                    Func<object?[], object?[], bool> left = BindCondition(andCondition.Left, resolve);
                    Func<object?[], object?[], bool> right = BindCondition(andCondition.Right, resolve);
                    return (row, parameters) => left(row, parameters) && right(row, parameters);
                }
                case SqlOrCondition orCondition:
                {
                    Func<object?[], object?[], bool> left = BindCondition(orCondition.Left, resolve);
                    Func<object?[], object?[], bool> right = BindCondition(orCondition.Right, resolve);
                    return (row, parameters) => left(row, parameters) || right(row, parameters);
                }
                case SqlNullCondition nullCondition:
                {
                    int offset = resolve(nullCondition.Column);
                    bool negated = nullCondition.Negated;
                    return (row, parameters) => (row[offset] == null) != negated;
                }
                case SqlCompareCondition compare:
                {
                    Func<object?[], object?[], object?> left = BindOperand(compare.Left, resolve);
                    Func<object?[], object?[], object?> right = BindOperand(compare.Right, resolve);
                    SqlOperator op = compare.Operator;
                    return (row, parameters) => SqlValues.Compare(op, left(row, parameters), right(row, parameters));
                }
                default:
                    throw SqlErrors.Syntax();
            }
        }

        private static Func<object?[], object?[], object?> BindOperand(SqlOperand operand, Func<SqlColumnRef, int> resolve)
        {
            switch (operand)
            {
                case SqlColumnOperand column:
                {
                    int offset = resolve(column.Reference);
                    return (row, parameters) => row[offset];
                }
                case SqlConstantOperand constant:
                {
                    object? value = constant.Value;
                    return (row, parameters) => value;
                }
                case SqlMarkerOperand marker:
                {
                    int index = marker.Index;
                    return (row, parameters) => parameters[index];
                }
                default:
                    throw SqlErrors.Syntax();
            }
        }
    }

    internal static class SqlValues
    {
        /// <summary>Evaluates a comparison. Nulls never compare equal; strings support only = and &lt;&gt;.</summary>
        public static bool Compare(SqlOperator op, object? left, object? right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            if (left is byte[] || right is byte[] || left is LazyBinary || right is LazyBinary)
            {
                throw SqlErrors.Syntax();
            }
            if (left is int a && right is int b)
            {
                return Holds(op, a.CompareTo(b));
            }
            if (left is string s && right is string t)
            {
                if (op != SqlOperator.Equal && op != SqlOperator.NotEqual)
                {
                    throw SqlErrors.Syntax();
                }
                return Holds(op, string.CompareOrdinal(s, t));
            }
            throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
        }

        /// <summary>Orders values for ORDER BY: nulls first, then integers and strings.</summary>
        public static int Order(object? left, object? right)
        {
            if (left == null || right == null)
            {
                return (left == null ? 0 : 1) - (right == null ? 0 : 1);
            }
            if (left is int a && right is int b)
            {
                return a.CompareTo(b);
            }
            if (left is string s && right is string t)
            {
                return string.CompareOrdinal(s, t);
            }
            return 0;
        }

        public static bool ValueEquals(object? left, object? right)
        {
            left = LazyBinary.Resolve(left);
            right = LazyBinary.Resolve(right);
            if (left == null || right == null)
            {
                return left == null && right == null;
            }
            if (left is byte[] x && right is byte[] y)
            {
                return x.AsSpan().SequenceEqual(y);
            }
            return left.Equals(right);
        }

        public static int ValueHash(object? value)
        {
            value = LazyBinary.Resolve(value);
            return value switch
            {
                null => 0,
                byte[] bytes => bytes.Length,
                _ => value.GetHashCode(),
            };
        }

        private static bool Holds(SqlOperator op, int comparison)
        {
            switch (op)
            {
                case SqlOperator.Equal:
                    return comparison == 0;
                case SqlOperator.NotEqual:
                    return comparison != 0;
                case SqlOperator.Less:
                    return comparison < 0;
                case SqlOperator.LessOrEqual:
                    return comparison <= 0;
                case SqlOperator.Greater:
                    return comparison > 0;
                default:
                    return comparison >= 0;
            }
        }
    }

    internal sealed class RowComparer : IEqualityComparer<object?[]>
    {
        public static readonly RowComparer Instance = new RowComparer();

        public bool Equals(object?[]? x, object?[]? y)
        {
            if (x == null || y == null)
            {
                return x == y;
            }
            if (x.Length != y.Length)
            {
                return false;
            }
            for (int i = 0; i < x.Length; i++)
            {
                if (!SqlValues.ValueEquals(x[i], y[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(object?[] obj)
        {
            int hash = 17;
            foreach (object? value in obj)
            {
                hash = unchecked((hash * 31) + SqlValues.ValueHash(value));
            }
            return hash;
        }
    }
}
