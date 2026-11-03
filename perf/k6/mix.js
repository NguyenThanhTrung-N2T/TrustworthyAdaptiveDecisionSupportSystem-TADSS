// Kịch bản tải hỗn hợp của môi trường perf (Epic 6C, task 2.15, 2.18; plan 6C Phụ lục B.7). Chạy bằng `bash perf/perf.sh run <PROFILE>`.
//   smoke       closed, ít VU, mỗi VU đi vòng đủ J1…J7: kiểm đường đi, không đo
//   load        open, RATE hành trình/giây trong DURATION (SP-01)
//   stress      open, 100 ⇒ 150 ⇒ 200 ⇒ 250 % RATE, mỗi bậc chuyển 10 s + giữ STEP, rồi về RATE 5 phút (SP-02)
//   spike       open, RATE 3 phút ⇒ ×5 giữ 1 phút ⇒ RATE 5 phút (SP-03, đo thời gian hồi phục)
//   soak        open, RATE trong DURATION dài (SP-04, 90 phút)
//   breakpoint  open, theo bậc từ 50 % RATE, mỗi bậc +25 % RATE tới 300 % (SP-05; START_RATE, STEP_RATE, MAX_RATE đổi được)
//   closed      closed (ramping-vus) VUS người dùng có think time giữa hai hành trình: góc nhìn trải nghiệm
// Mỗi profile (trừ smoke) là MỘT scenario: warm-up WARMUP tăng 25 % ⇒ 75 % RATE (phase=warmup) ⇒ chuyển 10 s + 1 phút ở mức đầu của pha đo
// (phase=settle) ⇒ phase=steady. Nhãn pha gắn theo thời điểm gửi request; chỉ steady vào threshold (B.7.2, B.7.3). Một scenario thay vì ba
// để k6 không cấp VU cho từng scenario chồng nhau lúc chuyển pha. Mọi ngưỡng/cửa sổ/bậc đọc từ slo.json (cùng file verdict.py chấm).
// Mô hình open đo theo nhịp hành trình đến, không phụ thuộc hệ thống chậm (tránh coordinated omission của closed model).
import exec from 'k6/execution';
import { SLO, usePhaseClock } from './lib/client.js';
import { JOURNEY_WEIGHTS, bookingOutcomes, journeys, loadCatalog, loginPool, pickWeighted, reseed, runJourney, thinkBetweenJourneys, usePool }
  from './lib/journeys.js';

const PROFILE = __ENV.PROFILE || 'smoke';
const RATE = Number(__ENV.RATE || 5);                 // hành trình/giây ở mức danh nghĩa
const DURATION = __ENV.DURATION || '15m';
const WARMUP = __ENV.WARMUP || '2m';
const SETTLE = __ENV.SETTLE || SLO.settle;
const TRANSITION = SLO.transition;
const SELFTEST = __ENV.SLO_SELFTEST === '1';
const JOURNEY_SECONDS = Number(__ENV.JOURNEY_SECONDS || SLO.journeySeconds);
const RUN_ID = __ENV.RUN_ID || `local-${Date.now()}`;
const SMOKE_ORDER = ['J1', 'J2', 'J3', 'J5', 'J6', 'J7'];
const GROUPS = Object.keys(SLO.groups);

const UNIT = { h: 3600, m: 60, s: 1 };
const seconds = (text) => [...text.matchAll(/(\d+)([hms])/g)].reduce((sum, [, n, u]) => sum + Number(n) * UNIT[u], 0);
const rate = (value) => Math.max(1, Math.round(value));

// Pha đo của từng profile open: các stage sau settle, tốc độ đầu/đỉnh, bậc tải (để verdict.py cắt cửa sổ)
function steadyPlan() {
  const hold = (step) => [{ target: rate(step.rate), duration: TRANSITION }, { target: rate(step.rate), duration: `${step.hold}s` }];
  const withSteps = (steps) => ({ first: steps[0].rate, peak: Math.max(...steps.map((s) => s.rate)), steps, stages: steps.flatMap(hold) });
  switch (PROFILE) {
    case 'load':
    case 'soak':
      return { first: RATE, peak: RATE, steps: [], stages: [{ target: rate(RATE), duration: DURATION }] };
    case 'stress': {
      const step = seconds(__ENV.STEP || SLO.stress.hold);
      return withSteps([...SLO.stress.factors.map((k) => ({ rate: rate(RATE * k), transition: seconds(TRANSITION), hold: step })),
        { rate: rate(RATE), transition: seconds(TRANSITION), hold: seconds(SLO.stress.recovery), recovery: true }]);
    }
    case 'spike': {
      const s = SLO.spike;
      const stages = [{ target: rate(RATE), duration: s.before }, { target: rate(RATE * s.factor), duration: TRANSITION },
        { target: rate(RATE * s.factor), duration: s.hold }, { target: rate(RATE), duration: TRANSITION }, { target: rate(RATE), duration: s.after }];
      const recoveryOffset = seconds(s.before) + 2 * seconds(TRANSITION) + seconds(s.hold);
      return { first: RATE, peak: RATE * s.factor, steps: [], stages, recoveryOffset, recoverySeconds: seconds(s.after) };
    }
    case 'breakpoint': {
      const b = SLO.breakpoint;
      const start = Number(__ENV.START_RATE || RATE * b.startFactor);
      const step = Number(__ENV.STEP_RATE || RATE * b.stepFactor);
      const max = Number(__ENV.MAX_RATE || RATE * b.maxFactor);
      const steps = [];
      for (let r = start; r <= max + 1e-9; r += step) {
        steps.push({ rate: rate(r), transition: seconds(TRANSITION), hold: seconds(__ENV.STEP || b.hold) });
      }
      return withSteps(steps);
    }
    default:
      return null;
  }
}

const PLAN = steadyPlan();
const PEAK = PLAN ? PLAN.peak : RATE;
// Trần VU (B.7.2): 1,5 × tốc độ đỉnh × thời lượng hành trình, cấp sẵn TOÀN BỘ: thời lượng hành trình dao động (J3 tới 30 s) nên lúc cao điểm cần
// hơn mức trung bình; VU khởi tạo giữa lượt làm k6 bỏ lượt dù hệ thống vẫn khỏe (lượt thử 02/11: bậc 10/s bỏ 1,17 % khi chỉ cấp sẵn 1 ×)
const MAX_VUS = Number(__ENV.MAX_VUS || Math.ceil(SLO.vuHeadroom * PEAK * JOURNEY_SECONDS));
const PRE_VUS = MAX_VUS;
const WARMUP_END = seconds(WARMUP);
const SETTLE_END = PROFILE === 'closed' ? WARMUP_END + seconds(SETTLE) : WARMUP_END + seconds(TRANSITION) + seconds(SETTLE);

function scenarios() {
  switch (PROFILE) {
    case 'smoke':
      // Warm-up 30 s: ngay sau reset BE/TADSS nguội, lời gọi TADSS đầu tiên quá TimeoutMs 800 ms ⇒ đường lùi / 503 khi duyệt (lượt 02/11)
      return {
        warmup: { executor: 'constant-arrival-rate', rate: 2, timeUnit: '1s', duration: __ENV.WARMUP || '30s', preAllocatedVUs: 20,
          maxVUs: 40, exec: 'mixed', tags: { phase: 'warmup' } },
        smoke: { executor: 'per-vu-iterations', vus: Number(__ENV.VUS || 6), iterations: SMOKE_ORDER.length * 2, exec: 'roundRobin',
          maxDuration: '10m', startTime: __ENV.WARMUP || '30s', tags: { phase: 'steady' } },
      };
    case 'closed': {
      const vus = Number(__ENV.VUS || 50);
      return { closed: { executor: 'ramping-vus', startVUs: 0, exec: 'closedUser', stages: [{ target: vus, duration: WARMUP },
        { target: vus, duration: SETTLE }, { target: vus, duration: DURATION }] } };
    }
    default:
      if (!PLAN) {
        throw new Error(`PROFILE không hợp lệ: ${PROFILE}`);
      }
      return { open: { executor: 'ramping-arrival-rate', timeUnit: '1s', startRate: rate(RATE * 0.25), preAllocatedVUs: PRE_VUS,
        maxVUs: MAX_VUS, exec: 'mixed', stages: [{ target: rate(RATE * 0.75), duration: WARMUP },
          { target: rate(PLAN.first), duration: TRANSITION }, { target: rate(PLAN.first), duration: SETTLE }, ...PLAN.stages] } };
  }
}

if (PROFILE !== 'smoke') {
  usePhaseClock(() => {
    if (__VU === 0) {
      return 'setup';   // setup() (danh mục, đăng nhập sẵn) chạy trước mọi scenario: không có exec.scenario, không vào thống kê
    }
    const t = (Date.now() - exec.scenario.startTime) / 1000;
    return t < WARMUP_END ? 'warmup' : t < SETTLE_END ? 'settle' : 'steady';
  });
}

function thresholds() {
  const sloRun = SLO.sloProfiles.includes(PROFILE);
  const limit = (group) => (SELFTEST && group === 'read' ? 1 : SLO.groups[group].p95);
  return {
    // Lượt dừng giữa chừng hoặc hỏng đường đi không được ra "đạt" (B.7.2); stress/spike/breakpoint vượt là dự kiến, chấm ở verdict.py
    'request_errors{phase:steady}': [SLO.errorProfiles.includes(PROFILE) ? `rate<${SLO.errorRateMax}` : 'rate>=0'],
    ...Object.fromEntries(GROUPS.map((g) => [`http_req_duration{phase:steady,slo:${g}}`,
      sloRun ? [`p(95)<${limit(g)}`, `p(99)<${SLO.groups[g].p99}`] : ['p(95)>=0']])),
    ...Object.fromEntries(GROUPS.map((g) => [`request_errors{phase:steady,slo:${g}}`, ['rate>=0']])),
    // Ngưỡng luôn đạt chỉ để summary có số đếm theo từng hành trình / kết quả đặt sân (tỷ lệ hành trình ≠ tỷ lệ request, B.6.5)
    ...Object.fromEntries(['J1', 'J2', 'J3', 'J4', 'J5', 'J6', 'J7'].map((j) => [`journeys{journey:${j}}`, ['count>=0']])),
    ...Object.fromEntries(['PENDING', 'CONFIRMED', 'PENDING_APPROVAL', 'http_409', 'http_400'].map((o) => [`booking_outcomes{outcome:${o}}`, ['count>=0']])),
  };
}

export const options = {
  scenarios: scenarios(),
  // Mẫu endpoint có id đã gom bằng tag name; không lưu nhãn url thô để Prometheus không bùng nhãn
  systemTags: ['status', 'method', 'name', 'group', 'check', 'error', 'error_code', 'scenario', 'expected_response'],
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
  setupTimeout: '5m',
  thresholds: thresholds(),
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

// Lịch tải thực dùng (giây, tính từ đầu pha steady) cho verdict.py: bậc tải, điểm bắt đầu hồi phục của spike, độ dài pha steady
function schedule() {
  const steadySeconds = PROFILE === 'closed' ? seconds(DURATION)
    : PLAN ? PLAN.stages.reduce((sum, s) => sum + seconds(s.duration), 0) : null;
  return {
    runId: RUN_ID, profile: PROFILE, rate: RATE, selftest: SELFTEST, warmupSeconds: WARMUP_END, settleEndSeconds: SETTLE_END,
    steadySeconds, peakRate: PEAK, maxVUs: MAX_VUS, preAllocatedVUs: PRE_VUS, journeySeconds: JOURNEY_SECONDS,
    steps: PLAN ? PLAN.steps : [], recoveryOffset: PLAN ? PLAN.recoveryOffset || null : null,
    recoverySeconds: PLAN ? PLAN.recoverySeconds || null : null,
  };
}

export function handleSummary(summary) {
  const m = summary.metrics;
  const counts = Object.entries(m)
    .filter(([key]) => key.startsWith('journeys{') || key.startsWith('booking_outcomes{') || key.startsWith('business_rejections{'))
    .map(([key, metric]) => `  ${key}: ${metric.values.count}`);
  const text = [
    `k6 ${PROFILE} (${RUN_ID})${SELFTEST ? ' [TỰ KIỂM: ngưỡng p95 Đọc 1 ms]' : ''}`,
    ...GROUPS.map((g) => metricLine(`  ${g}`, m[`http_req_duration{phase:steady,slo:${g}}`])),
    `request_errors (steady): ${m['request_errors{phase:steady}'] ? (m['request_errors{phase:steady}'].values.rate * 100).toFixed(2) : '-'} %`,
    `journeys: ${journeys && m.journeys ? m.journeys.values.count : 0}, đặt sân: ${bookingOutcomes && m.booking_outcomes ? m.booking_outcomes.values.count : 0}`,
    ...counts,
  ].join('\n');
  return {
    stdout: `${text}\n`,
    [`/results/${RUN_ID}/summary.json`]: JSON.stringify(summary, null, 2),
    [`/results/${RUN_ID}/schedule.json`]: JSON.stringify(schedule(), null, 2),
  };
}
