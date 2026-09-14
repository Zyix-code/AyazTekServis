using Microsoft.Data.Sqlite;

namespace AyazTekServis.Services;

public class ServiceNumberGenerator(IConfiguration configuration)
{
    private readonly IConfiguration _configuration = configuration;

    public async Task<string> NextAsync(DateTime date)
    {
        var period = date.ToString("yyyy/MM");
        var connectionString = _configuration.GetConnectionString("DefaultConnection")!;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ServiceSequences (Period, LastNumber)
            VALUES ($period, 1)
            ON CONFLICT(Period) DO UPDATE SET LastNumber = LastNumber + 1
            RETURNING LastNumber;
            """;
        command.Parameters.AddWithValue("$period", period);
        var value = await command.ExecuteScalarAsync();
        var sequence = Convert.ToInt32(value);
        return $"{period}/{sequence:00}";
    }
}
