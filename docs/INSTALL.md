# Hướng dẫn cài đặt Sohoa Scan Agent

## Tải về

Vào [Releases](https://github.com/tlong1610/sohoa-scan-agent/releases).

| Máy quét | File cần tải |
|----------|--------------|
| **Plustek PS4080U** (và hầu hết máy văn phòng) | `SohoaScanAgent-X.X.X-win-x86.zip` |
| Máy có driver TWAIN 64-bit (hiếm) | `SohoaScanAgent-X.X.X-win-x64.zip` |

> **Quan trọng:** Driver TWAIN Plustek PS4080U chỉ có bản **32-bit** (`DocTWAIN32.ds`).
> Nếu chạy bản **win-x64**, `/health` sẽ trả `"twainSources": []` dù đã cài driver.

## Cài trên máy quét

1. **Driver máy scan** — cài driver TWAIN Plustek PS4080U từ [Plustek Support](https://plustek.com/eu/products/workgroup-scanners/smartoffice-ps4080u/support.php).
2. **Giải nén** file zip **win-x86** vào thư mục cố định, ví dụ:
   ```
   C:\Program Files\SohoaScanAgent\SohoaScanAgent.exe
   ```
3. **Chạy** `SohoaScanAgent.exe` — icon xuất hiện ở khay hệ thống (system tray).
4. **Tự chạy khi đăng nhập** (khuyến nghị): tạo shortcut trong `shell:startup`.

## Kiểm tra

```
http://127.0.0.1:18612/health
```

Kết quả đúng (Plustek PS4080U):

```json
{
  "status": "ok",
  "version": "1.0.3",
  "processBitness": "x86",
  "twainSources": [
    "Plustek SmartOffice PS4080U-TWAIN",
    "WIA-Plustek SmartOffice PS4080U"
  ]
}
```

Nếu `twainSources: []`:
- Đang chạy bản **x64** → chuyển sang **win-x86**
- Hoặc chưa cài driver TWAIN → cài lại từ Plustek

## Sử dụng

1. Cắm máy scan USB vào máy tính.
2. Mở web app Sohoa trên **cùng máy**.
3. Vào **Tiếp nhận quét** → **Quét trang** → hoàn tất trên cửa sổ TWAIN.

## Lưu ý

- Chỉ cài trên **PC trạm quét**, không cài trên server.
- File staging: `%AppData%\SohoaScanAgent\sessions\`
