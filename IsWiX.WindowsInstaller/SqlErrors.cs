namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;
    internal static class SqlErrors
    {
        public static MsiErrorException Syntax()
        {
            return new MsiErrorException((uint) MsiError.BAD_QUERY_SYNTAX);
        }
    }
}
