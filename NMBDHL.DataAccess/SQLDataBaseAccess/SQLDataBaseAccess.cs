using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMBDHL.DataAccess.SQLDataBaseAccess
{
    public class SQLDataBaseAccess:ISQLDataBaseAccess
    {
        private readonly IConfiguration _config;

        public SQLDataBaseAccess(IConfiguration configuration)
        {
            _config = configuration;
        }

        // This method retrieves data (e.g., SELECT statements)
        public async Task<IEnumerable<T>> GetData<T, P>(string spName, P parameter, string connection = "DefaultConnection")
        {
            using IDbConnection con = new SqlConnection(_config.GetConnectionString(connection));
            return await con.QueryAsync<T>(spName, parameter, commandType: CommandType.StoredProcedure);
        }

        // This method saves data and returns the number of affected rows (e.g., for UPDATE/DELETE/INSERT statements)
        public async Task<int> SaveData<T>(string spName, T parameter, string connection = "DefaultConnection")
        {
            using IDbConnection con = new SqlConnection(_config.GetConnectionString(connection));
            return await con.ExecuteAsync(spName, parameter, commandType: CommandType.StoredProcedure);
        }
    }
}
