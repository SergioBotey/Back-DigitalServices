using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
public class DatabaseService
{
   private readonly string _connectionString;

   public DatabaseService(string connectionString)
   {
       _connectionString = connectionString;
   }

   public async Task<SqlConnection> OpenConnectionAsync()
   {
       var connection = new SqlConnection(_connectionString);
       await connection.OpenAsync();
       return connection;
   }
}