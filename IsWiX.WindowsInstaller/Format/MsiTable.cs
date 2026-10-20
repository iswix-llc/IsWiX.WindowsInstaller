using System.Diagnostics.CodeAnalysis;

namespace IsWiX.WindowsInstaller.Format;

/// <summary>A table loaded from an MSI database.</summary>
internal sealed class MsiTable
{
    private readonly MsiDatabase _database;

    internal MsiTable(MsiDatabase database, string name, IReadOnlyList<MsiColumn> columns, IReadOnlyList<MsiRow> rows)
    {
        _database = database;
        Name = name;
        Columns = columns;
        Rows = rows;
    }

    public string Name { get; }
    public IReadOnlyList<MsiColumn> Columns { get; }
    public IReadOnlyList<MsiRow> Rows { get; }

    internal MsiDatabase Database => _database;

    /// <summary>Finds a column by name (case-insensitive, as MSI does).</summary>
    public bool TryGetColumn(string name, [NotNullWhen(true)] out MsiColumn? column)
    {
        foreach (var c in Columns)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                column = c;
                return true;
            }
        }
        column = null;
        return false;
    }

    /// <summary>Finds a column by name, throwing if it does not exist.</summary>
    public MsiColumn GetColumn(string name) =>
        TryGetColumn(name, out var c)
            ? c
            : throw new KeyNotFoundException($"Table '{Name}' has no column '{name}'.");

    public override string ToString() => $"{Name} ({Rows.Count} rows)";
}
