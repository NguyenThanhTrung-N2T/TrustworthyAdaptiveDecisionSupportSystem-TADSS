// LT02 (methodology 4.6): hồ sơ tải theo config/modes.json (MODE=quick|full). Chạy bởi evaluator bước (b); xem docs/guides/RUNBOOK.md (mục lượt đánh giá TADSS).
import { scenarioOptions, iteration, summaryOutput } from '../lib/run.js';

export const options = scenarioOptions('lt02');

export default function () {
  iteration();
}

export function handleSummary(data) {
  return summaryOutput(data);
}
