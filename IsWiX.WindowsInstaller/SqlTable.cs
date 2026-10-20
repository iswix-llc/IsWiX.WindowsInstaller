namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    internal sealed record SqlTable(string Name, IReadOnlyList<SqlColumn> Columns, IReadOnlyList<object?[]> Rows);
}
