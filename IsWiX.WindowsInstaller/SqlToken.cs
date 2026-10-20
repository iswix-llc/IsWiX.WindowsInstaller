namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    internal sealed record SqlToken(SqlTokenKind Kind, string Text, bool Quoted = false, int Number = 0);
}
