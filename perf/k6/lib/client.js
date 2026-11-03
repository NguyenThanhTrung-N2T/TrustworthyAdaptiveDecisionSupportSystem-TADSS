// Client SmashCourt-BE cho môi trường perf (Epic 6C, task 2.15, 2.18). Mọi request gắn tag:
//   name    mẫu endpoint (không chứa id, để Prometheus không bùng nhãn)
//   slo     nhóm SLO của plan 6C Phụ lục B.7.2: read | booking | login | ipn | write
//   journey hành trình J1…J7 đang chạy (đặt ở journeys.js qua tagWithJourney)
//   phase   warmup | settle | steady theo thời điểm gửi (mix.js đăng ký đồng hồ pha); chỉ steady vào thống kê
// Lỗi (B.7.2) = 5xx + timeout/lỗi mạng (status 0) + 4xx NGOÀI danh sách (status, code) hợp lệ của endpoint trong slo.json:
// ghi vào Rate `request_errors`; cặp hợp lệ đếm ở `business_rejections` theo code. Timeout mỗi request: slo.json `timeout` (5 s).
import http from 'k6/http';
import crypto from 'k6/crypto';
import exec from 'k6/execution';
import { Counter, Rate } from 'k6/metrics';

export const SLO = JSON.parse(open('../slo.json'));
export const BASE_URL = __ENV.BASE_URL || 'http://be:8080';
// HashSecret VNPay GIẢ của stack perf (perf/stack/docker-compose.yml): k6 ký IPN như VNPay
const VNPAY_HASH_SECRET = __ENV.VNPAY_HASH_SECRET || 'PERFHASHSECRETNOTREAL0123456789AB';
const VNPAY_TMN_CODE = __ENV.VNPAY_TMN_CODE || 'PERFTEST';

export const businessRejections = new Counter('business_rejections');
export const requestErrors = new Rate('request_errors');
const OK_ONLY = http.expectedStatuses({ min: 200, max: 299 });
// http_req_failed khớp request_errors ở mức status: các status có cặp hợp lệ không bị coi là hỏng (code kiểm sau khi có phản hồi)
const EXPECTED = Object.fromEntries(Object.entries(SLO.businessRejections).map(([name, pairs]) =>
  [name, http.expectedStatuses({ min: 200, max: 299 }, ...new Set(pairs.map((p) => p.status)))]));

let journeyTag = '-';
export function tagWithJourney(journey) {
  journeyTag = journey;
}

let phaseClock = null;
export function usePhaseClock(clock) {
  phaseClock = clock;
}

function params(name, slo, token) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  if (phaseClock) {
    // Tag cấp VU: áp cho http_req_* lẫn metric tự định nghĩa phát sau đó (request_errors, journeys…)
    exec.vu.metrics.tags.phase = phaseClock();
  }
  return { headers, timeout: SLO.timeout, tags: { name, slo, journey: journeyTag }, responseCallback: EXPECTED[name] || OK_ONLY };
}

function envelopeCode(res) {
  try {
    return res.json('code') || '-';
  } catch (_) {
    return '-';   // thân không phải JSON
  }
}

// Phân loại một phản hồi theo B.7.2; trả true khi là lỗi
function judge(res, name, slo) {
  let error = !(res.status >= 200 && res.status < 300);
  if (error && res.status >= 400 && res.status < 500) {
    const code = envelopeCode(res);
    if ((SLO.businessRejections[name] || []).some((p) => p.status === res.status && p.code === code)) {
      businessRejections.add(1, { journey: journeyTag, status: String(res.status), code });
      error = false;
    }
  }
  requestErrors.add(error, { name, slo });
  return res;
}

// data của envelope { success, code, message, data }; null khi không phải 2xx
export function data(res) {
  if (res.status >= 200 && res.status < 300) {
    try {
      return res.json('data');
    } catch (_) {
      return null;
    }
  }
  return null;
}

export function get(path, name, slo, token) {
  return judge(http.get(`${BASE_URL}${path}`, params(name, slo, token)), name, slo);
}

export function post(path, body, name, slo, token) {
  return judge(http.post(`${BASE_URL}${path}`, body === null ? null : JSON.stringify(body), params(name, slo, token)), name, slo);
}

// ── VNPay giả ──────────────────────────────────────────────────────────────

// WebUtility.UrlEncode của .NET: dấu cách thành '+', mã hex chữ hoa (giá trị ở đây chỉ gồm chữ và số nên chủ yếu để đúng quy ước)
function urlEncode(value) {
  return encodeURIComponent(value).replace(/%20/g, '+').replace(/%[0-9a-f]{2}/g, (m) => m.toUpperCase());
}

export function queryParam(url, key) {
  const match = new RegExp(`[?&]${key}=([^&]*)`).exec(url || '');
  return match ? decodeURIComponent(match[1].replace(/\+/g, ' ')) : null;
}

// IPN có chữ ký đúng như VnPayService.VerifyIpn kiểm: khóa vnp_* sắp theo ordinal, key=UrlEncode(value) nối '&', HMAC-SHA512
export function signedIpnQuery(txnRef, amountTimes100, success, transactionNo) {
  const fields = {
    vnp_Amount: String(amountTimes100),
    vnp_ResponseCode: success ? '00' : '24',
    vnp_TmnCode: VNPAY_TMN_CODE,
    vnp_TransactionNo: String(transactionNo),
    vnp_TransactionStatus: success ? '00' : '02',
    vnp_TxnRef: txnRef,
  };
  const signData = Object.keys(fields).sort().map((k) => `${k}=${urlEncode(fields[k])}`).join('&');
  return `${signData}&vnp_SecureHash=${crypto.hmac('sha512', VNPAY_HASH_SECRET, signData, 'hex')}`;
}
