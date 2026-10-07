// Một lượt lặp của kịch bản tải: sinh payload, gửi POST /api/v2/decisions, ghi một dòng D2 (O-15).
import http from 'k6/http';
import { check } from 'k6';
import execution from 'k6/execution';
import { buildPayload, uuid } from './payload.js';
import { buildOptions } from './options.js';
import { logRequest, summaryOutput } from './output.js';

const BASE_URL = (__ENV.BASE_URL || 'http://eval-tadss-1:8080').replace(/\/$/, '');
const API_KEY = __ENV.API_KEY || '';
const SCENARIO = __ENV.SCENARIO || 'lt01';
const SAMPLE_ONLY = Number(__ENV.SAMPLE_ONLY || 0);
const DECISIONS_URL = `${BASE_URL}/api/v2/decisions`;

// SAMPLE_ONLY=N: chỉ in N payload (không gửi) để pytest kiểm schema; ngược lại dùng options theo MODE
export function scenarioOptions(key) {
  return SAMPLE_ONLY > 0 ? { vus: 1, iterations: 1 } : buildOptions(key);
}

export { summaryOutput };

function parse(response) {
  if (response.status !== 200 || !response.body) {
    return {};
  }
  try {
    return JSON.parse(response.body);
  } catch (error) {
    return {};
  }
}

// Gửi một request. `options.idempotencyKey`/`options.body` cho phép kịch bản (scale2) lặp lại đúng khóa + payload của request trước.
export function sendDecision(options = {}) {
  const body = options.body || buildPayload().body;
  const idempotencyKey = options.idempotencyKey || body.reservationId;
  const correlationId = uuid();
  const response = http.post(DECISIONS_URL, JSON.stringify(body), {
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': API_KEY,
      'X-Idempotency-Key': idempotencyKey,
      'X-Correlation-ID': correlationId,
    },
    tags: { name: 'POST /api/v2/decisions' },
  });
  check(response, { 'status 200': (r) => r.status === 200 });
  const decision = parse(response);
  logRequest({
    ts: new Date().toISOString(),
    scenario: SCENARIO,
    phase: execution.scenario.name,
    http_status: response.status,
    duration_ms: Math.round(response.timings.duration * 1000) / 1000,
    decision_status: decision.status || null,
    execution_type: decision.executionType || null,
    correlation_id: correlationId,
    idempotency_key: idempotencyKey,
    decision_id: decision.decisionId || null,
  });
  return { body, response, decision, idempotencyKey };
}

export function iteration() {
  if (SAMPLE_ONLY > 0) {
    for (let index = 0; index < SAMPLE_ONLY; index += 1) {
      console.log(JSON.stringify(buildPayload()));
    }
    return;
  }
  sendDecision();
}
