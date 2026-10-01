using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Collection xUnit dùng chung <see cref="TadssInfrastructureFixture"/>: container khởi động một lần,
/// các test class trong collection chạy tuần tự (an toàn khi test bật/tắt Toxiproxy).
/// </summary>
[CollectionDefinition(Name)]
public class TadssInfrastructureCollection : ICollectionFixture<TadssInfrastructureFixture>
{
    public const string Name = "TadssInfrastructure";
}
