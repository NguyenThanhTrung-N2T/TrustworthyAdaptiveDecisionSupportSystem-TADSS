// Đầu ra của kịch bản tải (O-15): mỗi request một dòng JSON (D2 timeseries) qua console (k6 --console-output), và summary.json qua handleSummary.

// Một dòng D2. `phase` = warmup | main (kịch bản warm-up bị loại khỏi thống kê ở bước (e)).
export function logRequest(entry) {
  console.log(JSON.stringify(entry));
}

// Ghi summary vào SUMMARY_PATH (đường dẫn do evaluator đặt); không đặt thì không ghi file (chạy thủ công vẫn in log của k6).
export function summaryOutput(data) {
  const path = __ENV.SUMMARY_PATH;
  return path ? { [path]: JSON.stringify(data, null, 2) } : {};
}
