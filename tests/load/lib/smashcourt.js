// Client SmashCourt-BE cho LT-04 (plan 6A-6 Task 14). Thân request đúng DTO của BE: RegisterDto, VerifyEmailDto, LoginDto,
// CreateOnlineBookingDto (POST /api/bookings/online). Envelope { success, code, message, data, errors }.
// Rate limit của BE: đăng ký/xác thực ("sensitive") và đăng nhập ("login") đếm theo IP; đặt sân ("booking") đếm theo người dùng.
// Mọi lời gọi chịu 429 đều chờ theo Retry-After rồi thử lại (giới hạn số lần), để setup() chạy được với nhiều tài khoản từ một máy.
import http from 'k6/http';
import { sleep } from 'k6';

const JSON_HEADERS = { 'Content-Type': 'application/json' };

function auth(token) {
  return { headers: { ...JSON_HEADERS, Authorization: `Bearer ${token}` } };
}

// Đọc data của envelope; lỗi thì ném kèm mã lỗi của BE để log dễ đọc
export function data(res, label) {
  let body = null;
  try {
    body = res.json();
  } catch (_) {
    // thân không phải JSON
  }
  if (res.status < 200 || res.status >= 300 || !body || body.success === false) {
    const code = body && body.code ? body.code : '-';
    throw new Error(`${label}: HTTP ${res.status} ${code} ${body && body.message ? body.message : ''}`.trim());
  }
  return body.data;
}

// Gọi lại khi 429: chờ Retry-After (giây) hoặc 15 giây, tối đa maxAttempts lần
export function withRetryAfter(call, maxAttempts = 10) {
  let res = call();
  for (let attempt = 1; res.status === 429 && attempt < maxAttempts; attempt++) {
    const retryAfter = Number(res.headers['Retry-After']);
    sleep(Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : 15);
    res = call();
  }
  return res;
}

export function register(baseUrl, email, password, fullName) {
  return withRetryAfter(() =>
    http.post(`${baseUrl}/api/auth/register`, JSON.stringify({ email, password, fullName }), { headers: JSON_HEADERS }));
}

export function verifyEmail(baseUrl, email, otpCode) {
  return withRetryAfter(() =>
    http.post(`${baseUrl}/api/auth/verify-email`, JSON.stringify({ email, otpCode }), { headers: JSON_HEADERS }));
}

export function login(baseUrl, email, password) {
  const res = withRetryAfter(() =>
    http.post(`${baseUrl}/api/auth/login`, JSON.stringify({ email, password }), { headers: JSON_HEADERS }));
  return data(res, `login ${email}`).accessToken;
}

// Mã OTP 6 số của một thư: ưu tiên bản text; bản HTML phải bỏ <style> và mọi thẻ trước (mã màu như #475569 cũng là 6 chữ số)
export function extractOtp(text, html) {
  const fromText = /\b(\d{6})\b/.exec(text || '');
  if (fromText) {
    return fromText[1];
  }
  const visible = (html || '').replace(/<style[\s\S]*?<\/style>/gi, ' ').replace(/<[^>]*>/g, ' ');
  const fromHtml = /\b(\d{6})\b/.exec(visible);
  return fromHtml ? fromHtml[1] : null;
}

// Mã OTP trong thư mới nhất gửi tới email (API Mailpit); chờ tối đa ~10 giây vì email gửi sau khi request đăng ký trả về
export function readOtpFromMailpit(mailpitUrl, email) {
  for (let attempt = 0; attempt < 20; attempt++) {
    const search = http.get(`${mailpitUrl}/api/v1/search?query=${encodeURIComponent(`to:"${email}"`)}`);
    const messages = search.status === 200 ? search.json('messages') || [] : [];
    if (messages.length > 0) {
      const message = http.get(`${mailpitUrl}/api/v1/message/${messages[0].ID}`).json();
      const otp = extractOtp(message.Text, message.HTML);
      if (otp) {
        return otp;
      }
    }
    sleep(0.5);
  }
  throw new Error(`Không thấy thư OTP của ${email} trong Mailpit (${mailpitUrl})`);
}

// Đăng ký qua Mailpit rồi đăng nhập; trả token
export function registerAndLogin(baseUrl, mailpitUrl, email, password, fullName) {
  data(register(baseUrl, email, password, fullName), `register ${email}`);
  data(verifyEmail(baseUrl, email, readOtpFromMailpit(mailpitUrl, email)), `verify-email ${email}`);
  return login(baseUrl, email, password);
}

// Sân đặt được của chi nhánh (GET /api/courts?branchId=, công khai)
export function listCourts(baseUrl, branchId) {
  const courts = data(http.get(`${baseUrl}/api/courts?branchId=${branchId}`), 'courts');
  return courts.filter((c) => c.status !== 'INACTIVE' && c.status !== 'SUSPENDED').map((c) => c.id);
}

export function createOnlineBooking(baseUrl, token, slot, note, tags) {
  const body = {
    bookingDate: slot.date,
    courts: [{ courtId: slot.courtId, startTime: slot.start, endTime: slot.end }],
    note,
  };
  return http.post(`${baseUrl}/api/bookings/online`, JSON.stringify(body), { ...auth(token), tags });
}

export function cancelMyBooking(baseUrl, token, bookingId) {
  return http.post(`${baseUrl}/api/me/bookings/${bookingId}/cancel`, null, auth(token));
}

export function getMyBooking(baseUrl, token, bookingId) {
  return http.get(`${baseUrl}/api/me/bookings/${bookingId}`, auth(token));
}
