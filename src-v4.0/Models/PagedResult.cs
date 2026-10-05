using System.Data;

namespace Configurator
{
    public class PagedResult
    {
        public DataTable Data { get; set; }
        public int TotalCount { get; set; }
    }
}
