// Bộ sinh payload cho POST /api/v2/decisions (O-15, Task 8). Bảng trộn khai báo ngay dưới đây: chi nhánh cố định, giờ cao điểm/thấp điểm theo giờ chi nhánh (Asia/Ho_Chi_Minh, +07:00),
// các hạng khách, khách vãng lai, giá dưới/trên ngưỡng giá trị lớn (2.000.000 VND) và 1–3 item. Thời gian chơi LUÔN ở tương lai (7–30 ngày) để cửa sổ duyệt còn tồn tại.
// Payload hợp lệ schema v2 và bất biến domain: lỗi 400 nghĩa là bộ sinh sai.

const BRANCH_ID = '1f2e3d4c-0000-4000-8000-000000000001';
const TIME_ZONE = 'Asia/Ho_Chi_Minh';
const OFFSET_MINUTES = 7 * 60;
const RESOURCE_TYPE_ID = 'c0a8012e-0000-4000-8000-0000000000bb';
const RESOURCES = Array.from({ length: 8 }, (_, i) => `c0a8012e-0000-4000-8000-0000000000${(0xa0 + i).toString(16)}`);
const HIGH_VALUE_THRESHOLD = 2000000;

// Mỗi ô của bảng trộn có trọng số; test pytest kiểm mọi ô xuất hiện trong mẫu đủ lớn.
export const MIX = {
  slot: [
    { name: 'PEAK_WEEKDAY', weight: 30, dayOfWeek: [1, 2, 3, 4, 5], hour: 18 },
    { name: 'OFFPEAK_WEEKDAY', weight: 30, dayOfWeek: [1, 2, 3, 4, 5], hour: 10 },
    { name: 'PEAK_WEEKEND', weight: 25, dayOfWeek: [0, 6], hour: 10 },
    { name: 'OFFPEAK_WEEKEND', weight: 15, dayOfWeek: [0, 6], hour: 6 },
  ],
  customer: [
    { name: 'GUEST', weight: 10, isGuest: true, tier: 'STANDARD' },
    { name: 'STANDARD', weight: 25, isGuest: false, tier: 'STANDARD' },
    { name: 'SILVER', weight: 20, isGuest: false, tier: 'SILVER' },
    { name: 'GOLD', weight: 20, isGuest: false, tier: 'GOLD' },
    { name: 'PLATINUM', weight: 15, isGuest: false, tier: 'PLATINUM' },
    { name: 'DIAMOND', weight: 10, isGuest: false, tier: 'DIAMOND' },
  ],
  price: [
    { name: 'NORMAL', weight: 85, min: 60000, max: 300000 },
    { name: 'HIGH_VALUE', weight: 15, min: 800000, max: 1200000 },
  ],
  items: [
    { name: 'ONE', weight: 60, count: 1 },
    { name: 'TWO', weight: 30, count: 2 },
    { name: 'THREE', weight: 10, count: 3 },
  ],
};

function pick(options) {
  const total = options.reduce((sum, option) => sum + option.weight, 0);
  let roll = Math.random() * total;
  for (const option of options) {
    roll -= option.weight;
    if (roll < 0) {
      return option;
    }
  }
  return options[options.length - 1];
}

function randomInt(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1));
}

export function uuid() {
  const hex = () => randomInt(0, 0xffff).toString(16).padStart(4, '0');
  const variant = (8 + randomInt(0, 3)).toString(16);
  return `${hex()}${hex()}-${hex()}-4${hex().slice(1)}-${variant}${hex().slice(1)}-${hex()}${hex()}${hex()}`;
}

// ISO 8601 với offset +07:00 tường minh (yêu cầu của schema)
export function formatLocal(utcMillis) {
  const local = new Date(utcMillis + OFFSET_MINUTES * 60000);
  return `${local.toISOString().slice(0, 19)}+07:00`;
}

// Ngày (giờ chi nhánh) lùi về phía trước từ `now` 7–30 ngày và rơi vào thứ trong `dayOfWeek`, tại giờ `hour`
function pickStart(now, slot) {
  const base = new Date(now + OFFSET_MINUTES * 60000);
  for (let attempt = 0; attempt < 8; attempt += 1) {
    const day = new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth(), base.getUTCDate() + randomInt(7, 30), slot.hour, 0, 0));
    if (slot.dayOfWeek.includes(day.getUTCDay())) {
      return day.getTime() - OFFSET_MINUTES * 60000;
    }
  }
  // Hiếm khi không trúng thứ mong muốn sau 8 lần thử: dịch tiến tới đúng thứ
  let day = new Date(Date.UTC(base.getUTCFullYear(), base.getUTCMonth(), base.getUTCDate() + 7, slot.hour, 0, 0));
  while (!slot.dayOfWeek.includes(day.getUTCDay())) {
    day = new Date(day.getTime() + 86400000);
  }
  return day.getTime() - OFFSET_MINUTES * 60000;
}

function buildCustomer(profile, now) {
  if (profile.isGuest) {
    return { isGuest: true, tier: profile.tier };
  }
  const completed = randomInt(0, 40);
  return {
    customerId: uuid(),
    isGuest: false,
    tier: profile.tier,
    memberSince: formatLocal(now - randomInt(30, 900) * 86400000),
    history: {
      completedCount: completed,
      noShowCount: randomInt(0, 3),
      cancelledCount: randomInt(0, 5),
      lateCancelledCount: randomInt(0, 2),
      paymentExpiredCount: randomInt(0, 1),
      lastCompletedAt: formatLocal(now - randomInt(1, 120) * 86400000),
    },
  };
}

// Trả { body, cells } — `cells` là tên các ô của bảng trộn mà payload này thuộc về (cho test và thống kê)
export function buildPayload(now = Date.now()) {
  const slot = pick(MIX.slot);
  const profile = pick(MIX.customer);
  const price = pick(MIX.price);
  const items = pick(MIX.items);
  const start = pickStart(now, slot);
  const resources = [...RESOURCES].sort(() => Math.random() - 0.5).slice(0, items.count);

  const requestItems = resources.map((resourceId) => ({
    resourceId,
    resourceTypeId: RESOURCE_TYPE_ID,
    startTime: formatLocal(start),
    endTime: formatLocal(start + 3600000),
    price: Math.round(randomInt(price.min, price.max) / 1000) * 1000,
  }));
  const sum = requestItems.reduce((total, item) => total + item.price, 0);
  // Ô giá phải khớp thực tế: HIGH_VALUE nghĩa là tổng vượt ngưỡng; nâng giá item đầu nếu tổng chưa vượt
  if (price.name === 'HIGH_VALUE' && sum <= HIGH_VALUE_THRESHOLD) {
    requestItems[0].price += HIGH_VALUE_THRESHOLD - sum + 100000;
  }
  const totalPrice = requestItems.reduce((total, item) => total + item.price, 0);

  const body = {
    reservationId: uuid(),
    branchId: BRANCH_ID,
    branchTimeZone: TIME_ZONE,
    items: requestItems,
    customer: buildCustomer(profile, now),
    totalPrice,
    currency: 'VND',
    channel: profile.isGuest ? 'WALK_IN' : 'ONLINE',
    demand: { occupancyRate: Math.round(Math.random() * 100) / 100, waitlistCount: randomInt(0, 5) },
    requestedAt: formatLocal(now),
  };
  return { body, cells: [slot.name, profile.name, totalPrice > HIGH_VALUE_THRESHOLD ? 'HIGH_VALUE' : 'NORMAL', items.name] };
}
