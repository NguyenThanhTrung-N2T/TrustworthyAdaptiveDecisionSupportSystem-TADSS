// LT01 (methodology 4.6): hồ sơ tải theo config/modes.json (MODE=quick|full). Chạy bởi evaluator bước (b); xem README.md.
import { scenarioOptions, iteration, summaryOutput } from '../lib/run.js';

export const options = scenarioOptions('lt01');

export default function () {
  iteration();
}

export function handleSummary(data) {
  return summaryOutput(data);
}
