Desktop Folders v6 — MERGE_ARMED virtual collections

DesktopFolders.exe chạy nền trong system tray và giữ nguyên lưới Windows Explorer.

Cách hoạt động:
- Kéo icon bằng drag image nguyên bản của Explorer; ứng dụng không chặn mouse events.
- Quick drop trước 280 ms: Windows tiếp tục sở hữu drag/drop và xử lý như bình thường.
- Giữ trên icon đích khoảng 280 ms: timer độc lập với mouse-move chuyển state sang MERGE_ARMED và chạy animation preview.
- Trong MERGE_ARMED, WM_MOUSEMOVE vẫn được chuyển tiếp để con trỏ và drag image của Explorer luôn chuyển động mượt. Ngay trước mouse-up, DesktopFolders gửi Escape + WM_CANCELMODE, nuốt mouse-up và commit đúng một collection operation.
- Di chuyển khỏi target ở bất kỳ thời điểm nào trước khi thả sẽ hủy trạng thái armed và trả gesture cho Windows; vùng hit được nới nhẹ. Cache Desktop quét nền mỗi 1.2 giây và có refresh riêng sau 450 ms của mỗi quick Windows drop để giữ vị trí chính xác mà không gây lag liên tục.
- Nhóm là dữ liệu virtual trong %APPDATA%\DesktopFolders\virtual-layout.json, không phải thư mục File Explorer.
- Item gốc giữ nguyên đường dẫn và thuộc tính gốc được lưu; ứng dụng chỉ đặt Hidden để thay chúng bằng một collection tile có thể phục hồi.
- Desktop hiển thị một shortcut tile có icon collection 3x3 dựng từ tối đa 9 icon con; không dùng generic Windows folder.
- Popup dùng design system dark-slate/Fluent được tinh chỉnh bằng UI/UX Pro Max: background #0F172A, card #1B2336, control #1E293B, border #475569, text #F8FAFC và focus ring #93C5FD. Compact giữ 440×520; expanded là 840×600.
- Header 52 px, search 48 px và card compact khoảng 96×112 theo nhịp spacing 4/8 px. Icon vector tự vẽ có cùng stroke; thứ tự từ trái sang phải là grid, list, phóng/thu và X ngoài cùng bên phải. Vùng app dùng viewport dịch chuyển + scrollbar tối tự vẽ, không tạo native AutoScroll.
- Tên collection nằm cùng hàng header với toolbar. Cụm card được căn giữa theo viewport; compact và expanded luôn giữ đúng ba icon/card mỗi hàng, với khoảng cách cân đối tới scrollbar.
- App card không hiển thị dòng “Ứng dụng”; toàn bộ vùng chữ phía dưới icon được dành cho tên dài. Toolbar dùng state fill khi hover/focus/selected, không vẽ outline xanh quanh bốn nút góc phải.
- Tên collection là label tĩnh; chỉ chuyển thành ô edit có caret khi người dùng bấm vào tên, Enter/ra ngoài để lưu và Escape để hủy.
- Click vùng nền của form/header/content/grid/scrollbar chuyển focus về form: caret ở title/search biến mất; title đang edit được commit trước khi bỏ focus.
- Popup chỉ dùng topmost ngắn trong lúc mở để không nằm sau Desktop, sau đó trở lại normal z-order nên mọi software khác có thể phủ lên nó.
- Bấm vùng Desktop trống sẽ đóng collection. Bấm một icon hoặc collection khác không đóng collection hiện tại; nhiều collection có thể mở đồng thời và item được chuyển trực tiếp giữa chúng mà không làm mất trạng thái Hidden.
- Command window được tạo trước registry/tile rebuild/UI Automation. Request mở được coalesce theo groupId; sender retry trong startup và có request-file fallback nếu IPC chưa sẵn sàng, nên single-instance mutex không thể làm mất lệnh mở.
- Có thể kéo item ngoài vào collapsed collection hoặc expanded panel; standalone icon bị ẩn và chỉ còn trong collection.
- Có thể kéo item từ expanded panel trở lại Desktop để khôi phục standalone icon và thuộc tính gốc. Drag nội bộ không gửi FileDrop cho Explorer nên không còn lỗi “same destination”; SHChangeNotify làm icon hiện ngay mà không cần refresh.
- Folder tự giải thể khi còn một item nếu tùy chọn này được bật.

Khả năng nhận diện:
- Mọi icon Desktop có đường dẫn file: .lnk, .url, .appref-ms, .website, .exe và các file-based item khác.
- Quét Desktop cá nhân/OneDrive Desktop và Public Desktop.
- Icon hệ thống ảo không có đường dẫn file (ví dụ Recycle Bin) được báo là chưa hỗ trợ trong Settings > Desktop target detection.

Hiệu năng và an toàn:
- Hook chỉ nhận một drag khi shell Desktop thực sự foreground, nên thao tác tại cùng tọa độ trong ứng dụng khác không thể bị nhận nhầm.
- Mỗi groupId chỉ có tối đa một popup; các lệnh mở trùng hủy ghost animation cũ và đưa form thật lên ngay. Popup ưu tiên gap 10 px phía dưới/trên tile; nếu không đủ chỗ dọc thì neo bên phải/trái tile, chỉ cascade nhẹ để tránh chồng lấn. Mỗi lần Desktop cache/quick-drop refresh, popup đang mở nhận bounds mới và tự đi theo tile collection.
- Icon được cache và mỗi lần scan chỉ lập chỉ mục file Desktop một lần, tránh chi phí duyệt thư mục lặp O(n²) khi nhiều collection mở.
- Nội dung thật được đặt ngay vào semantic state cuối. Hiệu ứng mở/phóng/thu dùng một overlay cố định trong suốt: chỉ snapshot di chuyển bên trong, không resize HWND/Region từng frame. Ghost click-through khoảng 130 ms và có thể bị request mới hủy; X/Escape đóng form thật ngay rồi phát ghost 90 ms thu về tile. Khởi tạo registry/tile/scan chạy nền STA sau khi message loop sẵn sàng.
- Không còn gửi LVM_SETITEMPOSITION theo index UI Automation, không phát UPDATEDIR/ASSOCCHANGED toàn Desktop khi cập nhật tile; chỉ gửi thông báo attributes/update cho đúng path để tránh làm icon không liên quan đổi vị trí.
- Quét UI Automation thường xuyên chỉ khi Desktop đang foreground; khi dùng ứng dụng khác, tiến trình nền ngủ và chủ động trả working set không dùng cho Windows.
- Smoke test trên máy build: 0.0000 giây CPU trong 8 giây khi Desktop không foreground; private bytes khoảng 38 MB, resident working set giảm còn khoảng 4 MB sau idle trim.
- Backup/Restore lưu virtual layout dạng JSON.
- Không có startup/status window hoặc taskbar window; vòng đời dùng ApplicationContext + tray icon.
- Keyboard: Tab có focus ring, Enter/Space mở app, F2 đổi tên, Ctrl+F tìm kiếm và Escape đóng. App card/toolbar có accessible name; item ghim có accessible description và marker vector.
- Menu tray > Khôi phục toàn bộ icon rồi Exit sẽ trả lại thuộc tính gốc, xóa tile/icon collection và thoát.
- Exit thông thường giữ bố cục virtual cho lần chạy sau.

Mã nguồn C# có trong DesktopFolders-source.zip.

Build từ source:
- Yêu cầu Windows có .NET Framework 4.x (csc.exe, WinForms, UI Automation assemblies).
- Mở PowerShell trong thư mục source và chạy: .\build.ps1
- Build chẩn đoán drag/popup (ghi %APPDATA%\DesktopFolders\drag-diagnostic.log): .\build.ps1 -TraceDrag -Output DesktopFolders-test.exe

Các kiểm thử đã chạy:
- Compile release và diagnostic build với version metadata 6.0.0.0.
- Self-test tạo .lnk collection + PNG-backed 256x256 ICO; kiểm tra target, arguments, icon path, alpha và màu nền composite.
- UI test: title static/click-to-edit/Escape, grid/list, search filtering, hai collection mở đồng thời và collection vẫn tồn tại phía sau software foreground.
- Startup race test: 5 lệnh mở gửi trong ~60 ms tạo đúng một process và một PANEL shown; request được coalesce trước background registry/tile/scan.
- Cold two-group anchor test: tile AI `{X=0,Y=615}` mở popup phía trên với gap 10 px; tile New Folder `{X=95,Y=493}` mở popup bên phải tại `X=199`, không che tile. Hai popup cùng tồn tại trong một process.
- Idle smoke test đo working set/private bytes/CPU.
- Hai icon DF6-* chỉ được dùng cho kiểm thử Desktop thực tế và không thuộc gói bàn giao.
