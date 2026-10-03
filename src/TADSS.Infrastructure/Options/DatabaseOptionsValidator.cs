using Microsoft.Extensions.Options;
using Npgsql;

namespace TADSS.Infrastructure.Options;

/// <summary>
/// Kiểm tra <see cref="DatabaseOptions"/> khi khởi động (fail-fast, mọi môi trường): <c>ConnectionString</c> phải có, parse được bằng
/// <see cref="NpgsqlConnectionStringBuilder"/> và có <c>Host</c>. Chuỗi giữ chỗ trong <c>appsettings.json</c> (<c>your-connection-string</c>)
/// bị từ chối theo cách này. Message không bao giờ chứa giá trị chuỗi kết nối hay message của Npgsql.
/// </summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    private const string InvalidConnectionString =
        "ConnectionStrings:DefaultConnection thiếu hoặc không phải chuỗi kết nối PostgreSQL hợp lệ (cần Host).";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        return IsValidConnectionString(options.ConnectionString)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(InvalidConnectionString);
    }

    private static bool IsValidConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            return !string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(connectionString).Host);
        }
        catch (ArgumentException)
        {
            // Không parse được (không có dấu '=' ...): coi như không hợp lệ, không đưa message/giá trị ra ngoài
            return false;
        }
    }
}
