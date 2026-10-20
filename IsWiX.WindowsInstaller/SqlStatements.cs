namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
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
}
