namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Specifies the modify mode for <see cref="View.Modify"/>.
    /// </summary>
    public enum ViewModifyMode : int
    {
        /// <summary>
        /// Refreshes the information in the supplied record without changing the position
        /// in the result set and without affecting subsequent fetch operations.
        /// </summary>
        Seek             = -1,

        /// <summary>Refreshes the data in a Record.</summary>
        Refresh          = 0,

        /// <summary>Inserts a Record into the view.</summary>
        Insert           = 1,

        /// <summary>Updates the View with new data from the Record.</summary>
        Update           = 2,

        /// <summary>Updates or inserts a Record into the View.</summary>
        Assign           = 3,

        /// <summary>Updates or deletes and inserts a Record into the View.</summary>
        Replace          = 4,

        /// <summary>Inserts or validates a record.</summary>
        Merge            = 5,

        /// <summary>Deletes a Record from the View.</summary>
        Delete           = 6,

        /// <summary>Inserts a Record into the View.  The inserted data is not persistent.</summary>
        InsertTemporary  = 7,

        /// <summary>Validates a record.</summary>
        Validate         = 8,

        /// <summary>Validates a new record.</summary>
        [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
        ValidateNew      = 9,

        /// <summary>Validates fields of a fetched or new record. Can validate one or more fields of an incomplete record.</summary>
        ValidateField    = 10,

        /// <summary>Validates a record that will be deleted later.</summary>
        ValidateDelete   = 11,
    }
}
