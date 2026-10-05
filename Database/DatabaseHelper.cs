using Microsoft.Data.SqlClient;

namespace sudokuvip.Database;

public static class DatabaseHelper
{
    public static SqlConnection GetConnection()
    {
        string? server = Environment.GetEnvironmentVariable("SUDOKU_SQL_SERVER");
        string? database = Environment.GetEnvironmentVariable("SUDOKU_SQL_DATABASE");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("SQL Server configuration is missing.");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,InitialCatalog = database,IntegratedSecurity = true,
            TrustServerCertificate = string.Equals(Environment.GetEnvironmentVariable("SUDOKU_SQL_TRUST_CERTIFICATE"),"true",StringComparison.OrdinalIgnoreCase),
            ConnectTimeout = 5
        };
        return new SqlConnection(builder.ConnectionString);
    }
}
