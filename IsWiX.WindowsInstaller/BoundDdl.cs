namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    /// <summary>
    /// CREATE TABLE, DROP TABLE and ALTER TABLE ... ADD. The statement is checked for its own form when it is bound;
    /// whether the table exists, or a column can be added, is checked when it runs.
    /// </summary>
    internal sealed class BoundDdl
    {
        private const int MaxNameLength = 31;

        private readonly Database database;
        private readonly SqlStatement statement;

        private BoundDdl(Database database, SqlStatement statement)
        {
            this.database = database;
            this.statement = statement;
        }

        public int MarkerCount => 0;

        public static BoundDdl Bind(SqlStatement statement, Database database)
        {
            switch (statement)
            {
                case SqlCreateTable create:
                    CheckTableName(create.Table);
                    if (create.Columns.Count == 0
                        || create.Columns.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != create.Columns.Count
                        || create.Columns.Count(c => c.Type == MsiColumnType.Binary) > 1)
                    {
                        throw SqlErrors.Syntax();
                    }
                    // Primary key columns come first, in the order they are listed.
                    for (int i = 0; i < create.PrimaryKeys.Count; i++)
                    {
                        if (create.Columns.Count <= i || create.Columns[i].Name != create.PrimaryKeys[i])
                        {
                            throw SqlErrors.Syntax();
                        }
                    }
                    break;
                case SqlDropTable drop:
                    CheckTableName(drop.Table);
                    break;
                case SqlAlterTableAdd alter:
                    CheckTableName(alter.Table);
                    break;
                default:
                    throw SqlErrors.Syntax();
            }
            return new BoundDdl(database, statement);
        }

        private static void CheckTableName(string name)
        {
            if (name.Length == 0 || name.Length > MaxNameLength)
            {
                throw SqlErrors.Syntax();
            }
        }

        public void Execute(object?[] markers)
        {
            this.database.RequireWritable();
            switch (this.statement)
            {
                case SqlCreateTable create:
                {
                    var columns = create.Columns
                        .Select(c => new SqlColumn(create.Table, c.Name, c.Type, c.Size, !c.NotNull,
                            create.PrimaryKeys.Contains(c.Name), c.Localizable))
                        .ToList();
                    this.database.CreateTable(new MutableTable(create.Table, columns, new List<object?[]>()));
                    break;
                }
                case SqlDropTable drop:
                    this.database.DropTable(drop.Table);
                    break;
                case SqlAlterTableAdd alter:
                    this.database.AddColumn(alter.Table, new SqlColumn(alter.Table, alter.Column.Name,
                        alter.Column.Type, alter.Column.Size, !alter.Column.NotNull, false, alter.Column.Localizable));
                    break;
            }
            this.database.Changed();
        }
    }
}
