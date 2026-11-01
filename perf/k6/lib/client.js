// Client SmashCourt-BE cho môi trường perf (Epic 6C, task 2.15). Mọi request gắn tag:
//   name    mẫu endpoint (không chứa id, để Prometheus không bùng nhãn)
//   slo     nhóm SLO của plan 6C Phụ lục B.7.2: read | booking | login | ipn | write
//   journey hành trình J1…J7 đang chạy (đặt ở journeys.js qua tagWithJourney)
// 4xx nghiệp vụ dự kiến (409 trùng sân, 400 hết hạn…) không tính là lỗi ở http_req_failed: khai bằng expected.
import http from 'k6/http';
import crypto from 'k6/crypto';
import { Counter } from 'k6/metrics';

export const BASE_URL = __ENV.BASE_URL || 'http://be:8080';
// HashSecret VNPay GIẢ của stack perf (perf/stack/docker-compose.yml): k6 ký IPN như VNPay
const VNPAY_HASH_SECRET = __ENV.VNPAY_HASH_SECRET || 'PERFHASHSECRETNOTREAL0123456789AB';
const VNPAY_TMN_CODE = __ENV.VNPAY_TMN_CODE || 'PERFTEST';

export const businessRejections = new Counter('business_rejections');
const OK_ONLY = http.expectedStatuses({ min: 200, max: 299 });

let journeyTag = '-';
export function tagWithJourney(journey) {
  journeyTag = journey;
}

function params(name, slo, token, expected) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  return {
    headers,
    tags: { name, slo, journey: journeyTag },
    responseCallback: expected ? http.expectedStatuses({ min: 200, max: 299 }, ...expected) : OK_ONLY,
  };
}

// data của envelope { success, code, message, data }; null khi lỗi (đếm 4xx nghiệp vụ vào business_rejections)
export function data(res, expected) {
  if (res.status >= 200 && res.status < 300) {
    try {
      return res.json('data');
    } catch (_) {
      return null;
    }
  }
  if (expected && expected.includes(res.status)) {
    let code = '-';
    try {
      code = res.json('code') || '-';
    } catch (_) {
      // thân không phải JSON
    }
    businessRejections.add(1, { journey: journeyTag, status: String(res.status), code });
  }
  return null;
}

export function get(path, name, slo, token, expected) {
  return http.get(`${BASE_URL}${path}`, params(name, slo, token, expected));
}

export function post(path, body, name, slo, token, expected) {
  return http.post(`${BASE_URL}${path}`, body === null ? null : JSON.stringify(body), params(name, slo, token, expected));
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
