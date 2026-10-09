// SCALE-2 (O-18): 50 RPS qua reverse proxy tới hai instance; `repeatRatio` request gửi LẠI đúng khóa idempotency + payload của một request trước đó của cùng VU
// (hai lần có thể rơi vào hai instance khác nhau). Kết luận do evaluator tính từ D2 + PostgreSQL + Prometheus.
import { scenarioOptions, sendDecision, summaryOutput } from '../lib/run.js';
import { loadProfile } from '../lib/options.js';

export const options = scenarioOptions('scale2');

const REPEAT_RATIO = loadProfile('scale2').profile.repeatRatio;
const HISTORY_LIMIT = 50;
const history = [];

export default function () {
  if (history.length > 0 && Math.random() < REPEAT_RATIO) {
    const previous = history[Math.floor(Math.random() * history.length)];
    sendDecision({ body: previous.body, idempotencyKey: previous.idempotencyKey });
    return;
  }
  const sent = sendDecision();
  history.push({ body: sent.body, idempotencyKey: sent.idempotencyKey });
  if (history.length > HISTORY_LIMIT) {
    history.shift();
  }
}

export function handleSummary(data) {
  return summaryOutput(data);
}
