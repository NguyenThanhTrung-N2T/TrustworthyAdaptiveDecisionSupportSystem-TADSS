// LT-04 (methodology 4.6, plan 6A-6 Task 14, P6-30): khách đặt sân online rồi hủy ngay, chạy trên E-STAGING (không thuộc evaluator).
// Đặt rồi hủy để chạy lâu mà không làm sai dữ liệu: đơn không bao giờ hết hạn (không tăng paymentExpiredCount), sân được nhả để lượt sau
// đặt lại, không ngập hàng chờ duyệt. Mỗi VU một tài khoản và một tập (sân, ngày, giờ) không giao với VU khác ở các ngày D+20…D+26.
//
// Biến môi trường:
//   BE_URL (mặc định http://localhost:8080), MAILPIT_URL (mặc định http://localhost:8025), LT04_BRANCH_ID (bắt buộc)
//   LT04_PROFILE = smoke (1 VU × 3 lượt) | full (20 VU, 11 phút, phút đầu là warm-up loại khỏi thống kê)
//   LT04_EXPECT  = fallback (cờ tắt: cọc 100 %) | tadss (bắt buộc LT04_EXPECTED_RATE, LT04_STRATEGY, LT04_EXPECTED_RATE_SOURCE)
//   LT04_ACCOUNTS = "email:password,…" (tài khoản có sẵn); vắng thì đăng ký lt04-{runId}-{i}@lt04.test qua Mailpit
//   LT04_RUN_ID (mặc định thời điểm chạy), SUMMARY_PATH (file summary JSON, lib/output.js)
//   LT04_DRY_RESPONSE = file JSON mẫu: chỉ chạy logic kiểm mức cọc trên dữ liệu giả, không gọi BE
import { check, fail, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter, Rate, Trend } from 'k6/metrics';
import * as be from '../lib/smashcourt.js';
import { checkDeposit, checkPaymentLink, expectationFromEnv } from '../lib/lt04_checks.js';
import { summaryOutput } from '../lib/output.js';

const PROFILE = (__ENV.LT04_PROFILE || 'smoke').toLowerCase();
const DRY = __ENV.LT04_DRY_RESPONSE ? JSON.parse(open(__ENV.LT04_DRY_RESPONSE)) : null;
const BASE = (__ENV.BE_URL || 'http://localhost:8080').replace(/\/$/, '');
const MAILPIT = (__ENV.MAILPIT_URL || 'http://localhost:8025').replace(/\/$/, '');
const WARMUP_SECONDS = 60;
const FIRST_DAY = 20; // D+20…D+26: xa mọi đơn thật, trong khoảng đặt trước cho phép
const DAYS = 7;
const HOURS = [8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]; // slot 60 phút, giá dưới ngưỡng duyệt

const PROFILES = {
  smoke: {
    vus: 1,
    scenarios: { lt04: { executor: 'per-vu-iterations', vus: 1, iterations: 3, maxDuration: '5m' } },
    thresholds: { checks: ['rate==1.0'] },
  },
  full: {
    vus: 20,
    // Một scenario (VU không đổi giữa warm-up và phần chính); gracefulStop để lượt đang chạy kịp hủy đơn, không để lại đơn PENDING
    scenarios: { lt04: { executor: 'constant-vus', vus: 20, duration: '11m', gracefulStop: '30s' } },
    thresholds: { checks: ['rate>0.99'] },
  },
};

const profile = PROFILES[PROFILE];
if (!profile) {
  throw new Error(`LT04_PROFILE phải là smoke hoặc full, không phải '${__ENV.LT04_PROFILE}'`);
}

export const options = DRY
  ? { scenarios: { dry: { executor: 'per-vu-iterations', vus: 1, iterations: 1 } }, thresholds: { checks: ['rate==1.0'] } }
  : { scenarios: profile.scenarios, thresholds: profile.thresholds, setupTimeout: '15m' };

const createBooking = new Trend('lt04_create_booking_ms', true);
const depositOk = new Rate('lt04_deposit_ok');
const pendingApproval = new Counter('lt04_pending_approval');
const rateLimited = new Counter('lt04_rate_limited');
const cancelFailed = new Counter('lt04_cancel_failed');

// Ngày theo giờ VN (yyyy-MM-dd) sau `days` ngày
function vietnamDate(days) {
  return new Date(Date.now() + 7 * 3600 * 1000 + days * 86400 * 1000).toISOString().slice(0, 10);
}

function pad(hour) {
  return `${hour < 10 ? '0' : ''}${hour}:00:00`;
}

// Chia mọi (sân, ngày, giờ) cho các tài khoản theo vòng: hai VU không bao giờ tranh cùng slot; một VU dùng lại slot của mình vì đơn đã hủy
function buildSlots(courts, accounts) {
  const perAccount = Array.from({ length: accounts }, () => []);
  let index = 0;
  for (let d = FIRST_DAY; d < FIRST_DAY + DAYS; d++) {
    const date = vietnamDate(d);
    for (const hour of HOURS) {
      for (const courtId of courts) {
        perAccount[index % accounts].push({ courtId, date, start: pad(hour), end: pad(hour + 1) });
        index++;
      }
    }
  }
  return perAccount;
}

function readAccounts(runId, count) {
  if (__ENV.LT04_ACCOUNTS) {
    const listed = __ENV.LT04_ACCOUNTS.split(',').map((pair) => {
      const at = pair.indexOf(':');
      return { email: pair.slice(0, at).trim(), password: pair.slice(at + 1) };
    });
    if (listed.length < count) {
      fail(`LT04_ACCOUNTS có ${listed.length} tài khoản, hồ sơ ${PROFILE} cần ${count}`);
    }
    return listed.slice(0, count).map((a) => ({ ...a, token: be.login(BASE, a.email, a.password) }));
  }
  const password = `Lt04!${runId}a`; // đủ luật RegisterDto: chữ hoa, số, ký tự đặc biệt, ≥ 8
  return Array.from({ length: count }, (_, i) => {
    const email = `lt04-${runId}-${i}@lt04.test`;
    return { email, password, token: be.registerAndLogin(BASE, MAILPIT, email, password, `LT04 ${i}`) };
  });
}

export function setup() {
  if (DRY) {
    return { dry: true };
  }
  const expectation = expectationFromEnv(__ENV);
  const branchId = __ENV.LT04_BRANCH_ID || fail('Thiếu LT04_BRANCH_ID');
  const runId = __ENV.LT04_RUN_ID || String(Date.now());
  const courts = be.listCourts(BASE, branchId);
  if (courts.length === 0) {
    fail(`Chi nhánh ${branchId} không có sân đặt được`);
  }
  const accounts = readAccounts(runId, profile.vus);
  return { runId, expectation, accounts, slots: buildSlots(courts, accounts.length), startedAt: Date.now() };
}

// Chế độ dữ liệu giả: chạy logic kiểm mức cọc trên các ca mẫu, mỗi ca phải cho đúng kết luận kỳ vọng (cả ca đúng lẫn ca sai)
function dryRun() {
  for (const sample of DRY.cases) {
    let verdict;
    try {
      verdict = checkDeposit(expectationFromEnv(sample.env), sample.booking).ok ? 'ok' : 'sai';
    } catch (error) {
      verdict = 'dừng';
    }
    check(verdict, { [`dry: ${sample.name} ⇒ ${sample.expect}`]: (v) => v === sample.expect });
  }
}

let token = null;
let next = 0;

export default function (data) {
  if (data.dry) {
    dryRun();
    return;
  }
  const k = (exec.vu.idInTest - 1) % data.accounts.length;
  const account = data.accounts[k];
  token = token || account.token;
  const slot = data.slots[k][next++ % data.slots[k].length];
  const phase = PROFILE === 'full' && (Date.now() - data.startedAt) / 1000 < WARMUP_SECONDS ? 'warmup' : 'main';
  const note = `LT04 ${data.runId}`;

  let res = be.createOnlineBooking(BASE, token, slot, note, { phase, name: 'create_booking' });
  if (res.status === 401) {
    token = be.login(BASE, account.email, account.password); // token hết hạn (15 phút) trong lượt dài
    res = be.createOnlineBooking(BASE, token, slot, note, { phase, name: 'create_booking' });
  }
  createBooking.add(res.timings.duration, { phase });
  if (res.status === 429) {
    rateLimited.add(1);
  }
  if (!check(res, { 'đặt sân 201': (r) => r.status === 201 })) {
    console.error(`[LT04] đặt ${slot.courtId} ${slot.date} ${slot.start}: HTTP ${res.status} ${res.body}`);
    sleep(2);
    return;
  }

  const booking = res.json('data');
  check(booking, { 'status PENDING | CONFIRMED | PENDING_APPROVAL': (b) => ['PENDING', 'CONFIRMED', 'PENDING_APPROVAL'].includes(b.status) });
  const link = checkPaymentLink(booking);
  if (!check(link, { 'PENDING có link thanh toán có hạn': (l) => l.ok })) {
    console.error(`[LT04] ${booking.bookingCode}: ${link.reason}`);
  }
  if (booking.status === 'PENDING_APPROVAL') {
    pendingApproval.add(1); // slot dưới ngưỡng duyệt nên kỳ vọng 0 ở chế độ tadss
  } else {
    const deposit = checkDeposit(data.expectation, booking);
    depositOk.add(deposit.ok);
    if (!check(deposit, { 'mức cọc đúng kỳ vọng': (d) => d.ok })) {
      console.error(`[LT04] ${booking.bookingCode}: ${deposit.reason}`);
    }
  }

  const cancel = be.cancelMyBooking(BASE, token, booking.bookingId);
  if (!check(cancel, { 'hủy 200': (r) => r.status === 200 })) {
    cancelFailed.add(1);
    console.error(`[LT04] hủy ${booking.bookingCode}: HTTP ${cancel.status} ${cancel.body}`);
  }
  const detail = be.getMyBooking(BASE, token, booking.bookingId);
  check(detail, {
    'đọc lại: CANCELLED, không hoàn tiền': (r) =>
      r.status === 200 && r.json('data.status') === 'CANCELLED' && Number(r.json('data.refundAmount')) === 0,
  });
  sleep(2 + Math.random() * 2);
}

// Summary cùng định dạng lib/output.js, thêm khối lt04 (chế độ, nguồn giá trị kỳ vọng, giả định khách). Bỏ setup_data: chứa token và mật khẩu
export function handleSummary(data) {
  const setupData = data.setup_data || {};
  const expectation = setupData.expectation || {};
  const { setup_data: _secret, ...summary } = data;
  const lt04 = {
    runId: setupData.runId || null,
    profile: DRY ? 'dry' : PROFILE,
    expect: expectation.mode || null,
    strategy: expectation.strategy || null,
    expectedRate: expectation.rate === undefined ? null : expectation.rate,
    expectedRateSource: expectation.rateSource || null,
    customerAssumption: 'hạng STANDARD, tài khoản mới, không lịch sử; đơn đặt rồi hủy ngay, chưa trả tiền',
    accounts: (setupData.accounts || []).length,
  };
  const checks = (data.metrics.checks || {}).values || {};
  const failed = (data.root_group.checks || [])
    .filter((c) => c.fails > 0)
    .map((c) => `  ✗ ${c.name}: ${c.fails}/${c.passes + c.fails}\n`)
    .join('');
  return {
    ...summaryOutput({ ...summary, lt04 }),
    stdout: `[LT04] ${JSON.stringify(lt04)}\n[LT04] checks đạt ${checks.passes || 0}, hỏng ${checks.fails || 0}\n${failed}`,
  };
}
