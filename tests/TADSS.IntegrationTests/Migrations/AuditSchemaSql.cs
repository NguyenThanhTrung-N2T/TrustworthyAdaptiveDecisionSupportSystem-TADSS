using Npgsql;
using NpgsqlTypes;

namespace TADSS.IntegrationTests.Migrations;

/// <summary>
/// Tiện ích SQL trực tiếp cho test schema audit v2: chèn bản ghi hợp lệ (có thể sửa từng cột để tạo dòng sai),
/// chạy lệnh và đọc giá trị đơn. Dùng SQL thuần để vượt qua mọi kiểm tra phía EF và chỉ chạm ràng buộc của PostgreSQL.
/// </summary>
internal static class AuditSchemaSql
{
    public const string Executions = "tadss.decision_executions";
    public const string Overrides = "tadss.decision_overrides";
    public const string Outcomes = "tadss.decision_outcomes";

    private static readonly HashSet<string> JsonbColumns = new(StringComparer.Ordinal) { "request_payload", "response_payload" };

    /// <summary>Dòng <c>decision_executions</c> hợp lệ (PRIMARY, APPROVED, có mức cọc).</summary>
    public static Dictionary<string, object?> ValidExecution(Guid? decisionId = null) => new()
    {
        ["decision_id"] = decisionId ?? Guid.NewGuid(),
        ["reservation_id"] = Guid.NewGuid(),
        ["branch_id"] = Guid.NewGuid(),
        ["role"] = "PRIMARY",
        ["strategy_name"] = "DefaultDecisionStrategy",
        ["strategy_version"] = "1.0.0",
        ["strategy_status"] = "APPROVED",
        ["status"] = "APPROVED",
        ["deposit_rate"] = 0.3m,
        ["deposit_amount"] = 90000m,
        ["reason_code"] = "RULE_APPROVED",
        ["execution_type"] = "AUTOMATED",
        ["execution_time_ms"] = 12,
        ["fallback_used"] = false,
        ["failure_reason"] = null,
        ["cache_hit"] = false,
        ["approval_expires_at"] = null,
        ["request_hash"] = new string('a', 64),
        ["request_payload"] = "{}",
        ["response_payload"] = "{}",
        ["idempotency_key"] = null,
        ["correlation_id"] = null,
        ["created_at"] = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero),
    };

    /// <summary>Dòng <c>decision_overrides</c> hợp lệ (APPROVE, có mức cọc) cho <paramref name="decisionId"/>.</summary>
    public static Dictionary<string, object?> ValidOverride(Guid decisionId) => new()
    {
        ["decision_id"] = decisionId,
        ["action"] = "APPROVE",
        ["deposit_rate"] = 0.5m,
        ["deposit_amount"] = 150000m,
        ["actor_id"] = "manager-01",
        ["reason"] = "Khach quen, duyet coc 50%",
        ["idempotency_key"] = $"override-{Guid.NewGuid():N}",
        ["created_at"] = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
    };

    /// <summary>Dòng <c>decision_outcomes</c> hợp lệ (COMPLETED) cho <paramref name="decisionId"/>.</summary>
    public static Dictionary<string, object?> ValidOutcome(Guid decisionId) => new()
    {
        ["decision_id"] = decisionId,
        ["outcome"] = "COMPLETED",
        ["occurred_at"] = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero),
        ["reported_at"] = new DateTimeOffset(2026, 10, 3, 10, 5, 0, TimeSpan.Zero),
    };

    /// <summary>Chèn một dòng vào <paramref name="table"/> bằng các cột/giá trị trong <paramref name="values"/>.</summary>
    public static async Task InsertAsync(string connectionString, string table, IReadOnlyDictionary<string, object?> values)
    {
        var columns = values.Keys.ToList();
        var sql = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select(c => "@" + c))})";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var column in columns)
        {
            var parameter = JsonbColumns.Contains(column)
                ? new NpgsqlParameter(column, NpgsqlDbType.Jsonb)
                : new NpgsqlParameter { ParameterName = column };
            parameter.Value = values[column] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Chèn một quyết định hợp lệ rồi trả về <c>decision_id</c> của nó.</summary>
    public static async Task<Guid> InsertValidExecutionAsync(string connectionString)
    {
        var decisionId = Guid.NewGuid();
        await InsertAsync(connectionString, Executions, ValidExecution(decisionId));
        return decisionId;
    }

    /// <summary>Chạy một lệnh không trả dữ liệu.</summary>
    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Chạy truy vấn và trả về giá trị đầu tiên ép về <typeparamref name="T"/>.</summary>
    public static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is null
            ? throw new InvalidOperationException("Truy vấn không trả về giá trị.")
            : (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>Đọc nội dung toàn bộ bảng thành một chuỗi (để so sánh trước/sau một lệnh bị từ chối).</summary>
    public static Task<string> TableContentAsync(string connectionString, string table) =>
        ScalarAsync<string>(connectionString,
            $"SELECT coalesce(string_agg(t::text, '|' ORDER BY t::text), '') FROM {table} t");

    /// <summary>
    /// Chụp cấu trúc schema <c>tadss</c> (cột, index, ràng buộc, trigger, hàm) thành danh sách dòng có thứ tự,
    /// bỏ qua bảng lịch sử migration, để so sánh hai database có cùng schema hay không.
    /// </summary>
    public static async Task<IReadOnlyList<string>> SchemaFingerprintAsync(string connectionString)
    {
        const string sql = """
            SELECT 'column|' || table_name || '|' || ordinal_position || '|' || column_name || '|' || data_type
                   || '|' || coalesce(character_maximum_length::text, '') || '|' || coalesce(numeric_precision::text, '')
                   || '|' || coalesce(numeric_scale::text, '') || '|' || is_nullable || '|' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'tadss' AND table_name <> '__EFMigrationsHistory'
            UNION ALL
            SELECT 'index|' || tablename || '|' || indexname || '|' || indexdef
            FROM pg_indexes
            WHERE schemaname = 'tadss' AND tablename <> '__EFMigrationsHistory'
            UNION ALL
            SELECT 'constraint|' || conrelid::regclass::text || '|' || conname || '|' || contype::text || '|' || pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE connamespace = 'tadss'::regnamespace AND conrelid::regclass::text <> 'tadss."__EFMigrationsHistory"'
            UNION ALL
            SELECT 'trigger|' || tgrelid::regclass::text || '|' || tgname || '|' || pg_get_triggerdef(oid)
            FROM pg_trigger
            WHERE NOT tgisinternal AND tgrelid::regclass::text LIKE 'tadss.%'
            UNION ALL
            SELECT 'function|' || proname || '|' || pg_get_functiondef(oid)
            FROM pg_proc
            WHERE pronamespace = 'tadss'::regnamespace
            ORDER BY 1
            """;

        var lines = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }
}
