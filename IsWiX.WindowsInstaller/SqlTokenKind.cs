namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

    internal enum SqlTokenKind
    {
        Identifier,
        String,
        Integer,
        Marker,
        Symbol,
        End,
    }
}
