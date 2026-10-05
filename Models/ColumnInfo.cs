namespace Configurator
{
    public class ColumnInfo
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsNullable { get; set; }
        public bool IsComputed { get; set; }

        /// <summary>
        /// Максимальная длина для char/varchar/nchar/nvarchar. -1 = MAX.
        /// </summary>
        public int MaxLength { get; set; }

        /// <summary>
        /// Точность для decimal/numeric.
        /// </summary>
        public byte Precision { get; set; }

        /// <summary>
        /// Масштаб для decimal/numeric.
        /// </summary>
        public byte Scale { get; set; }

        /// <summary>
        /// Есть ли у колонки значение по умолчанию в БД.
        /// </summary>
        public bool HasDefault { get; set; }

        /// <summary>
        /// Только для чтения (computed или timestamp/rowversion).
        /// </summary>
        public bool IsReadOnly => IsComputed || DataType == "timestamp" || DataType == "rowversion";
    }
}
