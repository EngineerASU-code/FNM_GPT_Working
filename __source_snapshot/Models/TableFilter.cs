namespace Configurator
{
    public class TableFilter
    {
        public string Column { get; set; } = "";
        public string Operator { get; set; } = "Contains";
        public string Value { get; set; } = "";

        public bool IsActive => !string.IsNullOrWhiteSpace(Column) &&
                                 (!string.IsNullOrWhiteSpace(Value) || Operator is "Is Null" or "Is Not Null");
    }
}
