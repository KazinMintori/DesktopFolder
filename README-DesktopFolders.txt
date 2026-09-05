Desktop Folders v6 — MERGE_ARMED virtual collections

DesktopFolders.exe mở Settings ngay khi chạy thủ công, sau đó tiếp tục chạy nền trong system tray và giữ nguyên lưới Windows Explorer. Nếu app đã chạy, bấm EXE lần nữa sẽ đưa cửa sổ Settings ra trước thay vì thoát im lặng. Chế độ --startup vẫn khởi động nền mà không hiện cửa sổ.

Cách hoạt động:
- Kéo icon bằng drag image nguyên bản của Explorer; ứng dụng không chặn mouse events.
- Quick drop trước 280 ms: Windows tiếp tục sở hữu drag/drop và xử lý như bình thường.
- Giữ trên icon đích khoảng 280 ms: timer chuyển state sang MERGE_ARMED, gửi WM_CANCELMODE/Escape trực tiếp tới cửa sổ Explorer rồi chạy preview; không dùng hook chuột toàn cục hoặc giả lập input hệ thống.
- Trước ngưỡng hold, di chuyển khỏi target trả toàn bộ gesture cho Windows. Sau khi armed, di chuyển ra ngoài sẽ hủy merge an toàn. Mouse-up chỉ được quan sát qua trạng thái nút, không bị phần mềm nuốt.
- Nhóm là dữ liệu virtual trong %APPDATA%\DesktopFolders\virtual-layout.json, không phải thư mục File Explorer.
- Item gốc giữ nguyên đường dẫn và thuộc tính gốc được lưu; ứng dụng chỉ đặt Hidden để thay chúng bằng một collection tile có thể phục hồi.
- Desktop hiển thị một shortcut tile có icon collection 3x3 dựng từ tối đa 9 icon con; không dùng generic Windows folder.
- Giao diện collection dùng nền tinh vân tím nhúng từ CollectionBackground.png, crop neo đáy; title, search, nút chọn, viền panel và card dùng gradient neon xanh→tím thật. Card kính trong suốt và icon được vẽ trực tiếp, không có ô nền đặc bao quanh. Compact vẫn giữ 440×520; expanded 840×600.
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
- Khi kéo trong panel, card đang cầm bám trực tiếp theo con trỏ; các card lân cận tiếp tục chuyển động từ frame hiện tại nên có thể rê qua lại mà không bị nhảy vị trí.
- Card gốc chuyển thành placeholder rỗng trong lúc kéo; ghost có shadow bám con trỏ bằng timer riêng. Swap dùng critically-damped spring có vận tốc liên tục nên đổi hướng ngay giữa animation.
- Hover/preview/reorder tính theo thời gian thực nên máy chậm có thể bỏ frame mà không đổi tốc độ animation; phiên Remote Desktop tự chuyển sang reduced-motion.
- Menu chuột phải dựng sẵn và hiện ngay; mục “Tùy chọn Windows…” mở riêng menu Shell đầy đủ khi cần.
- Icon đưa ra Desktop và icon còn lại sau khi hủy collection được xếp tuần tự vào ô lưới trống gần nhất qua Shell IFolderView. Ứng dụng không mở/ghi bộ nhớ tiến trình Explorer. Tile collection bị xóa bằng thông báo SHCNE_DELETE nên không cần refresh Desktop.
- Thả qua mép card để sắp xếp. Giữ ở vùng giữa card theo hover delay để tạo collection con; nếu card đích đã là collection thì item/collection đang cầm được chuyển vào collection đó.
- Có thể kéo một collection ngoài Desktop vào collection khác. Dữ liệu dùng GroupId ổn định, tự cập nhật đường dẫn khi đổi tên và chặn mọi vòng lặp A chứa B rồi B chứa A.
- Folder tự giải thể khi còn một item nếu tùy chọn này được bật.

Khả năng nhận diện:
- Mọi icon Desktop có đường dẫn file: .lnk, .url, .appref-ms, .website, .exe và các file-based item khác.
- Quét Desktop cá nhân/OneDrive Desktop và Public Desktop.
- Icon hệ thống ảo không có đường dẫn file (ví dụ Recycle Bin) được báo là chưa hỗ trợ trong Settings > Desktop target detection.

Hiệu năng và an toàn:
- Bộ theo dõi con trỏ read-only chỉ hoạt động khi shell Desktop foreground và con trỏ thực sự nằm trên SysListView32, nên không thể bắt nhầm click trong popup hoặc phần mềm khác.
- Mỗi groupId chỉ có tối đa một popup; các lệnh mở trùng hủy ghost animation cũ và đưa form thật lên ngay. Popup ưu tiên gap 10 px phía dưới/trên tile; nếu không đủ chỗ dọc thì neo bên phải/trái tile, chỉ cascade nhẹ để tránh chồng lấn. Mỗi lần Desktop cache/quick-drop refresh, popup đang mở nhận bounds mới và tự đi theo tile collection.
- Icon được cache và mỗi lần scan chỉ lập chỉ mục file Desktop một lần, tránh chi phí duyệt thư mục lặp O(n²) khi nhiều collection mở.
- Nội dung thật được đặt ngay vào semantic state cuối. Hiệu ứng mở/phóng/thu dùng một overlay cố định trong suốt: chỉ snapshot di chuyển bên trong, không resize HWND/Region từng frame. Ghost click-through khoảng 130 ms và có thể bị request mới hủy; X/Escape đóng form thật ngay rồi phát ghost 90 ms thu về tile. Khởi tạo registry/tile/scan chạy nền STA sau khi message loop sẵn sàng.
- Không còn gửi LVM_SETITEMPOSITION theo index UI Automation, không phát UPDATEDIR/ASSOCCHANGED toàn Desktop khi cập nhật tile; chỉ gửi thông báo attributes/update cho đúng path để tránh làm icon không liên quan đổi vị trí.
- Quét UI Automation thường xuyên chỉ khi Desktop đang foreground; khi dùng ứng dụng khác, tiến trình nền ngủ và chủ động trả working set không dùng cho Windows.
- Smoke test trên máy build: 0.0000 giây CPU trong 8 giây khi Desktop không foreground; private bytes khoảng 38 MB, resident working set giảm còn khoảng 4 MB sau idle trim.
- Giao diện Settings card-based hiện đại cho phép chỉnh thanh trượt độ trễ gộp nhóm (Hover Delay), bật/tắt tự giải thể khi còn 1 item, giảm chuyển động (Reduce Motion) và khởi động cùng Windows.
- Không có status window thường trực; vòng đời dùng ApplicationContext + tray icon. Chạy EXE thủ công tạo phản hồi rõ ràng bằng cửa sổ Settings, còn Windows Startup dùng tham số --startup để chạy yên lặng.
- Keyboard: Tab có focus ring, Enter/Space mở app, F2 đổi tên, Ctrl+F tìm kiếm và Escape đóng. App card/toolbar có accessible name; item ghim có accessible description và marker vector.
- Menu tray gồm: trạng thái hoạt động, Settings..., và Exit. Thoát thông thường giữ nguyên bố cục virtual và các icon Desktop cho lần chạy sau.

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
