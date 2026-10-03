using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Application.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Options;

/// <summary>
/// UT-14: <see cref="StrategyOptionsValidator"/> chặn tên strategy kích hoạt rỗng ngay khi khởi động
/// (<c>ValidateOnStart</c> trong <c>AddApplication</c>) và để mọi cấu hình đúng đi qua.
/// </summary>
public class StrategyOptionsValidatorTests
{
    private static IHost BuildHost(params (string Key, string Value)[] values)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)).Append(TestConnectionStrings.Pair));
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Tên rỗng hoặc toàn khoảng trắng bị từ chối khi khởi động, thông báo nêu khóa cấu hình.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StrategyOptions_BlankActiveStrategyName_FailsOnStart_Theory(string activeStrategyName)
    {
        using var host = BuildHost(("Tadss:Strategy:ActiveStrategyName", activeStrategyName));

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*Tadss:Strategy:ActiveStrategyName*");
    }

    /// <summary>Tên strategy hợp lệ (kể cả strategy thay thế) khởi động bình thường.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData("DefaultDecisionStrategy")]
    [InlineData("MockAlternativeDecisionStrategy")]
    public async Task StrategyOptions_ValidActiveStrategyName_StartsHost_Theory(string activeStrategyName)
    {
        using var host = BuildHost(("Tadss:Strategy:ActiveStrategyName", activeStrategyName));

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetRequiredService<IOptions<StrategyOptions>>().Value.ActiveStrategyName.Should().Be(activeStrategyName);
    }
}
