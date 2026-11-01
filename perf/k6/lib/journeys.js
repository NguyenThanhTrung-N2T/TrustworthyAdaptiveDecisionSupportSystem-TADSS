// Bảy hành trình người dùng của mô hình tải (plan 6C Phụ lục B.6.5, task 2.15) trên dữ liệu benchmark của perf/stack/datagen.py.
//   J1 xem chi nhánh, sân, lịch trống (khách chưa đăng nhập)   J2 đăng nhập
//   J3 đặt sân online ⇒ J4 trả cọc (IPN do k6 ký) / hủy / bỏ dở  J5 xem lịch sử, chi tiết đơn
//   J6 nhân viên: danh sách, lịch, tổng quan, check-in/check-out  J7 quản lý: hàng chờ duyệt, duyệt cọc
// Tỷ lệ hành trình (người dùng chốt Q4, 31/10): J1 55 %, J5 15 %, J3+J4 15 %, J2 5 %, J6 7 %, J7 3 %.
// Ngẫu nhiên có seed theo (SEED, VU, lượt): cùng seed ⇒ cùng chuỗi lựa chọn của từng VU, không phụ thuộc tốc độ máy.
import { sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { data, get, post, queryParam, signedIpnQuery, tagWithJourney } from './client.js';

export const JOURNEY_WEIGHTS = { J1: 55, J5: 15, J3: 15, J2: 5, J6: 7, J7: 3 };
const CUSTOMERS = Number(__ENV.CUSTOMERS || 2000);
const BRANCHES = Number(__ENV.BRANCHES || 3);
const STAFF_PER_BRANCH = Number(__ENV.STAFF_PER_BRANCH || 3);
const SEED = Number(__ENV.SEED || 42);
const THINK_SCALE = Number(__ENV.THINK_SCALE || 1);   // 1 = think time thật; lượt smoke dùng 0.05
const PASSWORD = 'Perf@12345';                         // mật khẩu thử chung của dữ liệu perf (datagen.PERF_PASSWORD)
const DOMAIN = 'smashcourt.perf';
const OPEN_HOUR = 6;
const CLOSE_HOUR = 22;
const TOKEN_MAX_AGE_MS = 13 * 60 * 1000;               // access token sống 15 phút: lấy lại trước khi hết

export const journeys = new Counter('journeys');
export const bookingOutcomes = new Counter('booking_outcomes');

// ── ngẫu nhiên có seed (mulberry32) ───────────────────────────────────────
function mulberry32(a) {
  return function next() {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
let rng = mulberry32(SEED);
export function reseed(vu, iteration) {
  rng = mulberry32((SEED * 1000003) ^ (vu * 7919) ^ (iteration * 104729));
}
const between = (min, max) => min + rng() * (max - min);
const pickInt = (min, max) => Math.floor(between(min, max + 1));
const pick = (items) => items[Math.floor(rng() * items.length)];
export function pickWeighted(weights) {
  const total = Object.values(weights).reduce((a, b) => a + b, 0);
  let roll = rng() * total;
  for (const [key, weight] of Object.entries(weights)) {
    roll -= weight;
    if (roll < 0) {
      return key;
    }
  }
  return Object.keys(weights)[0];
}

// Think time (B.6.5): xem trang 2–6 s, điền form 8–20 s, giữa hai hành trình 5–15 s
export const thinkPage = () => sleep(between(2, 6) * THINK_SCALE);
const thinkForm = () => sleep(between(8, 20) * THINK_SCALE);
export const thinkBetweenJourneys = () => sleep(between(5, 15) * THINK_SCALE);

// ── ngày giờ Việt Nam ─────────────────────────────────────────────────────
function vnDate(offsetDays) {
  return new Date(Date.now() + 7 * 3600 * 1000 + offsetDays * 86400 * 1000).toISOString().slice(0, 10);
}
const vnHourNow = () => new Date(Date.now() + 7 * 3600 * 1000).getUTCHours();
const hh = (hour) => `${String(hour).padStart(2, '0')}:00:00`;
// Thời gian đặt trước (B.6.4, theo distribution.placeholder.json cho tới khi có file thống kê thật)
const LEAD_DAYS = { 0: 10, 1: 25, 2: 20, 3: 15, 5: 12, 7: 10, 10: 5, 14: 3 };
const DURATION_HOURS = { 1: 55, 2: 40, 3: 5 };

// ── phiên đăng nhập của VU (mỗi VU một khách, một nhân viên, một quản lý cố định) ──
const sessions = {};
function account(role) {
  const branch = ((__VU - 1) % BRANCHES) + 1;
  if (role === 'customer') {
    return `perf-customer-${String(((__VU - 1) % CUSTOMERS) + 1).padStart(4, '0')}@${DOMAIN}`;
  }
  if (role === 'staff') {
    return `perf-staff-${branch}-${(Math.floor((__VU - 1) / BRANCHES) % STAFF_PER_BRANCH) + 1}@${DOMAIN}`;
  }
  return `perf-manager-${branch}@${DOMAIN}`;
}
function login(role) {
  const res = post('/api/auth/login', { email: account(role), password: PASSWORD }, 'POST /api/auth/login', 'login');
  const body = data(res);
  if (body && body.accessToken) {
    sessions[role] = { token: body.accessToken, at: Date.now() };
  }
  return sessions[role] ? sessions[role].token : null;
}
function token(role) {
  const session = sessions[role];
  return session && Date.now() - session.at < TOKEN_MAX_AGE_MS ? session.token : login(role);
}

// ── dữ liệu nền đọc một lần (setup) ───────────────────────────────────────
export function loadCatalog() {
  tagWithJourney('setup');
  const branches = data(get('/api/branches/basic?page=1&pageSize=50', 'GET /api/branches/basic', 'read')).items;
  return branches.map((b) => ({
    id: b.id,
    courts: data(get(`/api/courts?branchId=${b.id}`, 'GET /api/courts', 'read')).map((c) => c.id),
  }));
}

// J1: chi nhánh ⇒ sân ⇒ lịch trống của một sân một ngày; trả lựa chọn để J3 dùng tiếp
function browse(catalog) {
  get('/api/branches/basic?page=1&pageSize=50', 'GET /api/branches/basic', 'read');
  thinkPage();
  const branch = pick(catalog);
  get(`/api/courts?branchId=${branch.id}`, 'GET /api/courts', 'read');
  thinkPage();
  const lead = Number(pickWeighted(LEAD_DAYS));
  const date = vnDate(lead);
  const court = pick(branch.courts);
  const grid = data(get(`/api/branches/${branch.id}/courts/${court}/time-grid?date=${date}`, 'GET time-grid', 'read')) || [];
  const earliest = lead === 0 ? Math.max(OPEN_HOUR, vnHourNow() + 1) : OPEN_HOUR;
  const free = grid.filter((s) => s.status === 'AVAILABLE').map((s) => Number(s.startTime.slice(0, 2)))
    .filter((h) => h >= earliest && h < CLOSE_HOUR);
  return { branch, court, date, free };
}

export function j1(catalog) {
  tagWithJourney('J1');
  browse(catalog);
  journeys.add(1, { journey: 'J1' });
}

export function j2() {
  tagWithJourney('J2');
  login('customer');
  journeys.add(1, { journey: 'J2' });
}

// J3 + J4: xem lịch ⇒ điền form ⇒ đặt ⇒ trả cọc 80 % / hủy 10 % / bỏ dở 10 % (job hết hạn thanh toán xử lý)
export function j3(catalog) {
  tagWithJourney('J3');
  const auth = token('customer');
  const choice = browse(catalog);
  if (!auth || choice.free.length === 0) {
    journeys.add(1, { journey: 'J3', result: 'no_slot' });
    return;
  }
  // Như người dùng nhìn lịch: chọn giờ bắt đầu rồi rút ngắn thời lượng tới khi cả quãng còn trống
  const start = pick(choice.free);
  let hours = Math.min(Number(pickWeighted(DURATION_HOURS)), CLOSE_HOUR - start);
  while (hours > 1 && !Array.from({ length: hours }, (_, i) => start + i).every((h) => choice.free.includes(h))) {
    hours -= 1;
  }
  thinkForm();
  const res = post('/api/bookings/online', {
    bookingDate: choice.date,
    courts: [{ courtId: choice.court, startTime: hh(start), endTime: hh(start + hours) }],
  }, 'POST /api/bookings/online', 'booking', auth, [400, 409]);
  const booking = data(res, [400, 409]);
  if (!booking) {
    bookingOutcomes.add(1, { outcome: `http_${res.status}` });
    journeys.add(1, { journey: 'J3', result: 'rejected' });
    return;
  }
  bookingOutcomes.add(1, { outcome: booking.status });
  journeys.add(1, { journey: 'J3', result: booking.status });
  if (booking.status === 'PENDING' && booking.paymentUrl) {
    j4(auth, booking);
  }
}

function j4(auth, booking) {
  tagWithJourney('J4');
  thinkPage();   // khách ở trang VNPay
  const roll = rng();
  if (roll < 0.8) {
    const query = signedIpnQuery(queryParam(booking.paymentUrl, 'vnp_TxnRef'), queryParam(booking.paymentUrl, 'vnp_Amount'), true,
      14000000 + pickInt(1, 999999));
    get(`/api/payments/vnpay/ipn?${query}`, 'GET /api/payments/vnpay/ipn', 'ipn');
    journeys.add(1, { journey: 'J4', result: 'paid' });
  } else if (roll < 0.9) {
    post(`/api/me/bookings/${booking.bookingId}/cancel`, null, 'POST /api/me/bookings/{id}/cancel', 'write', auth, [400, 409]);
    journeys.add(1, { journey: 'J4', result: 'cancelled' });
  } else {
    journeys.add(1, { journey: 'J4', result: 'abandoned' });
  }
}

export function j5() {
  tagWithJourney('J5');
  const auth = token('customer');
  const list = auth ? data(get('/api/me/bookings?page=1&pageSize=10', 'GET /api/me/bookings', 'read', auth)) : null;
  thinkPage();
  const items = (list && list.items) || [];
  if (items.length > 0) {
    get(`/api/me/bookings/${pick(items).id}`, 'GET /api/me/bookings/{id}', 'read', auth);
  }
  journeys.add(1, { journey: 'J5' });
}

// J6: nhân viên một chi nhánh trong ngày; check-in đơn đã xác nhận / check-out đơn đang chơi (4xx nghiệp vụ là bình thường)
export function j6(catalog) {
  tagWithJourney('J6');
  const auth = token('staff');
  if (!auth) {
    return;
  }
  const branch = catalog[(__VU - 1) % catalog.length];
  const today = vnDate(0);
  const list = data(get(`/api/bookings?branchId=${branch.id}&date=${today}&page=1&pageSize=20`, 'GET /api/bookings', 'read', auth));
  get(`/api/bookings/schedule?branchId=${branch.id}&date=${today}`, 'GET /api/bookings/schedule', 'read', auth);
  get(`/api/bookings/dashboard-summary?branchId=${branch.id}`, 'GET /api/bookings/dashboard-summary', 'read', auth);
  thinkPage();
  const items = (list && list.items) || [];
  const toCheckIn = items.find((b) => b.status === 'CONFIRMED' || b.status === 'PAID_ONLINE');
  const toCheckOut = items.find((b) => b.status === 'IN_PROGRESS');
  if (toCheckIn && rng() < 0.5) {
    post(`/api/bookings/${toCheckIn.id}/check-in`, null, 'POST /api/bookings/{id}/check-in', 'write', auth, [400, 409]);
  } else if (toCheckOut) {
    post(`/api/bookings/${toCheckOut.id}/checkout`, null, 'POST /api/bookings/{id}/checkout', 'write', auth, [400, 409]);
  }
  journeys.add(1, { journey: 'J6' });
}

// J7: quản lý xem hàng chờ; có đơn chờ duyệt (strategy trả NEED_APPROVAL) thì duyệt cọc 30 %
export function j7(catalog) {
  tagWithJourney('J7');
  const auth = token('manager');
  if (!auth) {
    return;
  }
  const branch = catalog[(__VU - 1) % catalog.length];
  const queue = data(get(`/api/bookings/pending-approval?branchId=${branch.id}&page=1&pageSize=10`, 'GET /api/bookings/pending-approval',
    'read', auth));
  const items = (queue && queue.items) || [];
  thinkPage();
  if (items.length > 0) {
    post(`/api/bookings/${items[0].bookingId || items[0].id}/approve`, { depositRate: 0.3, reason: 'Duyệt trong lượt đo perf' },
      'POST /api/bookings/{id}/approve', 'write', auth, [400, 409]);
    journeys.add(1, { journey: 'J7', result: 'approved' });
  } else {
    journeys.add(1, { journey: 'J7', result: 'empty' });
  }
}

export function runJourney(name, catalog) {
  switch (name) {
    case 'J1': return j1(catalog);
    case 'J2': return j2();
    case 'J3': return j3(catalog);
    case 'J5': return j5();
    case 'J6': return j6(catalog);
    default: return j7(catalog);
  }
}
