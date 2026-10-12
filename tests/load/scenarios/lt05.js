// LT05 (methodology 4.6): hồ sơ tải theo config/modes.json (MODE=quick|full). Chạy bởi evaluator bước (b); xem docs/guides/EVALUATION_RUN_GUIDE.md, mục "Kịch bản tải k6".
import { scenarioOptions, iteration, summaryOutput } from '../lib/run.js';

export const options = scenarioOptions('lt05');

export default function () {
  iteration();
}

export function handleSummary(data) {
  return summaryOutput(data);
}
