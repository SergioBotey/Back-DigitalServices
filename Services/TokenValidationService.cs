using System.Threading.Tasks;
using Dapper;
public class TokenValidationService
{
    private readonly DatabaseConfig _databaseService;

    public TokenValidationService(DatabaseConfig databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<bool> IsValidTokenAsync(string token)
    {
        int? userProcessId;
        using (var connection = await _databaseService.Database1.OpenConnectionAsync())
        {
            userProcessId = await connection.QuerySingleOrDefaultAsync<int?>("SELECT count(*) as count FROM dbo.userToken WHERE token = @Token", new { Token = token });
        }

        return userProcessId.HasValue && userProcessId.Value > 0;
    }
}