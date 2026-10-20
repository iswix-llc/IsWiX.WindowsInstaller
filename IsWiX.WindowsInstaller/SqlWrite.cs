// Changes to the in-memory tables: INSERT, UPDATE and DELETE statements, and MsiViewModify on a
// single-table view. Every change is checked against the column definitions and the primary key before
// it is applied, so a failed statement leaves the table as it was.
namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using IsWiX.WindowsInstaller.Format;

}
