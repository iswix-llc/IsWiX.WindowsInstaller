namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Specifies the different patch states for <see cref="PatchInstallation.GetPatches(string, string, string, UserContexts, PatchStates)"/>.
    /// </summary>
    [Flags]
    public enum PatchStates : int
    {
        /// <summary>Invalid value.</summary>
        None = 0,

        /// <summary>Patches applied to a product.</summary>
        Applied = 1,

        /// <summary>Patches that are superseded by other patches.</summary>
        Superseded = 2,

        /// <summary>Patches that are obsolesced by other patches.</summary>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Obsoleted")]
        Obsoleted = 4,

        /// <summary>Patches that are registered to a product but not applied.</summary>
        Registered = 8,

        /// <summary>All valid patch states.</summary>
        All = (Applied | Superseded | Obsoleted | Registered)
    }

    /// <summary>
    /// Transform error conditions available for <see cref="Database.CreateTransformSummaryInfo"/> or
    /// <see cref="Database.ApplyTransform(string,TransformErrors)"/>.
    /// </summary>
    [Flags]
    public enum TransformErrors : int
    {
        /// <summary>No error conditions.</summary>
        None             = 0x0000,

        /// <summary>Adding a row that already exists.</summary>
        AddExistingRow   = 0x0001,

        /// <summary>Deleting a row that doesn't exist.</summary>
        DelMissingRow    = 0x0002,

        /// <summary>Adding a table that already exists.</summary>
        AddExistingTable = 0x0004,

        /// <summary>Deleting a table that doesn't exist.</summary>
        DelMissingTable  = 0x0008,

        /// <summary>Updating a row that doesn't exist.</summary>
        UpdateMissingRow = 0x0010,

        /// <summary>Transform and database code pages do not match and neither code page is neutral.</summary>
        ChangeCodePage   = 0x0020,

        /// <summary>Create the temporary _TransformView table when applying the transform.</summary>
        ViewTransform    = 0x0100,
    }

    /// <summary>
    /// Transform validation flags available for <see cref="Database.CreateTransformSummaryInfo"/>.
    /// </summary>
    [Flags]
    public enum TransformValidations : int
    {
        /// <summary>Validate no properties.</summary>
        None                       = 0x0000,

        /// <summary>Default language must match base database.</summary>
        Language                   = 0x0001,

        /// <summary>Product must match base database.</summary>
        Product                    = 0x0002,

        /// <summary>Check major version only.</summary>
        MajorVersion               = 0x0008,

        /// <summary>Check major and minor versions only.</summary>
        MinorVersion               = 0x0010,

        /// <summary>Check major, minor, and update versions.</summary>
        UpdateVersion              = 0x0020,

        /// <summary>Installed version &lt; base version.</summary>
        NewLessBaseVersion         = 0x0040,

        /// <summary>Installed version &lt;= base version.</summary>
        NewLessEqualBaseVersion    = 0x0080,

        /// <summary>Installed version = base version.</summary>
        NewEqualBaseVersion        = 0x0100,

        /// <summary>Installed version &gt;= base version.</summary>
        NewGreaterEqualBaseVersion = 0x0200,

        /// <summary>Installed version &gt; base version.</summary>
        NewGreaterBaseVersion      = 0x0400,

        /// <summary>UpgradeCode must match base database.</summary>
        UpgradeCode                = 0x0800,
    }

    /// <summary>
    /// Defines the type of error encountered by the <see cref="View.Validate"/>, <see cref="View.ValidateNew"/>,
    /// or <see cref="View.ValidateFields"/> methods of the <see cref="View"/> class.
    /// </summary>
    public enum ValidationError : int
    {
        /*
        InvalidArg        = -3,
        MoreData          = -2,
        FunctionError     = -1,
        */

        /// <summary>No error.</summary>
        None              = 0,

        /// <summary>The new record duplicates primary keys of the existing record in a table.</summary>
        DuplicateKey      = 1,

        /// <summary>There are no null values allowed, or the column is about to be deleted but is referenced by another row.</summary>
        Required          = 2,

        /// <summary>The corresponding record in a foreign table was not found.</summary>
        BadLink           = 3,

        /// <summary>The data is greater than the maximum value allowed.</summary>
        Overflow          = 4,

        /// <summary>The data is less than the minimum value allowed.</summary>
        Underflow         = 5,

        /// <summary>The data is not a member of the values permitted in the set.</summary>
        NotInSet          = 6,

        /// <summary>An invalid version string was supplied.</summary>
        BadVersion        = 7,

        /// <summary>The case was invalid. The case must be all uppercase or all lowercase.</summary>
        BadCase           = 8,

        /// <summary>An invalid GUID was supplied.</summary>
        BadGuid           = 9,

        /// <summary>An invalid wildcard file name was supplied, or the use of wildcards was invalid.</summary>
        BadWildcard       = 10,

        /// <summary>An invalid identifier was supplied.</summary>
        BadIdentifier     = 11,

        /// <summary>Invalid language IDs were supplied.</summary>
        BadLanguage       = 12,

        /// <summary>An invalid file name was supplied.</summary>
        BadFileName       = 13,

        /// <summary>An invalid path was supplied.</summary>
        BadPath           = 14,

        /// <summary>An invalid conditional statement was supplied.</summary>
        BadCondition      = 15,

        /// <summary>An invalid format string was supplied.</summary>
        BadFormatted      = 16,

        /// <summary>An invalid template string was supplied.</summary>
        BadTemplate       = 17,

        /// <summary>An invalid string was supplied in the DefaultDir column of the Directory table.</summary>
        BadDefaultDir     = 18,

        /// <summary>An invalid registry path string was supplied.</summary>
        BadRegPath        = 19,

        /// <summary>An invalid string was supplied in the CustomSource column of the CustomAction table.</summary>
        BadCustomSource   = 20,

        /// <summary>An invalid property string was supplied.</summary>
        BadProperty       = 21,

        /// <summary>The _Validation table is missing a reference to a column.</summary>
        MissingData       = 22,

        /// <summary>The category column of the _Validation table for the column is invalid.</summary>
        BadCategory       = 23,

        /// <summary>The table in the Keytable column of the _Validation table was not found or loaded.</summary>
        BadKeyTable       = 24,

        /// <summary>The value in the MaxValue column of the _Validation table is less than the value in the MinValue column.</summary>
        BadMaxMinValues   = 25,

        /// <summary>An invalid cabinet name was supplied.</summary>
        BadCabinet        = 26,

        /// <summary>An invalid shortcut target name was supplied.</summary>
        BadShortcut       = 27,

        /// <summary>The string is too long for the length specified by the column definition.</summary>
        StringOverflow    = 28,

        /// <summary>An invalid localization attribute was supplied. (Primary keys cannot be localized.)</summary>
        BadLocalizeAttrib = 29
    }
}
