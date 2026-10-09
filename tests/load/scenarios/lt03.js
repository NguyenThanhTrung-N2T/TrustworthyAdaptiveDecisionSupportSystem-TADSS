// LT03 (methodology 4.6): tải nền 50 RPS suốt lịch sự cố; evaluator bật/tắt lỗi theo chaos_schedule.py (không có warm-up: pha nền của lịch đóng vai trò đó).
import { scenarioOptions, iteration, summaryOutput } from '../lib/run.js';

export const options = scenarioOptions('lt03');

export default function () {
  iteration();
}

export function handleSummary(data) {
  return summaryOutput(data);
}
