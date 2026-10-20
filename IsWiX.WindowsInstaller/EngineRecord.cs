namespace IsWiX.WindowsInstaller
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using MsiReader;

    /// <summary>A record: field 0 is the format template, fields 1..n hold values.</summary>
    internal sealed class EngineRecord
    {
        private readonly object?[] fields;
        private readonly Dictionary<uint, int> readOffsets = new Dictionary<uint, int>();

        public EngineRecord(int count)
        {
            this.fields = new object?[count + 1];
        }

        public EngineRecord(IReadOnlyList<object?> values)
            : this(values.Count)
        {
            for (int i = 0; i < values.Count; i++)
            {
                this.fields[i + 1] = values[i];
            }
        }

        public int FieldCount => this.fields.Length - 1;

        public object? this[uint field]
        {
            get
            {
                this.CheckField(field);
                return this.fields[field];
            }
        }

        public void Set(uint field, object? value)
        {
            this.CheckField(field);
            this.fields[field] = value;
            this.readOffsets.Remove(field);
        }

        public void Clear()
        {
            for (int i = 0; i < this.fields.Length; i++)
            {
                this.fields[i] = null;
            }
            this.readOffsets.Clear();
        }

        public int ReadOffset(uint field)
        {
            return this.readOffsets.TryGetValue(field, out int offset) ? offset : 0;
        }

        public void SetReadOffset(uint field, int offset)
        {
            this.readOffsets[field] = offset;
        }

        /// <summary>Returns fields 1..n, the parameter values a query is executed with.</summary>
        public object?[] Values()
        {
            var values = new object?[this.FieldCount];
            Array.Copy(this.fields, 1, values, 0, this.FieldCount);
            return values;
        }

        public string GetString(uint field)
        {
            switch (this[field])
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

        public int GetInteger(uint field)
        {
            switch (this[field])
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
    }
}
