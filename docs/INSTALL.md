# Hướng dẫn cài đặt Sohoa Scan Agent

## Tải về

Vào [Releases](https://github.com/tlong1610/sohoa-scan-agent/releases) → tải file `SohoaScanAgent-vX.X.X-win-x64.zip`.

## Cài trên máy quét

1. **Driver máy scan** — cài driver TWAIN Plustek PS4080U (hoặc driver TWAIN của hãng máy bạn dùng).
2. **Giải nén** file zip vào thư mục cố định, ví dụ:
   ```
   C:\Program Files\SohoaScanAgent\SohoaScanAgent.exe
   ```
3. **Chạy** `SohoaScanAgent.exe` — icon xuất hiện ở khay hệ thống (system tray).
4. **Tự chạy khi đăng nhập** (khuyến nghị): tạo shortcut trong:
   ```
   shell:startup
   ```
   trỏ tới `SohoaScanAgent.exe`.

## Kiểm tra

Mở trình duyệt trên cùng máy, truy cập:

```
http://127.0.0.1:18612/health
```

Nếu thấy `{"status":"ok",...}` → Agent đang chạy.

## Sử dụng

1. Cắm máy scan USB vào máy tính.
2. Mở web app Sohoa (URL production của bạn).
3. Vào màn **Quét tài liệu** → bấm **Quét** → hoàn tất trên cửa sổ TWAIN.

## Lưu ý

- Chỉ cài trên **PC trạm quét** (máy cắm máy scan), không cài trên server.
- Mỗi trạm quét cần cài một lần.
- File staging lưu tại: `%AppData%\SohoaScanAgent\sessions\`
