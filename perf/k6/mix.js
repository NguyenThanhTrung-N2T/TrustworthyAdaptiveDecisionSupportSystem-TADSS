// Kịch bản tải hỗn hợp của môi trường perf (Epic 6C, task 2.15; kịch bản SP ở gói 6C-5). Chạy bằng `bash perf/perf.sh run <PROFILE>`.
//   smoke       closed, ít VU, mỗi VU đi vòng đủ J1…J7: kiểm đường đi, không đo
//   load        open (constant-arrival-rate) RATE hành trình/giây trong DURATION, sau warm-up
//   stress      open, tăng bậc 25 % ⇒ 50 % ⇒ 75 % ⇒ 100 % ⇒ 150 % ⇒ 200 % RATE, mỗi bậc STEP
//   spike       open, RATE ⇒ ×5 trong 1 phút ⇒ RATE (đo thời gian hồi phục)
//   soak        open, RATE trong DURATION dài (90–120 phút)
//   breakpoint  open, ramping-arrival-rate tăng đều tới MAX_RATE (tìm điểm gãy)
//   closed      closed (ramping-vus) VUS người dùng có think time giữa hai hành trình: góc nhìn trải nghiệm
// Warm-up 2 phút (WARMUP) gắn tag phase=warmup và bị loại khỏi threshold; số liệu chính thức chỉ lấy phase=steady (plan 6C B.7.3).
// Mô hình open đo theo nhịp hành trình đến, không phụ thuộc hệ thống chậm (tránh coordinated omission của closed model).
import { JOURNEY_WEIGHTS, bookingOutcomes, journeys, loadCatalog, loginPool, pickWeighted, reseed, runJourney, thinkBetweenJourneys, usePool }
  from './lib/journeys.js';

const PROFILE = __ENV.PROFILE || 'smoke';
const RATE = Number(__ENV.RATE || 5);                 // hành trình/giây ở mức danh nghĩa
const DURATION = __ENV.DURATION || '15m';
const WARMUP = __ENV.WARMUP || '2m';
const STEP = __ENV.STEP || '2m';
const MAX_RATE = Number(__ENV.MAX_RATE || RATE * 4);
const MAX_VUS = Number(__ENV.MAX_VUS || 400);
const RUN_ID = __ENV.RUN_ID || `local-${Date.now()}`;
const SMOKE_ORDER = ['J1', 'J2', 'J3', 'J5', 'J6', 'J7'];

function openScenario(executor, extra, phase, startTime) {
  return {
    executor, startTime, timeUnit: '1s', preAllocatedVUs: Math.min(50, MAX_VUS), maxVUs: MAX_VUS,
    exec: 'mixed', tags: { phase }, ...extra,
  };
}

function scenarios() {
  const warmup = openScenario('constant-arrival-rate', { rate: Math.max(1, Math.round(RATE / 2)), duration: WARMUP }, 'warmup', '0s');
  const after = WARMUP;
  switch (PROFILE) {
    case 'smoke':
      // Warm-up 30 s: ngay sau reset BE/TADSS nguội, lời gọi TADSS đầu tiên quá TimeoutMs 800 ms ⇒ đường lùi / 503 khi duyệt (lượt 02/11)
      return {
        warmup: openScenario('constant-arrival-rate', { rate: 2, duration: __ENV.WARMUP || '30s' }, 'warmup', '0s'),
        smoke: { executor: 'per-vu-iterations', vus: Number(__ENV.VUS || 6), iterations: SMOKE_ORDER.length * 2, exec: 'roundRobin',
          maxDuration: '10m', startTime: __ENV.WARMUP || '30s', tags: { phase: 'steady' } },
      };
    case 'load':
    case 'soak':
      return { warmup, steady: openScenario('constant-arrival-rate', { rate: RATE, duration: DURATION }, 'steady', after) };
    case 'stress': {
      const stages = [0.25, 0.5, 0.75, 1, 1.5, 2].map((k) => ({ target: Math.round(RATE * k), duration: STEP }));
      return { warmup, steady: openScenario('ramping-arrival-rate', { startRate: Math.round(RATE / 4), stages }, 'steady', after) };
    }
    case 'spike':
      return { warmup, steady: openScenario('ramping-arrival-rate', { startRate: RATE, stages: [
        { target: RATE, duration: '3m' }, { target: RATE * 5, duration: '10s' }, { target: RATE * 5, duration: '1m' },
        { target: RATE, duration: '10s' }, { target: RATE, duration: '5m' }] }, 'steady', after) };
    case 'breakpoint':
      return { warmup, steady: openScenario('ramping-arrival-rate', { startRate: RATE, stages: [{ target: MAX_RATE, duration: DURATION }] },
        'steady', after) };
    case 'closed':
      return { steady: { executor: 'ramping-vus', startVUs: 0, exec: 'closedUser', tags: { phase: 'steady' },
        stages: [{ target: Number(__ENV.VUS || 50), duration: WARMUP }, { target: Number(__ENV.VUS || 50), duration: DURATION }] } };
    default:
      throw new Error(`PROFILE không hợp lệ: ${PROFILE}`);
  }
}

export const options = {
  scenarios: scenarios(),
  // Mẫu endpoint có id đã gom bằng tag name; không lưu nhãn url thô để Prometheus không bùng nhãn
  systemTags: ['status', 'method', 'name', 'group', 'check', 'error', 'error_code', 'scenario', 'expected_response'],
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
  setupTimeout: '5m',
  thresholds: {
    // Lượt dừng giữa chừng hoặc hỏng đường đi không được ra "đạt": tỷ lệ lỗi (5xx + timeout, không gồm 4xx nghiệp vụ) phải dưới 0,5 %
    // (SLO đề xuất B.7.2; ngưỡng latency theo nhóm thêm ở gói 6C-5 sau khi người dùng chốt SLO, task 2.18)
    'http_req_failed{phase:steady}': ['rate<0.005'],
    // Ngưỡng luôn đạt chỉ để summary có số đếm theo từng hành trình / kết quả đặt sân (tỷ lệ hành trình ≠ tỷ lệ request, B.6.5)
    ...Object.fromEntries(['J1', 'J2', 'J3', 'J4', 'J5', 'J6', 'J7'].map((j) => [`journeys{journey:${j}}`, ['count>=0']])),
    ...Object.fromEntries(['PENDING', 'CONFIRMED', 'PENDING_APPROVAL', 'http_409', 'http_400'].map((o) => [`booking_outcomes{outcome:${o}}`, ['count>=0']])),
  },
};

export function setup() {
  return { catalog: loadCatalog(), pool: loginPool(Number(__ENV.CUSTOMERS || 2000), Number(__ENV.POOL_CUSTOMERS || 300)) };
}

export function mixed(context) {
  usePool(context.pool);
  reseed(__VU, __ITER);
  runJourney(pickWeighted(JOURNEY_WEIGHTS), context.catalog);
}

export function roundRobin(context) {
  usePool(context.pool);
  reseed(__VU, __ITER);
  runJourney(SMOKE_ORDER[__ITER % SMOKE_ORDER.length], context.catalog);
}

export function closedUser(context) {
  mixed(context);
  thinkBetweenJourneys();
}

function metricLine(name, metric) {
  if (!metric) {
    return `${name}: -`;
  }
  const v = metric.values;
  return v['p(95)'] !== undefined
    ? `${name}: p95 ${v['p(95)'].toFixed(1)} ms, p99 ${v['p(99)'].toFixed(1)} ms, n=${v.count}`
    : `${name}: ${JSON.stringify(v)}`;
}

export function handleSummary(summary) {
  const m = summary.metrics;
  const counts = Object.entries(m)
    .filter(([key]) => key.startsWith('journeys{') || key.startsWith('booking_outcomes{') || key.startsWith('business_rejections{'))
    .map(([key, metric]) => `  ${key}: ${metric.values.count}`);
  const text = [
    `k6 ${PROFILE} (${RUN_ID})`,
    metricLine('http_req_duration', m.http_req_duration),
    `http_req_failed: ${m.http_req_failed ? (m.http_req_failed.values.rate * 100).toFixed(2) : '-'} %`,
    `journeys: ${journeys && m.journeys ? m.journeys.values.count : 0}, đặt sân: ${bookingOutcomes && m.booking_outcomes ? m.booking_outcomes.values.count : 0}`,
    ...counts,
  ].join('\n');
  return { stdout: `${text}\n`, [`/results/${RUN_ID}/summary.json`]: JSON.stringify(summary, null, 2) };
}
