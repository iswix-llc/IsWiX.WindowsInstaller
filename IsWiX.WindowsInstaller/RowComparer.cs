namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

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
