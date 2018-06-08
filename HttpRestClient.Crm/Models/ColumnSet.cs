using System.Linq;

namespace HttpRestClient.Crm.Models
{
    public class ColumnSet
    {
        public ColumnSet(params string [] columns)
        {
            Columns = columns.ToArray();
        }

        public string[] Columns { get; }
    }
}
