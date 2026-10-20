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
    using IsWiX.WindowsInstaller.Format;
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
}
