namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
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
}
