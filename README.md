# DesktopFolders

DesktopFolders tạo các collection ảo trực tiếp trên Windows Desktop, tương tự nhóm ứng dụng trên màn hình điện thoại. Collection không phải thư mục Explorer: file và shortcut gốc chỉ được ẩn, không bị xóa hoặc di chuyển.

<img src="DesktopFolders.png" width="96" alt="DesktopFolders flat folder icon">

## Tính năng

- Quick drop trước ngưỡng hover vẫn do Windows xử lý.
- Giữ trên icon đích khoảng 280 ms để chuyển sang `MERGE_ARMED` và tạo collection.
- Popup collection có tìm kiếm, grid/list, ghim ưu tiên, đổi tên, kéo vào/ra và tự giải thể.
- Compact `440×520`, expanded `840×600`, luôn giữ ba card mỗi hàng.
- Popup neo cạnh tile collection và tự cập nhật vị trí sau khi Desktop thay đổi.
- Item có thể chuyển trực tiếp giữa các collection mà không làm mất thuộc tính gốc.
- Layout lưu tại `%APPDATA%\DesktopFolders\virtual-layout.json`.
- Chạy nền bằng system tray; hỗ trợ Backup/Restore và khởi động cùng Windows.
- Mỗi collection chỉ có một popup; startup IPC có retry, coalescing và fallback để không làm mất lệnh mở.

## An toàn dữ liệu

DesktopFolders không biến collection thành thư mục vật lý. Khi thêm item vào collection, ứng dụng lưu thuộc tính gốc rồi đặt cờ `Hidden`. Khi kéo item ra hoặc khôi phục toàn bộ, thuộc tính ban đầu được áp dụng lại.

Menu tray **Khôi phục toàn bộ icon rồi Exit** sẽ hiện lại các item, xóa tile collection và thoát ứng dụng.

## Cài đặt

1. Tải [`release/DesktopFolders.exe`](release/DesktopFolders.exe).
2. Chạy file EXE; ứng dụng xuất hiện trong system tray và không tạo cửa sổ taskbar thường trực.
3. Kéo một icon Desktop lên icon khác và giữ khoảng 280 ms để tạo collection.

Ứng dụng hiện chưa được ký số nên Windows có thể hiển thị cảnh báo SmartScreen cho file tải từ Internet.

## Build từ source

Yêu cầu Windows có .NET Framework 4.x, WinForms và UI Automation assemblies.

```powershell
.\build.ps1
```

Build chẩn đoán drag/open:

```powershell
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe
```

Tái tạo icon flat của dự án:

```powershell
.\generate-icon.ps1
```

Icon được dựng bằng `System.Drawing` từ các hình học phẳng; repository không sử dụng artwork sinh bởi AI.

## Cấu trúc repository

```text
DirectDesktopFolders.cs   Mã nguồn ứng dụng
DesktopFolders.ico        Icon dùng khi build EXE
DesktopFolders.png        Preview icon
build.ps1                 Build script
generate-icon.ps1         Script tái tạo icon
release/DesktopFolders.exe  Release build
```

## Giới hạn hiện tại

- Chỉ các Desktop item có đường dẫn file mới được đưa vào collection.
- Icon hệ thống ảo như Recycle Bin chưa được hỗ trợ.
- Ứng dụng dành cho Windows và phụ thuộc vào Desktop Explorer (`SysListView32`).


