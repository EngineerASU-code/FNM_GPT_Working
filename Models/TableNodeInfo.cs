namespace Configurator
{
    public class TableNodeInfo
    {
        public string DatabaseName { get; set; }
        public string Schema { get; set; }
        public string TableName { get; set; }

        /// <summary>
        /// Полный идентификатор: "schema.table"
        /// </summary>
        public string FullName => $"{Schema}.{TableName}";
    }
}
