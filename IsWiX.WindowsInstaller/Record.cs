namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// The Record object is a container for holding and transferring a variable number of values.
    /// Fields within the record are numerically indexed and can contain strings, integers, streams,
    /// and null values. Record fields are indexed starting with 1.  Field 0 is a special format field.
    /// </summary>
    /// <remarks><p>
    /// Most methods on the Record class have overloads that allow using either a number
    /// or a name to designate a field. However note that field names only exist when the
    /// Record is directly returned from a query on a database. For other records, attempting
    /// to access a field by name will result in an InvalidOperationException.
    /// </p></remarks>
    public class Record : IDisposable
    {

        private bool closed;

        /// <summary>
        /// Checks if the handle is closed. When closed, method calls on the handle object may throw an <see cref="InvalidHandleException"/>.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                return this.closed;
            }
        }

        /// <summary>
        /// Throws <see cref="InvalidHandleException"/> if the record has been closed.
        /// </summary>
        internal void ThrowIfClosed()
        {
            if (this.closed)
            {
                throw new InvalidHandleException();
            }
        }

        /// <summary>
        /// Closes the handle. After closing a handle, further method calls may throw an <see cref="InvalidHandleException"/>.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Closes the handle. This method is merely an alias for the <see cref="Dispose()"/> method.
        /// </summary>
        public void Close()
        {
            this.Dispose();
        }

        /// <summary>Releases the handle. Subclasses may override to release their own resources first.</summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.closed = true;
            }
        }
        private readonly object?[] fields;
        private readonly Dictionary<uint, int> readOffsets = new Dictionary<uint, int>();
        private View view;
        private bool isFormatStringInvalid;

        /// <summary>
        /// IsFormatStringInvalid is set from several View methods that invalidate the FormatString
        /// and used to determine behavior during Record.ToString().
        /// </summary>
        internal protected bool IsFormatStringInvalid
        {
            set { this.isFormatStringInvalid = value; }

            get { return this.isFormatStringInvalid; }
        }

        /// <summary>
        /// Creates a new record object with the requested number of fields.
        /// </summary>
        /// <param name="fieldCount">Required number of fields, which may be 0.
        /// The maximum number of fields in a record is limited to 65535.</param>
        /// <remarks><p>
        /// The Record object should be <see cref="Close"/>d after use.
        /// It is best that the handle be closed manually as soon as it is no longer
        /// needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msicreaterecord.asp">MsiCreateRecord</a>
        /// </p></remarks>
        public Record(int fieldCount)
            : this(fieldCount, (View) null)
        {
        }

        /// <summary>
        /// Creates a new record object, providing values for an arbitrary number of fields.
        /// </summary>
        /// <param name="fields">The values of the record fields.  The parameters should be of type Int16, Int32 or String</param>
        /// <remarks><p>
        /// The Record object should be <see cref="Close"/>d after use.
        /// It is best that the handle be closed manually as soon as it is no longer
        /// needed, as leaving lots of unused handles open can degrade performance.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msicreaterecord.asp">MsiCreateRecord</a>
        /// </p></remarks>
        public Record(params object[] fields)
            : this(fields.Length)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                this[i + 1] = fields[i];
            }
        }

        internal Record(int fieldCount, View view)
        {
            this.fields = new object?[fieldCount + 1];
            this.view = view;
        }

        internal Record(IReadOnlyList<object?> values, View view)
            : this(values.Count, view)
        {
            for (int i = 0; i < values.Count; i++)
            {
                this.fields[i + 1] = values[i];
            }
        }

        /// <summary>The value of a field, without the format-string checks the public indexer makes.</summary>
        internal object? ValueAt(uint field)
        {
            this.CheckField(field);
            return this.fields[field];
        }

        internal void SetValue(uint field, object? value)
        {
            this.CheckField(field);
            this.fields[field] = value;
            this.readOffsets.Remove(field);
        }

        internal void ClearValues()
        {
            for (int i = 0; i < this.fields.Length; i++)
            {
                this.fields[i] = null;
            }
            this.readOffsets.Clear();
        }

        internal int ReadOffset(uint field)
        {
            return this.readOffsets.TryGetValue(field, out int offset) ? offset : 0;
        }

        internal void SetReadOffset(uint field, int offset)
        {
            this.readOffsets[field] = offset;
        }

        /// <summary>Returns fields 1..n, the parameter values a query is executed with.</summary>
        internal object?[] Values()
        {
            var values = new object?[this.FieldCount];
            Array.Copy(this.fields, 1, values, 0, this.FieldCount);
            return values;
        }

        internal string ReadString(uint field)
        {
            switch (this.ValueAt(field))
            {
                case null:
                    return string.Empty;
                case string text:
                    return text;
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                default:
                    throw new MsiErrorException((uint) MsiError.DATATYPE_MISMATCH);
            }
        }

        internal int ReadInteger(uint field)
        {
            switch (this.ValueAt(field))
            {
                case int number:
                    return number;
                case string text when int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed):
                    return parsed;
                default:
                    return int.MinValue;
            }
        }

        private void CheckField(uint field)
        {
            if (field > this.FieldCount)
            {
                throw new MsiErrorException((uint) MsiError.INVALID_PARAMETER);
            }
        }

        /// <summary>
        /// Gets the number of fields in a record.
        /// </summary>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordgetfieldcount.asp">MsiRecordGetFieldCount</a>
        /// </p></remarks>
        public int FieldCount
        {
            get
            {
                this.ThrowIfClosed();
                return this.fields.Length - 1;
            }
        }

        /// <summary>
        /// Gets or sets field 0 of the Record, which is the format string.
        /// </summary>
        public string FormatString
        {
            get { return this.GetString(0); }
            set { this.SetString(0, value); }
        }

        /// <summary>
        /// Gets or sets a record field value.
        /// </summary>
        /// <param name="fieldName">Specifies the name of the field of the Record to get or set.</param>
        /// <exception cref="ArgumentOutOfRangeException">The name does not match any known field of the Record.</exception>
        /// <remarks><p>
        /// When getting a field, the type of the object returned depends on the type of the Record field.
        /// The object will be one of: Int16, Int32, String, Stream, or null.
        /// </p><p>
        /// When setting a field, the type of the object provided will be converted to match the View
        /// query that returned the record, or if Record was not returned from a view then the type of
        /// the object provided will determine the type of the Record field. The object should be one of:
        /// Int16, Int32, String, Stream, or null.
        /// </p></remarks>
        public object this[string fieldName]
        {
            get
            {
                int field = this.FindColumn(fieldName);
                return this[field];
            }

            set
            {
                int field = this.FindColumn(fieldName);
                this[field] = value;
            }
        }

        /// <summary>
        /// Gets or sets a record field value.
        /// </summary>
        /// <param name="field">Specifies the field of the Record to get or set.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Record fields are indexed starting with 1.  Field 0 is a special format field.
        /// </p><p>
        /// When getting a field, the type of the object returned depends on the type of the Record field.
        /// The object will be one of: Int16, Int32, String, Stream, or null.  If the Record was returned
        /// from a View, the type will match that of the field from the View query.  Otherwise, the type
        /// will match the type of the last value set for the field.
        /// </p><p>
        /// When setting a field, the type of the object provided will be converted to match the View
        /// query that returned the Record, or if Record was not returned from a View then the type of
        /// the object provided will determine the type of the Record field. The object should be one of:
        /// Int16, Int32, String, Stream, or null.
        /// </p><p>
        /// The type-specific getters and setters are slightly more efficient than this property, since
        /// they don't have to do the extra work to infer the value's type every time.
        /// </p><p>
        /// Win32 MSI APIs:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordgetinteger.asp">MsiRecordGetInteger</a>,
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordgetstring.asp">MsiRecordGetString</a>,
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetinteger.asp">MsiRecordSetInteger</a>,
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetstring.asp">MsiRecordSetString</a>
        /// </p></remarks>
        public object this[int field]
        {
            get
            {
                if (field == 0)
                {
                    return this.GetString(0);
                }
                else
                {
                    Type valueType = null;
                    if (this.view != null)
                    {
                        this.CheckRange(field);

                        valueType = this.view.Columns[field - 1].Type;
                    }

                    if (valueType == null || valueType == typeof(String))
                    {
                        return this.GetString(field);
                    }
                    else if (valueType == typeof(Stream))
                    {
                        return this.IsNull(field) ? null : new RecordStream(this, field);
                    }
                    else
                    {
                        int? value = this.GetNullableInteger(field);
                        return value.HasValue ? (object) value.Value : null;
                    }
                }
            }

            set
            {
                if (field == 0)
                {
                    if (value == null)
                    {
                        value = String.Empty;
                    }

                    this.SetString(0, value.ToString());
                }
                else if (value == null)
                {
                    this.SetNullableInteger(field, null);
                }
                else
                {
                    Type valueType = value.GetType();
                    if (valueType == typeof(Int32) || valueType == typeof(Int16))
                    {
                        this.SetInteger(field, (int) value);
                    }
                    else if (valueType.IsSubclassOf(typeof(Stream)))
                    {
                        this.SetStream(field, (Stream) value);
                    }
                    else
                    {
                        this.SetString(field, value.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// Reports whether a record field is null.
        /// </summary>
        /// <param name="field">Specifies the field to check.</param>
        /// <returns>True if the field is null, false otherwise.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordisnull.asp">MsiRecordIsNull</a>
        /// </p></remarks>
        public bool IsNull(int field)
        {
            this.CheckRange(field);
            return MsiApi.MsiRecordIsNull(this, (uint) field);
        }

        /// <summary>
        /// Reports whether a record field is null.
        /// </summary>
        /// <param name="fieldName">Specifies the field to check.</param>
        /// <returns>True if the field is null, false otherwise.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        public bool IsNull(string fieldName)
        {
            int field = this.FindColumn(fieldName);
            return this.IsNull(field);
        }

        /// <summary>
        /// Gets the length of a record field. The count does not include the terminating null.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// The returned data size is 0 if the field is null, non-existent,
        /// or an internal object pointer. The method also returns 0 if the handle is not a valid
        /// Record handle.
        /// </p><p>
        /// If the data is in integer format, the property returns 2 or 4.
        /// </p><p>
        /// If the data is in string format, the property returns the character count
        /// (not including the NULL terminator).
        /// </p><p>
        /// If the data is in stream format, the property returns the byte count.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecorddatasize.asp">MsiRecordDataSize</a>
        /// </p></remarks>
        public int GetDataSize(int field)
        {
            this.CheckRange(field);
            return (int) MsiApi.MsiRecordDataSize(this, (uint) field);
        }

        /// <summary>
        /// Gets the length of a record field. The count does not include the terminating null.
        /// </summary>
        /// <param name="fieldName">Specifies the field to check.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <remarks><p>The returned data size is 0 if the field is null, non-existent,
        /// or an internal object pointer. The method also returns 0 if the handle is not a valid
        /// Record handle.
        /// </p><p>
        /// If the data is in integer format, the property returns 2 or 4.
        /// </p><p>
        /// If the data is in string format, the property returns the character count
        /// (not including the NULL terminator).
        /// </p><p>
        /// If the data is in stream format, the property returns the byte count.
        /// </p></remarks>
        public int GetDataSize(string fieldName)
        {
            int field = this.FindColumn(fieldName);
            return this.GetDataSize(field);
        }

        /// <summary>
        /// Gets a field value as an integer.
        /// </summary>
        /// <param name="field">Specifies the field to retrieve.</param>
        /// <returns>Integer value of the field, or 0 if the field is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordgetinteger.asp">MsiRecordGetInteger</a>
        /// </p></remarks>
        /// <seealso cref="GetNullableInteger(int)"/>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "integer")]
        public int GetInteger(int field)
        {
            this.CheckRange(field);

            int value = MsiApi.MsiRecordGetInteger(this, (uint) field);
            if (value == Int32.MinValue)  // MSI_NULL_INTEGER
            {
                return 0;
            }
            return value;
        }

        /// <summary>
        /// Gets a field value as an integer.
        /// </summary>
        /// <param name="fieldName">Specifies the field to retrieve.</param>
        /// <returns>Integer value of the field, or 0 if the field is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <seealso cref="GetNullableInteger(string)"/>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "integer")]
        public int GetInteger(string fieldName)
        {
            int field = this.FindColumn(fieldName);
            return this.GetInteger(field);
        }

        /// <summary>
        /// Gets a field value as an integer.
        /// </summary>
        /// <param name="field">Specifies the field to retrieve.</param>
        /// <returns>Integer value of the field, or null if the field is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordgetinteger.asp">MsiRecordGetInteger</a>
        /// </p></remarks>
        /// <seealso cref="GetInteger(int)"/>
        public int? GetNullableInteger(int field)
        {
            this.CheckRange(field);

            int value = MsiApi.MsiRecordGetInteger(this, (uint) field);
            if (value == Int32.MinValue)  // MSI_NULL_INTEGER
            {
                return null;
            }
            return value;
        }

        /// <summary>
        /// Gets a field value as an integer.
        /// </summary>
        /// <param name="fieldName">Specifies the field to retrieve.</param>
        /// <returns>Integer value of the field, or null if the field is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <seealso cref="GetInteger(string)"/>
        public int? GetNullableInteger(string fieldName)
        {
            int field = this.FindColumn(fieldName);
            return this.GetInteger(field);
        }

        /// <summary>
        /// Sets the value of a field to an integer.
        /// </summary>
        /// <param name="field">Specifies the field to set.</param>
        /// <param name="value">new value of the field</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetinteger.asp">MsiRecordSetInteger</a>
        /// </p></remarks>
        /// <seealso cref="SetNullableInteger(int,int?)"/>
        public void SetInteger(int field, int value)
        {
            this.CheckRange(field);

            MsiApi.MsiRecordSetInteger(this, (uint) field, value);
        }

        /// <summary>
        /// Sets the value of a field to an integer.
        /// </summary>
        /// <param name="fieldName">Specifies the field to set.</param>
        /// <param name="value">new value of the field</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <seealso cref="SetNullableInteger(string,int?)"/>
        public void SetInteger(string fieldName, int value)
        {
            int field = this.FindColumn(fieldName);
            this.SetInteger(field, value);
        }

        /// <summary>
        /// Sets the value of a field to a nullable integer.
        /// </summary>
        /// <param name="field">Specifies the field to set.</param>
        /// <param name="value">new value of the field</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetinteger.asp">MsiRecordSetInteger</a>
        /// </p></remarks>
        /// <seealso cref="SetInteger(int,int)"/>
        public void SetNullableInteger(int field, int? value)
        {
            this.CheckRange(field);

            MsiApi.MsiRecordSetInteger(this, (uint) field, value.HasValue ? (int) value : Int32.MinValue);
        }

        /// <summary>
        /// Sets the value of a field to a nullable integer.
        /// </summary>
        /// <param name="fieldName">Specifies the field to set.</param>
        /// <param name="value">new value of the field</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <seealso cref="SetInteger(string,int)"/>
        public void SetNullableInteger(string fieldName, int? value)
        {
            int field = this.FindColumn(fieldName);
            this.SetNullableInteger(field, value);
        }

        /// <summary>
        /// Gets a field value as a string.
        /// </summary>
        /// <param name="field">Specifies the field to retrieve.</param>
        /// <returns>String value of the field, or an empty string if the field is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordgetstring.asp">MsiRecordGetString</a>
        /// </p></remarks>
        public string GetString(int field)
        {
            this.CheckRange(field);

            return MsiApi.MsiRecordGetString(this, (uint) field);
        }

        /// <summary>
        /// Gets a field value as a string.
        /// </summary>
        /// <param name="fieldName">Specifies the field to retrieve.</param>
        /// <returns>String value of the field, or an empty string if the field is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        public string GetString(string fieldName)
        {
            int field = this.FindColumn(fieldName);
            return this.GetString(field);
        }

        /// <summary>
        /// Sets the value of a field to a string.
        /// </summary>
        /// <param name="field">Specifies the field to set.</param>
        /// <param name="value">new value of the field</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetstring.asp">MsiRecordSetString</a>
        /// </p></remarks>
        public void SetString(int field, string value)
        {
            this.CheckRange(field);

            if (value == null)
            {
                value = String.Empty;
            }

            MsiApi.MsiRecordSetString(this, (uint) field, value);

            // If we set the FormatString manually, then it should be valid again
            if (field == 0)
            {
                this.IsFormatStringInvalid = false;
            }
        }

        /// <summary>
        /// Sets the value of a field to a string.
        /// </summary>
        /// <param name="fieldName">Specifies the field to set.</param>
        /// <param name="value">new value of the field</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        public void SetString(string fieldName, string value)
        {
            int field = this.FindColumn(fieldName);
            this.SetString(field, value);
        }

        /// <summary>
        /// Reads a record stream field into a file.
        /// </summary>
        /// <param name="field">Specifies the field of the Record to get.</param>
        /// <param name="filePath">Specifies the path to the file to contain the stream.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <exception cref="NotSupportedException">Attempt to extract a storage from a database open
        /// in read-write mode, or from a database without an associated file path</exception>
        /// <remarks><p>
        /// This method is capable of directly extracting substorages. To do so, first select both the
        /// `Name` and `Data` column of the `_Storages` table, then get the stream of the `Data` field.
        /// However, substorages may only be extracted from a database that is open in read-only mode.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordreadstream.asp">MsiRecordReadStream</a>
        /// </p></remarks>
        public void GetStream(int field, string filePath)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException("filePath");
            }

            IList<TableInfo> tables = (this.view != null ? this.view.Tables : null);
            if (tables != null && tables.Count == 1 && tables[0].Name == "_Storages" && field == this.FindColumn("Data"))
            {
                if (!this.view.Database.IsReadOnly)
                {
                    throw new NotSupportedException("Database must be opened read-only to support substorage extraction.");
                }
                else if (this.view.Database.FilePath == null)
                {
                    throw new NotSupportedException("Database must have an associated file path to support substorage extraction.");
                }
                else if (this.FindColumn("Name") <= 0)
                {
                    throw new NotSupportedException("Name column must be part of the Record in order to extract substorage.");
                }
                else
                {
                    Record.ExtractSubStorage(this.view.Database.FilePath, this.GetString("Name"), filePath);
                }
            }
            else
            {
                if (!this.IsNull(field))
                {
                    Stream readStream = null, writeStream = null;
                    try
                    {
                        readStream = new RecordStream(this, field);
                        writeStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                        int count = 512;
                        byte[] buf = new byte[count];
                        while (count == buf.Length)
                        {
                            if ((count = readStream.Read(buf, 0, buf.Length)) > 0)
                            {
                                writeStream.Write(buf, 0, count);
                            }
                        }
                    }
                    finally
                    {
                        if (readStream != null) readStream.Close();
                        if (writeStream != null) writeStream.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Reads a record stream field into a file.
        /// </summary>
        /// <param name="fieldName">Specifies the field of the Record to get.</param>
        /// <param name="filePath">Specifies the path to the file to contain the stream.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <exception cref="NotSupportedException">Attempt to extract a storage from a database open
        /// in read-write mode, or from a database without an associated file path</exception>
        /// <remarks><p>
        /// This method is capable of directly extracting substorages. To do so, first select both the
        /// `Name` and `Data` column of the `_Storages` table, then get the stream of the `Data` field.
        /// However, substorages may only be extracted from a database that is open in read-only mode.
        /// </p></remarks>
        public void GetStream(string fieldName, string filePath)
        {
            int field = this.FindColumn(fieldName);
            this.GetStream(field, filePath);
        }

        /// <summary>
        /// Gets a record stream field.
        /// </summary>
        /// <param name="field">Specifies the field of the Record to get.</param>
        /// <returns>A Stream that reads the field data.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// This method is not capable of reading substorages. To extract a substorage,
        /// use <see cref="GetStream(int,string)"/>.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordreadstream.asp">MsiRecordReadStream</a>
        /// </p></remarks>
        public Stream GetStream(int field)
        {
            this.CheckRange(field);

            return this.IsNull(field) ? null : new RecordStream(this, field);
        }

        /// <summary>
        /// Gets a record stream field.
        /// </summary>
        /// <param name="fieldName">Specifies the field of the Record to get.</param>
        /// <returns>A Stream that reads the field data.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <remarks><p>
        /// This method is not capable of reading substorages. To extract a substorage,
        /// use <see cref="GetStream(string,string)"/>.
        /// </p></remarks>
        public Stream GetStream(string fieldName)
        {
            int field = this.FindColumn(fieldName);
            return this.GetStream(field);
        }

        /// <summary>
        /// Sets a record stream field from a file. Stream data cannot be inserted into temporary fields.
        /// </summary>
        /// <param name="field">Specifies the field of the Record to set.</param>
        /// <param name="filePath">Specifies the path to the file containing the stream.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// The contents of the specified file are read into a stream object. The stream persists if
        /// the Record is inserted into the Database and the Database is committed.
        /// </p><p>
        /// To reset the stream to its beginning you must pass in null for filePath.
        /// Do not pass an empty string, "", to reset the stream.
        /// </p><p>
        /// Setting a stream with this method is more efficient than setting a field to a
        /// FileStream object.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetstream.asp">MsiRecordsetStream</a>
        /// </p></remarks>
        public void SetStream(int field, string filePath)
        {
            this.CheckRange(field);

            if (String.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException("filePath");
            }

            MsiApi.MsiRecordSetStream(this, (uint) field, filePath);
        }

        /// <summary>
        /// Sets a record stream field from a file. Stream data cannot be inserted into temporary fields.
        /// </summary>
        /// <param name="fieldName">Specifies the field name of the Record to set.</param>
        /// <param name="filePath">Specifies the path to the file containing the stream.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <remarks><p>
        /// The contents of the specified file are read into a stream object. The stream persists if
        /// the Record is inserted into the Database and the Database is committed.
        /// To reset the stream to its beginning you must pass in null for filePath.
        /// Do not pass an empty string, "", to reset the stream.
        /// </p><p>
        /// Setting a stream with this method is more efficient than setting a field to a
        /// FileStream object.
        /// </p></remarks>
        public void SetStream(string fieldName, string filePath)
        {
            int field = this.FindColumn(fieldName);
            this.SetStream(field, filePath);
        }

        /// <summary>
        /// Sets a record stream field from a Stream object. Stream data cannot be inserted into temporary fields.
        /// </summary>
        /// <param name="field">Specifies the field of the Record to set.</param>
        /// <param name="stream">Specifies the stream data.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field is less than 0 or greater than the
        /// number of fields in the Record.</exception>
        /// <remarks><p>
        /// The stream persists if the Record is inserted into the Database and the Database is committed.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msirecordsetstream.asp">MsiRecordsetStream</a>
        /// </p></remarks>
        public void SetStream(int field, Stream stream)
        {
            this.CheckRange(field);

            if (stream == null)
            {
                MsiApi.MsiRecordSetStream(this, (uint) field, null);
            }
            else
            {
                Stream writeStream = null;
                string tempPath = Path.GetTempFileName();
                try
                {
                    writeStream = new FileStream(tempPath, FileMode.Truncate, FileAccess.Write);
                    byte[] buf = new byte[512];
                    int count;
                    while ((count = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        writeStream.Write(buf, 0, count);
                    }
                    writeStream.Close();
                    writeStream = null;

                    MsiApi.MsiRecordSetStream(this, (uint) field, tempPath);
                }
                finally
                {
                    if (writeStream != null) writeStream.Close();
                    if (File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch (IOException)
                        {
                            if (this.view != null)
                            {
                                this.view.Database.DeleteOnClose(tempPath);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sets a record stream field from a Stream object. Stream data cannot be inserted into temporary fields.
        /// </summary>
        /// <param name="fieldName">Specifies the field name of the Record to set.</param>
        /// <param name="stream">Specifies the stream data.</param>
        /// <exception cref="ArgumentOutOfRangeException">The field name does not match any
        /// of the named fields in the Record.</exception>
        /// <remarks><p>
        /// The stream persists if the Record is inserted into the Database and the Database is committed.
        /// </p></remarks>
        public void SetStream(string fieldName, Stream stream)
        {
            int field = this.FindColumn(fieldName);
            this.SetStream(field, stream);
        }

        /// <summary>
        /// Gets a formatted string representation of the Record.
        /// </summary>
        /// <returns>A formatted string representation of the Record.</returns>
        /// <remarks><p>
        /// If field 0 of the Record is set to a nonempty string, it is used to format the data in the Record.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiformatrecord.asp">MsiFormatRecord</a>
        /// </p></remarks>
        /// <seealso cref="FormatString"/>
        /// <seealso cref="Session.FormatRecord(Record)"/>
        public override string ToString()
        {
            return this.ToString((IFormatProvider) null);
        }

        /// <summary>
        /// Gets a formatted string representation of the Record, optionally using a Session to format properties.
        /// </summary>
        /// <param name="provider">an optional Session instance that will be used to lookup any
        /// properties in the Record's format string</param>
        /// <returns>A formatted string representation of the Record.</returns>
        /// <remarks><p>
        /// If field 0 of the Record is set to a nonempty string, it is used to format the data in the Record.
        /// </p><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiformatrecord.asp">MsiFormatRecord</a>
        /// </p></remarks>
        /// <seealso cref="FormatString"/>
        /// <seealso cref="Session.FormatRecord(Record)"/>
        public string ToString(IFormatProvider provider)
        {
            if (this.IsFormatStringInvalid) // Format string is invalid
            {
                // TODO: return all values by default?
                return String.Empty;
            }

            return MsiApi.MsiFormatRecord(this);
        }

        /// <summary>
        /// Gets a formatted string representation of the Record.
        /// </summary>
        /// <param name="format">String to be used to format the data in the Record,
        /// instead of the Record's format string.</param>
        /// <returns>A formatted string representation of the Record.</returns>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiformatrecord.asp">MsiFormatRecord</a>
        /// </p></remarks>
        [Obsolete("This method is obsolete because it has undesirable side-effects. As an alternative, set the FormatString " +
            "property separately before calling the ToString() override that takes no parameters.")]
        public string ToString(string format)
        {
            return this.ToString(format, null);
        }

        /// <summary>
        /// Gets a formatted string representation of the Record, optionally using a Session to format properties.
        /// </summary>
        /// <param name="format">String to be used to format the data in the Record,
        /// instead of the Record's format string.</param>
        /// <param name="provider">an optional Session instance that will be used to lookup any
        /// properties in the Record's format string</param>
        /// <returns>A formatted string representation of the Record.</returns>
        /// <remarks><p>
        /// Win32 MSI API:
        /// <a href="http://msdn.microsoft.com/library/en-us/msi/setup/msiformatrecord.asp">MsiFormatRecord</a>
        /// </p></remarks>
        /// <seealso cref="FormatString"/>
        /// <seealso cref="Session.FormatRecord(Record)"/>
        [Obsolete("This method is obsolete because it has undesirable side-effects. As an alternative, set the FormatString " +
            "property separately before calling the ToString() override that takes just a format provider.")]
        public string ToString(string format, IFormatProvider provider)
        {
            if (format == null)
            {
                return this.ToString(provider);
            }
            else if (format.Length == 0)
            {
                return String.Empty;
            }
            else
            {
                string savedFormatString = (string) this[0];
                try
                {
                    this.FormatString = format;
                    return this.ToString(provider);
                }
                finally
                {
                    this.FormatString = savedFormatString;
                }
            }
        }

        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
        private static void ExtractSubStorage(string databaseFile, string storageName, string extractFile)
        {
            MsiApi.STGM openMode = MsiApi.STGM.READ | MsiApi.STGM.SHARE_DENY_WRITE;
            IStorage storage = MsiApi.StgOpenStorage(databaseFile, (uint) openMode);
            try
            {
                openMode = MsiApi.STGM.READ | MsiApi.STGM.SHARE_EXCLUSIVE;
                IStorage subStorage = storage.OpenStorage(storageName, IntPtr.Zero, (uint) openMode, IntPtr.Zero, 0);
                try
                {
                    openMode = MsiApi.STGM.CREATE | MsiApi.STGM.READWRITE | MsiApi.STGM.SHARE_EXCLUSIVE;
                    IStorage newStorage = MsiApi.StgCreateDocfile(extractFile, (uint) openMode);
                    try
                    {
                        subStorage.CopyTo(0, IntPtr.Zero, IntPtr.Zero, newStorage);
                        newStorage.Commit(0);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(newStorage);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(subStorage);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(storage);
            }
        }

        private int FindColumn(string fieldName)
        {
            if (this.view == null)
            {
                throw new InvalidOperationException();
            }
            ColumnCollection columns = this.view.Columns;
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Name == fieldName)
                {
                    return i + 1;
                }
            }
            throw new ArgumentOutOfRangeException("fieldName");
        }

        private void CheckRange(int field)
        {
            if (field < 0 || field > this.FieldCount)
            {
                throw new ArgumentOutOfRangeException("field");
            }
        }
    }
}
