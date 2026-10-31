// Kiểm mức cọc của LT-04 (plan 6A-6 Task 14, R18, R29), hàm thuần: không gọi mạng, nên chạy được cả ở chế độ dữ liệu giả.
// Không lấy depositRate của chính response làm chuẩn: giá trị kỳ vọng đến từ cấu hình strategy của lượt chạy (LT04_EXPECTED_RATE).

export const FALLBACK = 'fallback';
export const TADSS = 'tadss';

const RATE_SCALE = 10000; // TADSS giữ tỷ lệ cọc tối đa 4 chữ số thập phân (MaxRateDecimals = 4)

// Tỷ lệ dạng số nguyên phần vạn; quá 4 chữ số thập phân thì ném (không phải tỷ lệ TADSS trả được)
export function rateInTenThousandths(rate) {
  const scaled = Math.round(rate * RATE_SCALE);
  if (Math.abs(rate * RATE_SCALE - scaled) > 1e-6) {
    throw new Error(`Tỷ lệ ${rate} có hơn 4 chữ số thập phân`);
  }
  return scaled;
}

// Số tiền cọc kỳ vọng = round(tổng × tỷ lệ) tới đồng, xa số 0 (INTEGRATION_SPEC §3.3; DepositCalculator của TADSS dùng decimal).
// Tính bằng số nguyên: tổng × tỷ lệ bằng số thực lệch ở biên ,5 (ví dụ 1.450 × 0,29 ra 420,49999… thay vì 420,5)
export function expectedDeposit(total, rate) {
  if (!Number.isInteger(total) || total < 0) {
    throw new Error(`Tổng tiền ${total} phải là số đồng nguyên không âm`);
  }
  const product = total * rateInTenThousandths(rate); // ≤ 2^53 với mọi tổng thực tế
  const remainder = product % RATE_SCALE;
  const quotient = (product - remainder) / RATE_SCALE;
  return remainder * 2 >= RATE_SCALE ? quotient + 1 : quotient;
}

// Cấu hình chế độ; thiếu thông tin nguồn gốc của giá trị kỳ vọng ở chế độ tadss thì ném (setup dừng, không chạy)
export function expectationFromEnv(env) {
  const mode = (env.LT04_EXPECT || FALLBACK).toLowerCase();
  if (mode === FALLBACK) {
    return { mode };
  }
  if (mode !== TADSS) {
    throw new Error(`LT04_EXPECT phải là '${FALLBACK}' hoặc '${TADSS}', không phải '${env.LT04_EXPECT}'`);
  }
  const missing = ['LT04_EXPECTED_RATE', 'LT04_STRATEGY', 'LT04_EXPECTED_RATE_SOURCE'].filter((name) => !env[name]);
  if (missing.length > 0) {
    throw new Error(`Chế độ tadss thiếu ${missing.join(', ')} (nguồn gốc giá trị kỳ vọng, R29)`);
  }
  const rate = Number(env.LT04_EXPECTED_RATE);
  if (!(rate >= 0 && rate <= 1)) {
    throw new Error(`LT04_EXPECTED_RATE = '${env.LT04_EXPECTED_RATE}' phải trong [0; 1]`);
  }
  rateInTenThousandths(rate); // quá 4 chữ số thập phân ⇒ dừng
  return { mode, rate, strategy: env.LT04_STRATEGY, rateSource: env.LT04_EXPECTED_RATE_SOURCE };
}

// Mức cọc trong response đặt sân có đúng kỳ vọng của chế độ không; trả { ok, reason }
export function checkDeposit(expectation, booking) {
  const total = Number(booking.finalTotal);
  const rate = Number(booking.depositRate);
  const amount = Number(booking.depositAmount);
  if (expectation.mode === FALLBACK) {
    if (booking.depositSource !== 'FULL_PAYMENT_FALLBACK') {
      return { ok: false, reason: `depositSource ${booking.depositSource}, kỳ vọng FULL_PAYMENT_FALLBACK` };
    }
    if (rate !== 1 || amount !== total) {
      return { ok: false, reason: `cọc ${rate} / ${amount} đ, kỳ vọng 1 / ${total} đ` };
    }
    return { ok: true };
  }

  if (booking.depositSource !== 'TADSS') {
    return { ok: false, reason: `depositSource ${booking.depositSource}, kỳ vọng TADSS` };
  }
  const expectedAmount = expectedDeposit(total, expectation.rate);
  if (rate !== expectation.rate || amount !== expectedAmount) {
    return { ok: false, reason: `cọc ${rate} / ${amount} đ, kỳ vọng ${expectation.rate} / ${expectedAmount} đ (${expectation.strategy})` };
  }
  return { ok: true };
}

// Đơn PENDING phải có link thanh toán có hạn (P6-27)
export function checkPaymentLink(booking) {
  if (booking.status !== 'PENDING') {
    return { ok: true };
  }
  if (!booking.paymentUrl || booking.paymentUrl.indexOf('vnp_ExpireDate=') < 0) {
    return { ok: false, reason: 'paymentUrl thiếu vnp_ExpireDate' };
  }
  if (!booking.paymentLinkExpiresAt) {
    return { ok: false, reason: 'thiếu paymentLinkExpiresAt' };
  }
  return { ok: true };
}
