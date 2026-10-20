namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
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
}
