namespace Configurator
{
    public class TemplateColumn
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "int";
        public int MaxLength { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsComputed { get; set; }
        public bool HasDefault { get; set; }
        public string DefaultDefinition { get; set; } = "";
        public string OriginalName { get; set; } = "";
        public bool IsExisting { get; set; }
        public bool IsReadOnly => IsExisting && (IsIdentity || IsComputed);
    }
}
