# Báo cáo gói 5.4 — Runtime thích ứng (Task 5–8; Task 1–4 đã commit ở `b179d8b`)

Trạng thái: DONE (kèm ghi chú ở mục 7). Nhánh `feat/phase-5-contract-v2-test-infra`. Các thay đổi của Task 5–8 **chưa commit** (để người dùng review và commit); không push.

## 1. Kiểm chứng cuối gói (số liệu thật)

| # | Kiểm chứng | Kết quả |
| :-: | :--- | :--- |
| 1 | `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror --no-incremental` | 0 Warning, 0 Error |
| 2 | Unit (`TADSS.UnitTests`) | **1707 passed**, 0 failed, 0 skipped (đầu gói 1518: +189) |
| 2 | Integration (Docker/Testcontainers) | **211 passed**, 0 failed, 0 skipped (đầu gói 197: +14); chạy 3 lần liên tiếp đều xanh: 1 m 24 s, 1 m 01 s, 1 m 17 s |
| 3 | Số test sau từng task (Task 5–7, đo trong phiên này) | Task 5: 1677 / 203; Task 6: 1706 / 208; Task 7: 1706 / 211; thêm 1 unit test sau rà soát (timeout được breaker Redis đếm): 1707 / 211 |
| 4 | `docs/contracts/openapi-v2.yaml`, `schemas/v2/*.json`, `baseline_public_api.txt` so với `aa5fa34` | **không đổi**. `git diff aa5fa34 HEAD -- docs/contracts/` không rỗng vì commit `b179d8b` (Task 1–4) sửa 3 dòng văn xuôi của `INTEGRATION_SPEC.md` (ngày đóng băng, tuần của T5.22, bỏ chữ "pilot"); không phải thay đổi wire. Working tree so với HEAD: rỗng |
| 5 | `dotnet ef migrations list --no-connect` (`Testing`) | đúng 4 ID: `20260912105336_InitialCreate`, `20260915152502_UpdateUniqueIndexes`, `20260930190021_AddCreatedAtIndex`, `20261002101457_DecisionPersistenceV2` |
| 5 | `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| 6 | `bash tools/expose/verify-expose.sh` | TẤT CẢ ĐẠT: NG-1, NG-2a, NG-2b, NG-3, NG-5, SEC-6 |
| 7 | App thật (Release, môi trường `Testing`) trên PostgreSQL 16 + Redis 7 tạm, `Shadow:Enabled = true` (strategy `DefaultDecisionStrategy`), một rule `peak-hour → MockAlternativeDecisionStrategy` | `/health/live` = 200; một `POST /api/v2/decisions` (request mẫu, thứ Sáu 18:00) → 200 `MockAlternativeDecisionStrategy` cọc 50%; DB có đúng hai bản ghi: `PRIMARY` (Mock, có khóa idempotency) và `SHADOW` (Default, `idempotency_key` NULL); log không có dòng lỗi |
| 8 | grep `E-LOCAL` trong `src/`, `tests/` | 1 kết quả: `MigrationIntegrationTests.cs:70`, là assertion **phủ định** `!entry.Message.Contains("E-LOCAL")` (đúng như plan yêu cầu test "không chứa E-LOCAL"); không còn chuỗi này ở mã/message |
| 8 | grep `ActiveStrategyName` trong `src/` | chỉ còn hằng khóa cũ trong `StartupTasks.cs` (message Warning A-03) |
| 8 | grep `TtlHours` trong `src/`, `README.md` | rỗng |
| 8 | grep `SC-[123]` trong tài liệu hiện hành | rỗng |
| 9 | Link nội bộ của mọi `.md` đã sửa (README, CLAUDE.md, 6 file `docs/`) | không có link hỏng |
| 10 | Dọn dẹp | container PostgreSQL/Redis tạm, tiến trình app và file tạm đã xóa; compose của `verify-expose.sh` tự dọn; không còn container/image `tadss*` |

## 2. Phụ lục A — đối chiếu spec → task, có trạng thái

| Yêu cầu | Task | Trạng thái |
| :--- | :---: | :--- |
| Routing `Tadss:Routing`, AND, rule đầu khớp | 4 | ✅ (`b179d8b`) |
| Cấu hình sai lúc khởi động → fail-fast; reload sai → giữ cũ, log, `tadss.config.reload_failures` | 4 | ✅ (`b179d8b`) |
| `ActiveStrategyName` thay bằng `Routing.DefaultStrategy` | 4 | ✅ (`b179d8b`) |
| Pipeline theo strategy (V-2) | 2 | ✅ (`b179d8b`) |
| `FaultInjectionStrategyDecorator` chỉ ở `Testing` + `Enabled` | 3 | ✅ (`b179d8b`) |
| `IsolatedStrategyInvoker`, `tadss.strategy.abandoned`, giới hạn ghi vào Mục 8 | 2, 8 | ✅ (mã ở `b179d8b`; giới hạn ghi vào EXPERIMENTAL_DESIGN Mục 8, SAS Mục 3, RESILIENCE) |
| Redis qua pipeline riêng (timeout + breaker), bypass dùng memory, metric `bypass`; lỗi cache không kích hoạt fallback | 5 | ✅ |
| Tiền tố khóa cache theo cấu hình (`InstanceName`), xóa `TtlHours` | 5 | ✅ |
| Shadow: `Tadss:Shadow`, `Channel` có giới hạn, đếm việc bị bỏ, worker, bản `SHADOW` | 6 | ✅ |
| IT-12 gồm shadow lỗi/treo không ảnh hưởng `PRIMARY` | 1, 6 | ✅ |
| Nhiều instance: 50 request song song hai host, cache chéo host, breaker theo host | 1, 7 | ✅ |
| Mang từ 5.3/ADR-013: tiền tố khóa cache, `InstanceName`/`TtlHours`, log ERR `23505`, `"ready"`, E-LOCAL | 1, 5 | ✅ |
| Tài liệu SAS, RESILIENCE, INVARIANTS, ADR, EXPERIMENTAL_DESIGN, BACKLOG, README, CLAUDE.md, skill | 8 | ✅ |

## 3. Bảng 6.1 của spec (các dòng thuộc 5.4)

| Bất biến | Test khóa | Kết quả |
| :--- | :--- | :--- |
| Shadow không ảnh hưởng `PRIMARY` (INV-8) | IT-12: `PostDecision_ShadowEnabled_StoresShadowRecordAndKeepsResponse`, `PostDecision_ShadowStrategyThrowsOrHangs_PrimaryUnaffected_Theory` (ném lỗi và treo), `ShadowRecord_NotInPendingQueue_AndNotOverridable`, `PostDecision_IdempotentReplay_NoSecondShadow`; unit: `ShadowDispatcherTests`, `ShadowExecutorTests`, `DecisionServiceTests` (chỉ gửi việc khi ghi `PRIMARY` mới) | ✅ |
| Decorator tiêm lỗi không có ở `Production`/`Staging`/`Development` | `FaultInjectionRegistrationTests` (UT-09, Task 3) | ✅ |
| Cấu hình routing sai không khởi động | `Host_InvalidRouting_FailsOnStart_Theory` (UT-04, Task 4) | ✅ |
| Idempotency/cache/breaker đúng khi chạy nhiều instance | SCALE-1: `MultiInstanceTests` (3 test: 50 request song song hai host → 1 bản `PRIMARY` và 50 response giống nhau; cache ghi ở host A đọc ở host B là `CACHE_HIT` không gọi strategy; breaker host A mở, host B vẫn `AUTOMATED`) | ✅ |
| Breaker Redis; lỗi cache không kích hoạt fallback | UT-13: `RedisDecisionCacheTests` (mở, quá hạn, nửa mở → đóng, payload hỏng không tính, hủy của caller), `StrategyDecisionCacheTests` (đúng một số đo mỗi lần tra, khóa mặc định `tadss:v2:...`); FI-06: `F6_RedisCut_BreakerOpens_RequestsBypassRedis` (Toxiproxy) | ✅ |

## 4. Việc của người dùng

- Xóa khóa `Tadss:Strategy:ActiveStrategyName` và khóa `TtlHours` (`Tadss:Cache`) khỏi `appsettings.Development.json`: hiện file đó vẫn còn cả hai (chỉ kiểm tên khóa, không đọc giá trị). Chúng không gây lỗi (khóa cũ chỉ sinh Warning, khóa còn lại bị bỏ qua) nhưng nên gỡ. Muốn dùng strategy khác mặc định thì đặt `Tadss:Routing:DefaultStrategy`.
- Review rồi commit thay đổi Task 5–8 (đề xuất bên dưới) và tạo PR vào `develop`.
- Docker Desktop: tôi đã khởi động nó để chạy IT/verify-expose (Docker chưa chạy lúc bắt đầu); tắt lại nếu không cần.

Commit đề xuất (theo thứ tự):
1. `feat(cache): redis circuit breaker and configurable key namespace`
2. `feat(shadow): asynchronous shadow evaluation with bounded queue`
3. `test: multi-instance idempotency, cache and breaker isolation`
4. `docs: adaptive runtime routing, shadow and isolation (package 5.4)`

## 5. Thay đổi ở Task 5–8, theo nhóm

- **Task 5 (cache):** thêm `RedisDecisionCache`, `RedisCacheResilience`, `RedisCacheStatus`, `RedisReadResult` (Infrastructure/Caching); `StrategyDecisionCache` gọn lại (142 dòng), cộng `InstanceName`, ghi `tadss.cache.requests`; `StrategyDecisionCacheKey` bỏ tiền tố `tadss:`; `CacheOptions` + validator thêm `RedisBreaker*`, kiểm `InstanceName`, xóa `TtlHours`; `appsettings.json` đồng bộ. Test thay thế có chủ đích: `Build_AllParts_FollowsSpecFormat` → `Build_ReturnsVersionedKeyWithoutNamespace`; `CacheOptions_Invalid_FailsOnStart_Theory` đổi ca `TtlHours` thành các ca `RedisBreaker*`/`InstanceName`; `StrategyDecisionCacheTests`/`RegistrationTests` đổi khóa sang dạng không tiền tố và khóa đầy đủ.
- **Task 6 (shadow):** `ShadowOptions`/validator (Application/Options), `ShadowWorkItem`, `IShadowDispatcher`, `ShadowDispatcher`, `ShadowExecutor` (Application/Shadow), `ShadowExecutionWorker` (Infrastructure/Shadow); `DecisionComposition` thêm `Role` (mặc định `PRIMARY`) và `IdempotencyKey` nullable, composer ghép theo `Role` và ném lỗi khi khóa idempotency không khớp role; `DecisionService` gửi việc shadow sau khi ghi `PRIMARY` mới; `StartupTasks` resolve sớm dispatcher; `appsettings.json` thêm `Tadss:Shadow`.
- **Task 7 (SCALE-1):** `Scale/MultiInstanceTests.cs`; `FaultInjectionTestBase` thêm `ProxyRedis`, `ConfigureCache`, `CacheLogs`, `ApplyResilience`; test dùng `ExtendedStrategyRegistry` (strategy thật + strategy giả) ở IT shadow. Không sửa `src/` để làm SCALE-1 (không phát hiện lỗi thật).
- **Task 8 (tài liệu):** README (mục "Runtime thích ứng", trạng thái, số test), SAS (Mục 3, 4.2, 4.4.2, 4.5), RESILIENCE_AND_FAULT_TAXONOMY, DECISION_PERSISTENCE_INVARIANTS (INV-8), ARCHITECTURE_DECISION_RECORDS (ADR-013 "để lại cho 5.4" đã xử lý), EXPERIMENTAL_DESIGN Mục 8, BACKLOG_SPRINTS (T5.9–T5.11), `TADSS-Runtime/CLAUDE.md`, skill `tadss-architecture` (hàng metric, định tuyến/shadow, decorator).

## 6. Mục chuyển sang gói 5.5 (không thuộc 5.4)

- Gauge trạng thái breaker theo strategy (`tadss.circuit.state`, cần đọc breaker từ registry pipeline), OpenTelemetry exporter và các metric còn lại của SAS 4.5 (`AddMeter(TadssMetrics.MeterName)`), dashboard.
- Thứ tự `UseSerilogRequestLogging` (ghi "500" cho phản hồi do exception tạo ra).
- SCALE-2 (hai replica sau reverse proxy), SCALE-3, rate limit phân tán.
- LT-03 (sự cố dưới tải) dùng decorator tiêm lỗi: `ASPNETCORE_ENVIRONMENT=Testing` + `Tadss:Testing:FaultInjection:*`; FI-07 (Redis chậm dưới tải).

## 7. Concerns / quan sát

1. **Breaker Redis đặt ngoài timeout:** plan ghi "Timeout → Circuit breaker" mơ hồ về thứ tự; tôi đặt breaker ở ngoài, timeout ở trong để quá hạn (`TimeoutRejectedException`) được breaker đếm là lỗi, đúng yêu cầu "mọi exception của Redis và timeout là lỗi". Hủy của chính caller không được đếm.
2. **Test flaky có từ Task 2, đã sửa tối thiểu:** `StrategyPipelineTests.ExecuteAsync_StrategySucceeds_Automated` (inner timeout 30 ms + `Task.Run` của `IsolatedStrategyInvoker`) thỉnh thoảng đỏ khi chạy song song toàn bộ suite (`InvocationCount` = 2). Tôi nới `innerTimeoutMs` của riêng test thành công này lên 2000 ms (không đổi assertion, không đụng test timeout/retry/breaker). Sau đó unit xanh 3/3 lần chạy đầy đủ. Các test khác dùng `SmallOptions()` với timeout 30 ms vẫn có thể có rủi ro tương tự dưới tải nặng; chưa thấy đỏ.
3. **`ShadowDispatcher.Reader` khi tắt:** trả một hàng đợi rỗng đã đóng (dùng chung, tĩnh) thay vì ném, để test "tắt thì không có việc" đọc được; không tạo channel theo từng instance khi tắt.
4. **Log số việc bị bỏ khi dừng worker** dùng `Reader.Count` (hàng đợi giới hạn hỗ trợ đếm); không có sai lệch với giả định của plan.
5. **F6** phụ thuộc Toxiproxy ngắt Redis; breaker mở sau vài lệnh lỗi (ngưỡng đặt 2 qua `ConfigureCache`); mặc định production là 10 lệnh.
6. **Shadow trong IT** dùng đồng hồ thật (Polly cần timer thật) và host đối chứng tắt shadow dùng chung PostgreSQL/Redis; Redis được xóa trước lượt đối chứng để nó không đọc `CACHE_HIT` do host bật shadow vừa ghi.
