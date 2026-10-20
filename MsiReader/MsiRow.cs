using System.Globalization;

namespace MsiReader;

/// <summary>A row of an <see cref="MsiTable"/>.</summary>
public sealed class MsiRow
{
    private readonly object?[] _values;

    internal MsiRow(MsiTable table, int index, object?[] values)
    {
        Table = table;
        Index = index;
        _values = values;
    }

    public MsiTable Table { get; }

    /// <summary>Zero-based row index within the table.</summary>
    public int Index { get; }

    /// <summary>
    /// The value of a column: a <see cref="string"/> for string columns, an <see cref="int"/>
    /// for integer columns, a <c>byte[]</c> for binary columns, or <c>null</c>.
    /// Binary values are read from the database on each access.
    /// </summary>
    public object? this[string column]
    {
        get
        {
            var col = Table.GetColumn(column);
            return col.Type == MsiColumnType.Binary ? GetBinary(col) : _values[col.Number - 1];
        }
    }

    public string? GetString(string column)
    {
        var col = Table.GetColumn(column);
        if (col.Type != MsiColumnType.String)
            throw new InvalidOperationException($"Column '{col}' is not a string column.");
        return (string?)_values[col.Number - 1];
    }

    public int? GetInt(string column)
    {
        var col = Table.GetColumn(column);
        if (col.Type != MsiColumnType.Integer)
            throw new InvalidOperationException($"Column '{col}' is not an integer column.");
        return (int?)_values[col.Number - 1];
    }

    public byte[]? GetBinary(string column) => GetBinary(Table.GetColumn(column));

    private byte[]? GetBinary(MsiColumn col)
    {
        if (col.Type != MsiColumnType.Binary)
            throw new InvalidOperationException($"Column '{col}' is not a binary column.");

        // The stream is named "<Table>.<key1>.<key2>..." using the primary-key values.
        var parts = new List<string> { Table.Name };
        foreach (var key in Table.Columns.Where(c => c.IsPrimaryKey))
        {
            object? v = _values[key.Number - 1];
            if (v is null)
                return null;
            parts.Add(v is int i ? i.ToString(CultureInfo.InvariantCulture) : (string)v);
        }
        return Table.Database.ReadStream(string.Join(".", parts));
    }

    public override string ToString() => $"{Table.Name}[{Index}]";
}
