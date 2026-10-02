using System;
using System.Collections.Generic;

namespace Configurator
{
    public sealed class ProgramTemplateItem
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Name { get; set; } = "";
        public string PLC { get; set; } = "";
        public string Area { get; set; } = "";
        public string ClassNumber { get; set; } = "";
        public string Record { get; set; } = "";
        public string ContextText
            => string.Join("  ·  ", new[]
            {
                string.IsNullOrWhiteSpace(PLC) ? null : $"PLC={PLC}",
                string.IsNullOrWhiteSpace(Record) ? null : $"Record={Record}",
                string.IsNullOrWhiteSpace(Area) ? null : $"Area={Area}",
                string.IsNullOrWhiteSpace(ClassNumber) ? null : $"Класс={ClassNumber}"
            });
        public override string ToString() => Name;
    }

    public sealed class ProgramContext
    {
        public string PLC { get; set; } = "";
        public object PlcValue { get; set; }
        public object ClassNumberValue { get; set; }
        public object RecordValue { get; set; }
        public object AreaValue { get; set; }

        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string ContextText
            => string.Join("  ·  ", new[]
            {
                string.IsNullOrWhiteSpace(PLC) ? null : $"PLC={PLC}",
                ClassNumberValue == null || ClassNumberValue == DBNull.Value ? null : $"Класс={ClassNumberValue}",
                RecordValue == null || RecordValue == DBNull.Value ? null : $"Record={RecordValue}",
                AreaValue == null || AreaValue == DBNull.Value ? null : $"Area={AreaValue}"
            });
    }

    public sealed class ProgramSectionDefinition
    {
        public string Key { get; init; } = "";
        public string Caption { get; init; } = "";
        public string TableName { get; init; } = "";
        public string Description { get; init; } = "";
    }
}
