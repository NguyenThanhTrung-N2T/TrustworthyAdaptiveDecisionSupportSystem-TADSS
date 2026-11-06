# Giữ laptop đo thức suốt một lần đo perf (06/11: lượt đầu bị Modern Standby giữa lượt). Không đổi cài đặt nguồn;
# tự thoát khi log có "=== xong" hoặc sau 3 giờ. Gập màn hình / bấm nút nguồn vẫn làm máy ngủ.
param([string]$Log)
$t = Add-Type -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);' -Name P -Namespace KeepAwake -PassThru
# ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED: chặn ngủ/tắt màn hình do không thao tác, tự hết khi tiến trình thoát
$t::SetThreadExecutionState([uint32]2147483651) | Out-Null
$deadline = (Get-Date).AddHours(3)
while ((Get-Date) -lt $deadline -and -not (Select-String -Path $Log -Pattern '=== xong' -Quiet)) { Start-Sleep -Seconds 20 }
