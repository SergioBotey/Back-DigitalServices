public class DatabaseConfig
{
   public DatabaseService Database1 { get; }
   public DatabaseService Database2 { get; }
   public DatabaseService Database3 { get; }

   public DatabaseConfig(string connectionString1, string connectionString2, string connectionString3)
   {
       Database1 = new DatabaseService(connectionString1);
       Database2 = new DatabaseService(connectionString2);
       Database3 = new DatabaseService(connectionString3);
   }
}
