using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.BenchmarkRunner.Protocol;

namespace TADSS.BenchmarkRunner.Harness;

/// <summary>
/// Thực thi pha đo đạc tải đồng thời (Controlled Concurrent Measurement Phase),
/// xác định chính xác từng request là CACHE_HIT, AUTOMATED hay FALLBACK thông qua tập DecisionId của Strategy.
/// </summary>
public sealed class MeasurementWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ScenarioHostController _hostController;
    private readonly HttpClient _client;
    private readonly RunMetadata _metadata;
    private readonly RawMeasurementRecord[] _records;
    private readonly int _totalRequests;
    private readonly int _concurrency;
    private readonly int _maxErrorLogEntries;

    private int _nextRequestIndex = -1;
    private int _loggedErrorCount;

    public MeasurementWorker(
        ScenarioHostController hostController,
        HttpClient client,
        RunMetadata metadata,
        int totalRequests,
        int concurrency,
        int maxErrorLogEntries = BenchmarkDefaults.Execution.MaxErrorLogEntriesPerRun)
    {
        _hostController = hostController;
        _client = client;
        _metadata = metadata;
        _totalRequests = totalRequests;
        _concurrency = concurrency;
        _maxErrorLogEntries = maxErrorLogEntries;
        _records = new RawMeasurementRecord[totalRequests];
    }

    public async Task<(IReadOnlyList<RawMeasurementRecord> Records, double WallClockMs)> RunAllAsync()
    {
        var wallClockSw = Stopwatch.StartNew();

        var workerTasks = new Task[_concurrency];
        for (int w = 0; w < _concurrency; w++)
        {
            workerTasks[w] = Task.Run(ExecuteWorkerLoopAsync);
        }

        await Task.WhenAll(workerTasks);
        wallClockSw.Stop();

        return (_records, wallClockSw.Elapsed.TotalMilliseconds);
    }

    private async Task ExecuteWorkerLoopAsync()
    {
        while (true)
        {
            int idx = Interlocked.Increment(ref _nextRequestIndex);
            if (idx >= _totalRequests)
            {
                break;
            }

            _records[idx] = await MeasureSingleRequestAsync(idx);
        }
    }

    private async Task<RawMeasurementRecord> MeasureSingleRequestAsync(int requestIndex)
    {
        var payload = _hostController.CreatePayloadForRequest(requestIndex, isWarmup: false);
        string expectedRequestHash = TADSS.Runtime.Api.Services.RequestHasher.ComputeHash(payload);
        var idempotencyKey = $"{_metadata.RunId}-REQ-{requestIndex:D6}";

        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(payload)
        };
        reqMsg.Headers.Add("X-Idempotency-Key", idempotencyKey);

        long startTick = Stopwatch.GetTimestamp();
        int statusCode = 0;

        try
        {
            using var response = await _client.SendAsync(reqMsg);
            long endTick = Stopwatch.GetTimestamp();
            double elapsedMs = Stopwatch.GetElapsedTime(startTick, endTick).TotalMilliseconds;
            statusCode = (int)response.StatusCode;

            Guid decisionId = Guid.Empty;
            Guid reservationId = payload.ReservationId;
            string decisionStatus = "ERROR";
            string executionType = "NONE";
            bool fallbackUsed = false;
            bool isCacheHit = false;
            int serverExecMs = 0;
            bool isSafeCompleted = false;

            if (response.IsSuccessStatusCode)
            {
                var dto = await response.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
                if (dto != null)
                {
                    decisionId = dto.DecisionId;
                    reservationId = dto.ReservationId;
                    decisionStatus = dto.Status;
                    serverExecMs = dto.ExecutionTimeMs;
                    fallbackUsed = dto.FallbackUsed;
                    bool evaluatedByStrategy = _hostController.WasDecisionEvaluatedByStrategy(dto.DecisionId);
                    isCacheHit = !fallbackUsed && !evaluatedByStrategy && !_hostController.Descriptor.DisableCache;

                    executionType = fallbackUsed
                        ? "FALLBACK"
                        : (isCacheHit ? "CACHE_HIT" : "AUTOMATED");
                    isSafeCompleted = true;
                }
            }

            return new RawMeasurementRecord
            {
                RunId = _metadata.RunId,
                Scenario = _metadata.Scenario,
                Concurrency = _concurrency,
                RequestIndex = requestIndex,
                DecisionId = decisionId,
                ReservationId = reservationId,
                ExpectedRequestHash = expectedRequestHash,
                ClientElapsedMs = elapsedMs,
                ServerExecutionTimeMs = serverExecMs,
                HttpStatusCode = statusCode,
                DecisionStatus = decisionStatus,
                ExecutionType = executionType,
                FallbackUsed = fallbackUsed,
                CacheHit = isCacheHit,
                IsEligibleForDcr = true, // Not a caller cancellation (F7)
                IsSafeCompletedDecision = isSafeCompleted,
                FailureDetails = isSafeCompleted ? null : $"HTTP_{statusCode}"
            };
        }
        catch (Exception ex)
        {
            long endTick = Stopwatch.GetTimestamp();
            double elapsedMs = Stopwatch.GetElapsedTime(startTick, endTick).TotalMilliseconds;

            string classifiedStatus = ClassifyException(ex);
            string truncatedMessage = ex.Message.Length > BenchmarkDefaults.Execution.MaxExceptionDetailLength
                ? ex.Message[..BenchmarkDefaults.Execution.MaxExceptionDetailLength]
                : ex.Message;

            if (Interlocked.Increment(ref _loggedErrorCount) <= _maxErrorLogEntries)
            {
                await Console.Error.WriteLineAsync(
                    $"  [DIAGNOSTIC] Run={_metadata.RunId} Req={requestIndex} Classification={classifiedStatus}: {ex.GetType().Name} - {truncatedMessage}");
            }

            return new RawMeasurementRecord
            {
                RunId = _metadata.RunId,
                Scenario = _metadata.Scenario,
                Concurrency = _concurrency,
                RequestIndex = requestIndex,
                DecisionId = Guid.Empty,
                ReservationId = payload.ReservationId,
                ExpectedRequestHash = expectedRequestHash,
                ClientElapsedMs = elapsedMs,
                ServerExecutionTimeMs = 0,
                HttpStatusCode = statusCode == 0 ? 504 : statusCode,
                DecisionStatus = classifiedStatus,
                ExecutionType = "FAILED",
                FallbackUsed = false,
                CacheHit = false,
                IsEligibleForDcr = true,
                IsSafeCompletedDecision = false,
                FailureDetails = $"{ex.GetType().Name}: {truncatedMessage}"
            };
        }
    }

    public static string ClassifyException(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException => "CLIENT_TIMEOUT",
        HttpRequestException => "NETWORK_ERROR",
        TimeoutException => "OPERATION_TIMEOUT",
        _ => "UNHANDLED_EXCEPTION"
    };
}
