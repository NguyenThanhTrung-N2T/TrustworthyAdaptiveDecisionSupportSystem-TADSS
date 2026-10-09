// Dựng `options` của kịch bản từ config/modes.json theo MODE (O-15). Mỗi kịch bản gồm scenario `warmup` (tag phase=warmup) rồi `main` (phase=main) nối tiếp.
const MODES = JSON.parse(open('../config/modes.json'));

function seconds(duration) {
  const match = /^(\d+)(s|m|h)$/.exec(duration);
  if (!match) {
    throw new Error(`Thời lượng không hợp lệ: ${duration}`);
  }
  return Number(match[1]) * { s: 1, m: 60, h: 3600 }[match[2]];
}

export function loadProfile(scenarioKey) {
  const mode = (__ENV.MODE || 'quick').toLowerCase();
  const profile = (MODES[mode] || {})[scenarioKey];
  if (!profile) {
    throw new Error(`Chế độ '${mode}' không có kịch bản '${scenarioKey}'`);
  }
  return { mode, profile };
}

function arrivalRate(profile, startTime, rate, duration, phase) {
  return {
    executor: 'constant-arrival-rate',
    rate,
    timeUnit: '1s',
    duration,
    preAllocatedVUs: profile.preAllocatedVUs,
    maxVUs: profile.maxVUs,
    startTime,
    tags: { phase },
  };
}

// Tổng thời lượng (giây) của kịch bản: warm-up + phần chính
export function totalSeconds(scenarioKey) {
  const { profile } = loadProfile(scenarioKey);
  if (profile.executor === 'ramping-arrival-rate') {
    return seconds(profile.warmup) + profile.steps.length * (seconds(profile.hold) + 1);
  }
  return seconds(profile.warmup) + seconds(profile.duration);
}

export function buildOptions(scenarioKey) {
  const { profile } = loadProfile(scenarioKey);
  const warmup = profile.warmup;
  const startMain = `${seconds(warmup)}s`;
  const scenarios = {};

  if (profile.executor === 'constant-arrival-rate') {
    if (seconds(warmup) > 0) {
      scenarios.warmup = arrivalRate(profile, '0s', profile.rate, warmup, 'warmup');
    }
    scenarios.main = arrivalRate(profile, startMain, profile.rate, profile.duration, 'main');
  } else {
    // Bậc thang: mỗi bậc chuyển 1 giây rồi giữ `hold`; evaluator suy ra cửa sổ từng bậc từ cùng cấu hình
    const stages = profile.steps.flatMap((rate) => [{ target: rate, duration: '1s' }, { target: rate, duration: profile.hold }]);
    scenarios.warmup = arrivalRate(profile, '0s', profile.startRate, warmup, 'warmup');
    scenarios.main = {
      executor: 'ramping-arrival-rate',
      startRate: profile.startRate,
      timeUnit: '1s',
      stages,
      preAllocatedVUs: profile.preAllocatedVUs,
      maxVUs: profile.maxVUs,
      startTime: startMain,
      tags: { phase: 'main' },
    };
  }

  return {
    scenarios,
    // Không dừng lượt vì ngưỡng: kết luận đạt/không đạt do evaluator tính từ D2; tắt thu thập tag URL theo từng request để tránh bùng nổ nhãn
    discardResponseBodies: false,
    systemTags: ['status', 'method', 'name', 'scenario'],
    summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  };
}
