namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

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
}
