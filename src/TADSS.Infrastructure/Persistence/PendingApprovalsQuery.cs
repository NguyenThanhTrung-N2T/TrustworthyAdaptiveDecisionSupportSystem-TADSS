using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using TADSS.Application.Abstractions.Persistence;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Truy vấn hàng chờ duyệt của một chi nhánh (DECISION_PERSISTENCE_INVARIANTS B.5) bằng SQL thô tham số hóa (<c>FromSql</c>/<c>SqlQuery</c>
/// với <see cref="FormattableString"/>: mọi giá trị đi qua tham số, không nối giá trị vào chuỗi SQL).
/// <para>
/// Viết theo dạng dùng được partial index: ứng viên là bản <c>PRIMARY</c> + <c>NEED_APPROVAL</c> của chi nhánh (khớp <c>idx_de_pending</c>),
/// rồi loại bản bị bản <c>PRIMARY</c> mới hơn cùng đơn và chi nhánh thay thế (<c>NOT EXISTS</c> trên <c>idx_de_reservation_created</c>;
/// "mới hơn" so theo <c>(created_at, decision_id)</c> nên hai bản cùng <c>created_at</c> vẫn có đúng một bản thắng), bản đã có
/// override hoặc outcome, và bản hết hạn duyệt (<c>approval_expires_at &gt; nowUtc</c> nghiêm ngặt; <c>nowUtc</c> do caller truyền từ
/// <see cref="TimeProvider"/>, không dùng <c>now()</c>). Sắp theo <c>approval_expires_at, decision_id</c> nên phân trang tất định.
/// </para>
/// <para>
/// Trang và tổng là hai câu SQL riêng, không chung transaction: giữa hai lần đọc hàng chờ có thể đổi nên tổng chỉ là số đếm tại thời điểm đọc.
/// </para>
/// </summary>
internal sealed class PendingApprovalsQuery
{
    // {NOW} là vị trí tham số mốc hiện tại (khác nhau giữa câu theo chi nhánh và câu gộp). Một nơi duy nhất cho điều kiện lọc,
    // dùng chung cho câu trang, câu tổng và câu đếm theo chi nhánh (gauge tadss.pending_approvals)
    private const string Predicates = """
        c.role = 'PRIMARY' AND c.status = 'NEED_APPROVAL'
          AND c.approval_expires_at > {NOW}
          AND NOT EXISTS (SELECT 1 FROM tadss.decision_executions n
                          WHERE n.reservation_id = c.reservation_id AND n.branch_id = c.branch_id AND n.role = 'PRIMARY'
                            AND (n.created_at, n.decision_id) > (c.created_at, c.decision_id))
          AND NOT EXISTS (SELECT 1 FROM tadss.decision_overrides o WHERE o.decision_id = c.decision_id)
          AND NOT EXISTS (SELECT 1 FROM tadss.decision_outcomes r WHERE r.decision_id = c.decision_id)
        """;

    // Câu theo chi nhánh: {0} = branchId, {1} = nowUtc
    private static readonly string BranchConditions = "c.branch_id = {0} AND " + Predicates.Replace("{NOW}", "{1}");

    // {2} = pageSize, {3} = offset
    private static readonly string PageSql =
        "SELECT c.* FROM tadss.decision_executions c WHERE " + BranchConditions +
        " ORDER BY c.approval_expires_at, c.decision_id LIMIT {2} OFFSET {3}";

    private static readonly string CountSql =
        "SELECT count(*)::int AS \"Value\" FROM tadss.decision_executions c WHERE " + BranchConditions;

    // Câu gộp mọi chi nhánh: {0} = nowUtc
    private static readonly string CountByBranchSql =
        "SELECT c.branch_id AS \"BranchId\", count(*)::bigint AS \"Count\" FROM tadss.decision_executions c WHERE " +
        Predicates.Replace("{NOW}", "{0}") + " GROUP BY c.branch_id";

    private readonly TadssDbContext _db;

    /// <summary>
    /// Khởi tạo truy vấn trên <paramref name="db"/> của scope hiện tại.
    /// </summary>
    /// <param name="db">DbContext của scope.</param>
    public PendingApprovalsQuery(TadssDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Dựng câu trang (<c>SELECT … ORDER BY … LIMIT … OFFSET …</c>) với bốn tham số theo thứ tự <paramref name="branchId"/>,
    /// <paramref name="nowUtc"/>, <paramref name="pageSize"/>, <paramref name="offset"/>. Tách ra để test chạy <c>EXPLAIN</c> đúng câu mà store chạy.
    /// </summary>
    public static FormattableString PageStatement(Guid branchId, DateTimeOffset nowUtc, int pageSize, long offset) =>
        FormattableStringFactory.Create(PageSql, branchId, nowUtc, pageSize, offset);

    private static FormattableString CountStatement(Guid branchId, DateTimeOffset nowUtc) =>
        FormattableStringFactory.Create(CountSql, branchId, nowUtc);

    private static FormattableString CountByBranchStatement(DateTimeOffset nowUtc) =>
        FormattableStringFactory.Create(CountByBranchSql, nowUtc);

    /// <summary>
    /// Đọc một trang và tổng số bản ghi chờ duyệt của <paramref name="branchId"/>.
    /// </summary>
    /// <param name="branchId">Chi nhánh.</param>
    /// <param name="page">Số trang, từ 1.</param>
    /// <param name="pageSize">Số bản ghi mỗi trang.</param>
    /// <param name="nowUtc">Mốc hiện tại để loại bản hết hạn duyệt.</param>
    /// <param name="cancellationToken">Token hủy.</param>
    public async Task<PendingApprovalsPage> ExecuteAsync(
        Guid branchId, int page, int pageSize, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var offset = (long)(page - 1) * pageSize;

        var entities = await _db.DecisionExecutions
            .FromSql(PageStatement(branchId, nowUtc, pageSize, offset))
            .AsNoTracking()
            .OrderBy(execution => execution.ApprovalExpiresAt)
            .ThenBy(execution => execution.DecisionId)
            .ToListAsync(cancellationToken);

        var total = await _db.Database
            .SqlQuery<int>(CountStatement(branchId, nowUtc))
            .SingleAsync(cancellationToken);

        return new PendingApprovalsPage(entities.Select(DecisionAuditMapper.ToRecord).ToList(), total);
    }

    /// <summary>
    /// Đếm số quyết định đang chờ duyệt của <b>mọi</b> chi nhánh bằng một truy vấn gộp, cùng điều kiện lọc với <see cref="ExecuteAsync"/>.
    /// Chi nhánh không có bản nào chờ thì không có mặt trong kết quả.
    /// </summary>
    /// <param name="nowUtc">Mốc hiện tại để loại bản hết hạn duyệt.</param>
    /// <param name="cancellationToken">Token hủy.</param>
    public async Task<IReadOnlyDictionary<Guid, long>> CountByBranchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var rows = await _db.Database
            .SqlQuery<PendingBranchCountRow>(CountByBranchStatement(nowUtc))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.BranchId, row => row.Count);
    }
}
