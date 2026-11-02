using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMBDHL.DataAccess.SQLDataBaseAccess
{
    public interface ISQLDataBaseAccess
    {
        Task<IEnumerable<T>> GetData<T, P>(string spName, P parameter, string connection = "DefaultConnection");

        Task<int> SaveData<T>(string spName, T parameter, string connection = "DefaultConnection");
    }
}
