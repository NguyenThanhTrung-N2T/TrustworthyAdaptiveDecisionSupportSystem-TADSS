# Bảng tham chiếu cấu hình `Tadss:*`

Tài liệu này là **nơi sở hữu duy nhất** của danh sách khóa cấu hình `Tadss:*` của TADSS-Runtime: tên khóa, kiểu, giá trị mặc định và ràng buộc. Các tài liệu khác (README, hướng dẫn chạy) chỉ đưa ví dụ tối thiểu và dẫn link về đây. Test DOC-4 (`ConfigurationDocumentationTests`) so khớp hai chiều bảng này với các lớp Options trong mã: thêm hoặc bỏ một thuộc tính cấu hình mà không sửa bảng thì test đỏ.

## Nguồn cấu hình và thứ tự ghi đè

Ứng dụng đọc cấu hình theo thứ tự sau, nguồn sau ghi đè nguồn trước:

1. `appsettings.json` (trong repo, chứa giá trị mặc định vận hành và các ô giữ chỗ không phải secret).
2. `appsettings.{Environment}.json` theo `ASPNETCORE_ENVIRONMENT` (ví dụ `Development`, `Testing`). File cục bộ chứa chuỗi kết nối và mã băm API key thật, không nằm trong tài liệu này.
3. Biến môi trường: đổi dấu `:` của khóa thành `__`, ví dụ `Tadss:Validation:MaxItemMinutes` thành `Tadss__Validation__MaxItemMinutes`. Phần tử của danh sách đánh chỉ số: `Tadss__Security__ApiKeys__0__ClientId`.

Chuỗi kết nối PostgreSQL không nằm dưới `Tadss:*` mà ở `ConnectionStrings:DefaultConnection` (biến `ConnectionStrings__DefaultConnection`).

Ví dụ biến môi trường trong `docker-compose.yml`, service `eval-tadss-portability` (chạy dataset khách sạn bằng EUR chỉ nhờ cấu hình, không sửa mã):

```yaml
environment:
  Tadss__Cache__InstanceName: "tadss-pt:"
  Tadss__Validation__MaxItemMinutes: "100800"
  Tadss__Strategies__Default__HighValueThreshold: "750"
```

Quy ước chung:

- Mọi section có validator và `ValidateOnStart()`: giá trị sai làm ứng dụng không khởi động, thông báo nêu tên khóa (không in giá trị bí mật). Options của hai strategy tự kiểm bằng `Validate()` khi dựng registry, cùng hậu quả.
- Chỉ `Tadss:Routing` được nạp lại nóng khi file cấu hình đang chạy đổi (`appsettings*.json`); biến môi trường không nạp lại. Mọi khóa khác cần khởi động lại.
- Cột *Mặc định* là giá trị khởi tạo trong mã; khi `appsettings.json` đặt giá trị khác thì ghi rõ cả hai.
- Cột *Kiểu* của danh sách hoặc đối tượng lồng là **một dòng**; các khóa con được mô tả ở cột *Ý nghĩa / ràng buộc* (không thêm dòng riêng).
- Khóa Serilog `Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware = Fatal` trong `appsettings.json` tắt log lỗi trùng của framework (`GlobalExceptionHandler` đã tự ghi lỗi 5xx ở mức Error và 4xx ở mức Warning). Khóa này không thuộc `Tadss:*` nên bảng này không liệt kê; DOC-4 chỉ kiểm các khóa `Tadss:*`.

## Section của Runtime

### Tadss:Approval

Lớp `ApprovalOptions`, project `TADSS.Application`. Chính sách approval window của quyết định `NEED_APPROVAL`.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Approval:WindowMinutes` | `int` | `30` | Độ dài approval window (phút) tính từ lúc đánh giá; phải lớn hơn 0. |
| `Tadss:Approval:MinLeadMinutes` | `int` | `120` | Thời gian tối thiểu (phút) phải còn lại từ lúc hết approval window đến giờ bắt đầu sớm nhất thì mới cho chờ duyệt; không âm. |

### Tadss:Validation

Lớp `ValidationOptions`, project `TADSS.Application`. Giới hạn thời lượng mỗi item của request (P-01).

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Validation:MinItemMinutes` | `int` | `30` | Thời lượng tối thiểu (phút) của một item, tính cả biên. Ràng buộc: `0 < MinItemMinutes <= MaxItemMinutes`. |
| `Tadss:Validation:MaxItemMinutes` | `int` | `720` | Thời lượng tối đa (phút) của một item, tính cả biên; không nhỏ hơn `MinItemMinutes`. |

### Tadss:Calendar

Lớp `CalendarOptions`, project `TADSS.Application`. Lịch cao điểm dùng chung cho mọi strategy và điều kiện `PeakHour` của định tuyến.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Calendar:PeakWindows` | `List<PeakWindowOptions>` | mã: danh sách rỗng; `appsettings.json`: 7 khung giờ | Các khung giờ cao điểm theo giờ địa phương của chi nhánh. Mỗi phần tử có `Day` (bắt buộc, `Monday`…`Sunday`), `Start` (tính cả) và `End` (không tính), cả hai định dạng `HH:mm` và `Start < End`. Danh sách rỗng nghĩa là dùng lịch mặc định của SAS 4.3: thứ Hai đến thứ Sáu 17:00-21:00, thứ Bảy và Chủ Nhật 08:00-21:00 (đúng bằng nội dung `appsettings.json`). Mã cố ý không khởi tạo sẵn vì binder của .NET nối thêm phần tử vào danh sách có sẵn thay vì thay thế. |

### Tadss:Routing

Lớp `StrategyRoutingOptions`, project `TADSS.Application`. Chọn strategy theo từng request (A-01), **nạp lại nóng**: bản mới sai thì giữ bảng cũ, ghi log Error và tăng `tadss.config.reload_failures{section="Tadss:Routing"}`.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Routing:DefaultStrategy` | `string` | `DefaultDecisionStrategy` | Strategy dùng khi không rule nào khớp; bắt buộc không rỗng, khớp `StrategyName` của strategy trong registry (không phân biệt hoa thường). Thay khóa cũ `Tadss:Strategy:ActiveStrategyName`, nay chỉ sinh Warning khi gặp khóa đó. |
| `Tadss:Routing:Rules` | `List<RoutingRuleOptions>` | danh sách rỗng | Các rule theo thứ tự ưu tiên, **rule đầu tiên khớp** được chọn; rỗng thì mọi request dùng `DefaultStrategy`. Mỗi rule có `Name` (bắt buộc, tối đa 64 ký tự, duy nhất không phân biệt hoa thường), `Strategy` (bắt buộc, phải có trong registry) và `When`. `When` phải có ít nhất một điều kiện, mọi điều kiện là AND, danh sách rỗng hoặc bỏ trống là không ràng buộc: `PeakHour` (`bool`, theo `Tadss:Calendar` và giờ địa phương của chi nhánh, xét thời điểm bắt đầu sớm nhất), `BranchIds` (danh sách `Guid`), `Channels` (`ONLINE`, `WALK_IN`), `Tiers` (`CustomerTier`, `STANDARD` đến `DIAMOND`), `IsGuest` (`bool`: `true` chỉ khách vãng lai, `false` chỉ thành viên). Giá trị enum ngoài tập hợp lệ bị từ chối. |

Ví dụ nằm ở [`README.md`](../../README.md).

### Tadss:Shadow

Lớp `ShadowOptions`, project `TADSS.Application`. Chạy thêm một strategy ứng viên ở nền để so sánh (A-11); không ảnh hưởng response.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Shadow:Enabled` | `bool` | `false` | Bật shadow. Không nạp lại nóng. |
| `Tadss:Shadow:Strategy` | `string` | `""` | Tên strategy chạy shadow, phải có trong registry; bắt buộc không rỗng khi `Enabled` là `true`. Trùng strategy chính của request thì bỏ qua. |
| `Tadss:Shadow:SampleRate` | `double` | `1.0` | Tỷ lệ request được chạy shadow, trong [0, 1]: 1 luôn chạy, 0 không chạy. |
| `Tadss:Shadow:QueueCapacity` | `int` | `256` | Sức chứa hàng đợi việc shadow, 1..10000. Đầy thì việc mới bị bỏ và tăng `tadss.shadow.dropped{reason="queue_full"}`. |

### Tadss:Warmup

Lớp `WarmupOptions`, project `TADSS.Application`. Làm nóng đường xử lý quyết định một lần lúc khởi động, sau migration và trước khi nhận request (`StartupTasks`): mở sẵn kết nối PostgreSQL bằng truy vấn idempotency chỉ đọc, đọc cache, chạy mọi strategy đã đăng ký qua pipeline và dựng bản ghi như request thật. **Không** ghi audit, không ghi cache, không gửi shadow, không đếm quyết định; chỉ `tadss.pipeline.duration` nhận vài mẫu lúc khởi động. Lỗi của một bước hay hết ngân sách chỉ ghi Warning, instance vẫn khởi động. Không có bước này, instance lạnh nhận tải dồn có thể chậm 5–56 s ở những request đầu (đo ở lượt `EV-20261015-EVAL-full-r3` và thí nghiệm 16/10/2026).

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Warmup:Enabled` | `bool` | `true` | Bật làm nóng. |
| `Tadss:Warmup:DatabaseConnections` | `int` | `10` | Số kết nối PostgreSQL mở sẵn (mỗi kết nối một truy vấn chỉ đọc chạy song song), 0..100; 0 thì bỏ bước này. Kết nối rảnh quá thời gian sống của pool Npgsql vẫn bị đóng như bình thường. |
| `Tadss:Warmup:Iterations` | `int` | `5` | Số vòng chạy thử đường xử lý, mỗi vòng chạy mọi strategy đã đăng ký một lần, 1..100. |
| `Tadss:Warmup:TimeoutMs` | `int` | `15000` | Ngân sách thời gian cho cả lượt làm nóng (mili giây), 100..120000. Hết ngân sách thì dừng làm nóng và khởi động tiếp. |

### Tadss:ErrorHandling

Lớp `ErrorHandlingOptions`, project `TADSS.Runtime.Api`.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:ErrorHandling:RetryAfterSeconds` | `int` | `5` | Số giây client nên chờ trước khi thử lại, trả trong header `Retry-After` và trường `retryAfterSeconds` của lỗi tạm thời (503 `PERSISTENCE_UNAVAILABLE`, 504 `GATEWAY_TIMEOUT`). Ràng buộc: 1..3600 (lớn hơn 1 giờ gần như chắc chắn là nhầm đơn vị). |

### Tadss:Security

Lớp `SecurityOptions`, project `TADSS.Runtime.Api`. Xác thực API key, trần body, giới hạn tốc độ. Thông báo lỗi chỉ nêu chỉ số phần tử và tên trường, không bao giờ chứa mã băm. Cách tạo mã băm: mục "Tạo API key và mã băm" trong [`README.md`](../../README.md).

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Security:ApiKeys` | `List<ApiKeyClientOptions>` | danh sách rỗng | Các client được gọi `/api/*`, mỗi phần tử `{ ClientId, KeySha256 }` (không bao giờ lưu khóa gốc). `ClientId`: 1..64 ký tự `A-Za-z0-9._-`, duy nhất không phân biệt hoa thường, không dùng tên dành riêng `health` và `anonymous`. `KeySha256`: SHA-256 (UTF-8) của khóa, hex 64 ký tự, duy nhất. Danh sách rỗng thì mọi `/api/*` bị 401; ở `Development` và `Testing` ứng dụng vẫn khởi động (ghi Warning), ở môi trường khác ứng dụng **không khởi động** và cũng từ chối mã băm của khóa local công khai. |
| `Tadss:Security:MaxRequestBodyBytes` | `long` | `65536` | Trần kích thước body (byte), 1024..1048576. Vượt thì 413 `PAYLOAD_TOO_LARGE`, kiểm tra trước xác thực. |
| `Tadss:Security:RateLimiting` | `RateLimitingOptions` | `PermitLimit` 100, `TokensPerPeriod` 100, `ReplenishmentPeriodSeconds` 1 | Giới hạn tốc độ token bucket, mỗi `ClientId` một xô, tính theo từng instance; hết token thì 429 `RATE_LIMITED`, `/health/*` không bị giới hạn. Khóa con: `PermitLimit` (dung lượng xô, 1..100000, cũng là số request liên tiếp tối đa khi xô đầy), `TokensPerPeriod` (số token nạp mỗi chu kỳ, 1..`PermitLimit`), `ReplenishmentPeriodSeconds` (chu kỳ nạp, 1..3600 giây). Biến môi trường: `Tadss__Security__RateLimiting__PermitLimit` và tương tự. |
| `Tadss:Security:TrustForwardedHeaders` | `bool` | `false` | Tin `X-Forwarded-For/Proto/Host` từ reverse proxy (ngrok). Chỉ bật khi chỉ proxy chạm được ứng dụng, vì khi chạy trực tiếp client có thể giả các header này. `docker-compose.yml` đặt `true`. |

### Tadss:Database

Lớp `DatabaseOptions`, project `TADSS.Infrastructure`.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Database:ConnectionString` | `string?` | không có | **Không đặt khóa này.** Giá trị lấy từ `ConnectionStrings:DefaultConnection` của cấu hình cuối cùng trong DI (không bind từ section). Ràng buộc: là chuỗi kết nối PostgreSQL parse được và có `Host`; thiếu, sai hoặc còn là ô giữ chỗ thì ứng dụng không khởi động ở mọi môi trường. Chuỗi thật không được ghi vào repo. |
| `Tadss:Database:ApplyMigrationsOnStartup` | `bool` | `false` | Tự áp migration EF Core khi khởi động. Chỉ bật cho database thử dùng một lần (Testcontainers, `verify-expose.sh`, chế độ đánh giá); không bật cho Neon hay staging. `docker-compose.yml` luôn đặt `false` cho service vận hành, migration ở đó do người vận hành chạy `dotnet ef database update`. |

### Tadss:Resilience

Lớp `ResilienceOptions`, project `TADSS.Infrastructure`. Pipeline Polly cho mỗi lượt gọi strategy. Ràng buộc chung về ngân sách thời gian: `InnerTimeoutMs × (MaxRetryAttempts + 1) + RetryBaseDelayMs × MaxRetryAttempts` không vượt `OuterTimeoutMs`.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Resilience:OuterTimeoutMs` | `int` | `500` | Hard deadline toàn cục cho cả chu trình ra quyết định (ms); lớn hơn 0. |
| `Tadss:Resilience:InnerTimeoutMs` | `int` | `120` | Timeout tối đa cho một lượt gọi strategy (ms); lớn hơn 0. Mặc định để 3 attempt cùng 2 lần chờ retry nằm trong SLA 500 ms. |
| `Tadss:Resilience:MaxRetryAttempts` | `int` | `2` | Số lần retry tối đa khi gặp lỗi nhất thời (mặc định tối đa 3 attempt); không âm. |
| `Tadss:Resilience:RetryBaseDelayMs` | `int` | `30` | Thời gian chờ cơ bản giữa các lần retry (ms); lớn hơn 0. |
| `Tadss:Resilience:CircuitBreakerSamplingSeconds` | `int` | `30` | Khung lấy mẫu tỷ lệ lỗi của circuit breaker (giây); lớn hơn 0. |
| `Tadss:Resilience:CircuitBreakerFailureRatio` | `double` | `0.5` | Tỷ lệ lỗi tối thiểu để mở mạch, trong (0, 1]. |
| `Tadss:Resilience:CircuitBreakerMinimumThroughput` | `int` | `10` | Số request tối thiểu trong khung lấy mẫu trước khi đánh giá tỷ lệ lỗi; từ 2 trở lên. |
| `Tadss:Resilience:CircuitBreakerBreakDurationSeconds` | `int` | `15` | Thời gian giữ mạch mở trước khi thử Half-Open (giây); lớn hơn 0. |

### Tadss:Cache

Lớp `CacheOptions`, project `TADSS.Infrastructure`. Cache phân tán (Redis) kèm fallback memory. Breaker mở thì request bỏ qua Redis ngay (dùng memory nếu `EnableFallbackToMemory`), không chờ `RedisTimeoutMs`; breaker và cache memory đều tính theo từng instance. Khóa đầy đủ mặc định có dạng `tadss:v2:...`; TTL do strategy khai báo (`CacheTtlMinutes`), section này không có TTL.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Cache:Provider` | `string` | `Redis` | Nhà cung cấp cache chính: `Redis` hoặc `Memory` (không phân biệt hoa thường). |
| `Tadss:Cache:RedisConnectionString` | `string` | mã: `localhost:6379`; `appsettings.json`: ô giữ chỗ không dùng được | Kết nối Redis dạng `host:port[,user=...,password=...]`. Giá trị thật chỉ đặt ở file cấu hình môi trường hoặc biến môi trường, không ghi vào repo. Host còn là ô giữ chỗ của template thì timeout kết nối dùng `RedisTimeoutMs` để không chờ vô ích. |
| `Tadss:Cache:InstanceName` | `string` | `tadss:` | Tiền tố cộng vào mọi khóa cache (Redis và memory); không rỗng, tối đa 32 ký tự, không chứa khoảng trắng. Đặt tiền tố riêng khi nhiều instance dùng chung một Redis mà không được dùng chung khóa. |
| `Tadss:Cache:EnableFallbackToMemory` | `bool` | `true` | Tự chuyển sang cache memory khi Redis mất kết nối hoặc timeout. |
| `Tadss:Cache:RedisTimeoutMs` | `int` | `150` | Timeout tối đa cho một thao tác Redis (ms) trước khi chuyển sang fallback; lớn hơn 0. |
| `Tadss:Cache:RedisConnectTimeoutMs` | `int` | `3000` | Timeout tối thiểu khi kết nối Redis (ms); lớn hơn 0. Timeout kết nối thực tế là `max(RedisTimeoutMs, RedisConnectTimeoutMs)`. |
| `Tadss:Cache:RedisBreakerFailureRatio` | `double` | `0.5` | Tỷ lệ lệnh Redis lỗi hoặc quá hạn trong khung lấy mẫu để breaker mở, trong (0, 1]. |
| `Tadss:Cache:RedisBreakerSamplingSeconds` | `int` | `30` | Độ dài khung lấy mẫu của breaker Redis (giây); từ 1 trở lên. |
| `Tadss:Cache:RedisBreakerMinimumThroughput` | `int` | `10` | Số lệnh Redis tối thiểu trong khung trước khi breaker được phép mở; từ 2 trở lên. |
| `Tadss:Cache:RedisBreakerBreakDurationSeconds` | `int` | `30` | Thời gian breaker giữ trạng thái mở trước khi thăm dò lại (giây); từ 1 trở lên. |

### Tadss:Telemetry

Lớp `TelemetryOptions`, project `TADSS.Infrastructure`. Gửi trace, metric và log tới collector OpenTelemetry (`grafana/otel-lgtm`). Mặc định tắt: khi `Enabled` là `false` không có provider OpenTelemetry nào được đăng ký và worker làm mới gauge hàng chờ không truy vấn. Collector không tới được thì request không bị ảnh hưởng. `docker compose up` bật sẵn và trỏ tới Grafana của compose.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Telemetry:Enabled` | `bool` | `false` | Bật telemetry. |
| `Tadss:Telemetry:OtlpEndpoint` | `string` | `http://localhost:4317` | Địa chỉ collector; phải là URI tuyệt đối http hoặc https khi `Enabled` là `true`. |
| `Tadss:Telemetry:OtlpProtocol` | `TelemetryOtlpProtocol` | `Grpc` | Giao thức OTLP: `Grpc` (cổng 4317 của collector) hoặc `HttpProtobuf` (cổng 4318). |
| `Tadss:Telemetry:ServiceName` | `string` | `tadss-runtime` | Giá trị resource `service.name` (nhãn `job`); không rỗng, tối đa 64 ký tự. |
| `Tadss:Telemetry:ServiceInstanceId` | `string` | `""` | Giá trị `service.instance.id` (nhãn `instance`); rỗng thì dùng tên máy hoặc container. |
| `Tadss:Telemetry:TraceSampleRatio` | `double` | `1.0` | Tỷ lệ lấy mẫu trace `ParentBased(TraceIdRatioBased)`, trong [0, 1]; metric và audit không lấy mẫu. |
| `Tadss:Telemetry:ExportLogs` | `bool` | `true` | Gửi log tới collector (sink OTLP/Loki) ngoài console, khi `Enabled`. |
| `Tadss:Telemetry:MetricExportIntervalMs` | `int` | `10000` | Chu kỳ xuất metric (ms), 1000..60000. |
| `Tadss:Telemetry:PendingApprovalsRefreshSeconds` | `int` | `30` | Chu kỳ làm mới gauge số quyết định chờ duyệt (giây), 5..3600. |

### Tadss:Testing:FaultInjection

Lớp `FaultInjectionOptions`, project `TADSS.Infrastructure`. Tiêm lỗi vào strategy để kiểm thử độ chịu lỗi (A-07, kịch bản LT-03). Options luôn được đăng ký và kiểm tra, nhưng decorator chỉ bọc strategy khi môi trường là `Testing` **và** `Enabled` là `true`; `Enabled` ở môi trường khác chỉ sinh Warning khi khởi động. Giới hạn của profile bỏ qua token: xem [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../architecture/RESILIENCE_AND_FAULT_TAXONOMY.md).

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Testing:FaultInjection:Enabled` | `bool` | `false` | Bật tiêm lỗi (chỉ có tác dụng khi `ASPNETCORE_ENVIRONMENT=Testing`). |
| `Tadss:Testing:FaultInjection:Profile` | `FaultInjectionProfile` | `None` | Kiểu lỗi: `None` (gọi thẳng strategy thật), `Delay` (chờ `DelayMs` có tôn trọng token hủy), `Throw` (ném `InvalidOperationException` với message cố định), `IgnoreToken` (chờ `DelayMs` không nhìn token hủy), `BusyCpu` (quay vòng CPU `DelayMs`, không kiểm token). |
| `Tadss:Testing:FaultInjection:DelayMs` | `int` | `1000` | Thời gian chờ hoặc quay vòng (ms) của các profile `Delay`, `IgnoreToken`, `BusyCpu`; hợp lệ 1..60000. |
| `Tadss:Testing:FaultInjection:TargetStrategy` | `string?` | không có | Tên strategy bị tiêm lỗi (không phân biệt hoa thường); rỗng hoặc bỏ trống là mọi strategy; không được chỉ gồm khoảng trắng. |

## Section của strategy

Hai strategy không có hằng `SectionName` trên lớp options: runtime bind từ `Tadss:Strategies:{Default|MockAlternative}` (hằng ở `StrategyRegistrations`), kiểm bằng `Validate()` của options và đưa tham số vào `optionsFingerprint`. Thiếu section thì dùng giá trị mặc định của lớp. Tham số sai làm ứng dụng không khởi động; tham số đổi phải khởi động lại. Giá trị trong `appsettings.json` của hai section bằng đúng mặc định của mã.

### Tadss:Strategies:Default

Lớp `DefaultStrategyOptions`, project `TADSS.Strategies.Default` (tất cả setter là `init`). Giá trị mặc định là chính sách SmashCourt; đơn vị tiền theo tiền tệ của request.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Strategies:Default:HighValueThreshold` | `decimal` | `2000000` | Tổng tiền từ ngưỡng này trở lên cần Manager duyệt; không âm. |
| `Tadss:Strategies:Default:NoShowReviewThreshold` | `int` | `2` | Số lần không đến từ ngưỡng này trở lên cần Manager duyệt; từ 1 trở lên. |
| `Tadss:Strategies:Default:PaymentExpiryRejectThreshold` | `int` | `5` | Số lần hết hạn thanh toán (giữ chỗ nhưng không thanh toán) từ ngưỡng này trở lên, đồng thời nhiều hơn số lần hoàn tất, thì từ chối đơn (`REJECTED`, mã `REPEATED_PAYMENT_EXPIRY`); không âm, 0 là tắt quy tắc. Giá trị khởi điểm, chưa hiệu chỉnh theo dữ liệu thật. |
| `Tadss:Strategies:Default:StandardDepositRate` | `decimal` | `0.5` | Tỷ lệ cọc cho khách vãng lai và hạng `STANDARD`; trong [0, 1], tối đa 4 chữ số thập phân. |
| `Tadss:Strategies:Default:SilverDepositRate` | `decimal` | `0.4` | Tỷ lệ cọc hạng `SILVER`; trong [0, 1], tối đa 4 chữ số thập phân. |
| `Tadss:Strategies:Default:GoldDepositRate` | `decimal` | `0.3` | Tỷ lệ cọc hạng `GOLD`; trong [0, 1], tối đa 4 chữ số thập phân. |
| `Tadss:Strategies:Default:PremiumDepositRate` | `decimal` | `0.2` | Tỷ lệ cọc hạng `PLATINUM` và `DIAMOND`; trong [0, 1], tối đa 4 chữ số thập phân. |
| `Tadss:Strategies:Default:CacheTtlMinutes` | `int` | `60` | Thời gian sống của cache (phút); không âm, 0 là không cache. |

### Tadss:Strategies:MockAlternative

Lớp `MockAlternativeStrategyOptions`, project `TADSS.Strategies.MockAlternative` (tất cả setter là `init`). Strategy thay thế dùng để thử định tuyến và shadow.

| Khóa | Kiểu | Mặc định | Ý nghĩa / ràng buộc |
| :--- | :--- | :--- | :--- |
| `Tadss:Strategies:MockAlternative:HighValueThreshold` | `decimal` | `1500000` | Tổng tiền từ ngưỡng này trở lên vào giờ cao điểm cần Manager duyệt; không âm. |
| `Tadss:Strategies:MockAlternative:PeakDepositRate` | `decimal` | `0.5` | Tỷ lệ cọc giờ cao điểm; trong [0, 1], tối đa 4 chữ số thập phân. |
| `Tadss:Strategies:MockAlternative:OffPeakDepositRate` | `decimal` | `0.3` | Tỷ lệ cọc ngoài giờ cao điểm; trong [0, 1], tối đa 4 chữ số thập phân. |
| `Tadss:Strategies:MockAlternative:CacheTtlMinutes` | `int` | `60` | Thời gian sống của cache (phút); không âm, 0 là không cache. |
