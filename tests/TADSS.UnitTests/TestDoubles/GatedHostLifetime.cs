using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Bọc <see cref="IHostLifetime"/> thật của host: <c>Host.StartAsync</c> chờ <c>gate</c> trước khi làm bất cứ việc gì khác (đây là bước đầu tiên của
/// <c>StartAsync</c>, trước kiểm tra <c>ValidateOnStart</c> và trước khi chạy hosted service), rồi mới giao cho lifetime thật.
/// <para>
/// Lý do: với <c>WebApplicationFactory</c>, <c>Program</c> chạy trên thread riêng; khi khởi động thất bại, <c>app.Run()</c> hủy (dispose) host
/// trong khi thread của test có thể vẫn đang resolve <see cref="IHostApplicationLifetime"/> trong <c>DeferredHost.StartAsync</c>, nên exception
/// thật bị thay bằng <see cref="ObjectDisposedException"/> khi máy bận. Cổng chỉ đổi thứ tự hai thread (test đăng ký chờ trước, host khởi động
/// sau), không đổi việc host kiểm tra hay đăng ký gì. Cổng chỉ che lỗi bên trong <c>Host.StartAsync</c>; lỗi ở <c>StartupTasks</c> (trước
/// <c>app.Run()</c>) không race vì <c>Program.cs</c> không dispose <c>app</c> khi đó.
/// </para>
/// </summary>
internal sealed class GatedHostLifetime(IHostLifetime inner, Task gate) : IHostLifetime, IDisposable
{
    /// <summary>Thay đăng ký <see cref="IHostLifetime"/> cuối cùng của <paramref name="services"/> bằng bản bọc chờ <paramref name="gate"/>.</summary>
    public static void Decorate(IServiceCollection services, Task gate)
    {
        var descriptor = services.LastOrDefault(registration => registration.ServiceType == typeof(IHostLifetime) && !registration.IsKeyedService)
            ?? throw new InvalidOperationException("Host chưa đăng ký IHostLifetime nên không gắn được cổng khởi động.");
        services.Remove(descriptor);
        services.AddSingleton<IHostLifetime>(provider => new GatedHostLifetime(CreateInner(provider, descriptor), gate));
    }

    /// <inheritdoc />
    public async Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        await inner.WaitForStartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => inner.StopAsync(cancellationToken);

    /// <summary>Lifetime thật (vd. <c>ConsoleLifetime</c>) được tạo trong factory nên container không tự dispose nó; bản bọc dispose thay.</summary>
    public void Dispose() => (inner as IDisposable)?.Dispose();

    private static IHostLifetime CreateInner(IServiceProvider provider, ServiceDescriptor descriptor) => descriptor switch
    {
        { ImplementationInstance: IHostLifetime instance } => instance,
        { ImplementationFactory: { } factory } => (IHostLifetime)factory(provider),
        { ImplementationType: { } type } => (IHostLifetime)ActivatorUtilities.CreateInstance(provider, type),
        _ => throw new InvalidOperationException("Đăng ký IHostLifetime không có cách tạo được hỗ trợ.")
    };
}
