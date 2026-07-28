# Phase 11 — Force I/O

**Status:** ⬜ Pending | **Phụ thuộc:** phase-02, phase-10 | **Nội dung BRIEF:** #15

> ⚠️ **Đây là tính năng có thể gây nguy hiểm vật lý.** Force ghi đè tín hiệu thật của phần cứng — dùng sai có thể làm hỏng máy hoặc gây tai nạn. UI phải làm cho việc "đang force" **không thể bỏ sót**.

---

## 🚨 ĐỌC TRƯỚC — quả mìn từ B-2

**KHÔNG làm Force layer dạng decorator bọc quanh `IMemoryImage`** nếu Task 00.3 (phase-00) chưa hoàn tất.

Lý do: cả 5 driver hiện ép kiểu `memoryImage is not MemorySnapshot snapshot` và **im lặng trả về** khi ép kiểu hỏng. Một decorator `ForcedMemoryImage : IMemoryImage` sẽ khiến **mọi driver ngừng đọc/ghi mà không báo gì** — băng tải đứng yên, không exception, không log, kỹ sư không biết tại sao.

```csharp
// ❌ CẤM khi driver còn ép kiểu
class ForcedMemoryImage : IMemoryImage { ... }   // driver sẽ im lặng bỏ qua

// ✅ An toàn: Force nằm TRONG MemorySnapshot
class MemorySnapshot : IMemoryImage, IForceLayer { ... }
```

**Điều kiện tiên quyết:** DoD của phase-00 *"cả 5 driver bỏ hẳn ép kiểu"* phải tick xong. Kiểm tra:

```powershell
Select-String -Path "Drivers\*\*.cs" -Pattern "is not MemorySnapshot"   # phải RỖNG
```

Thiết kế dưới đây đặt Force **bên trong** `MemorySnapshot` chính vì lý do này.

---

## Task 11.1 — Tầng Force trong Memory Image

**File:** `src/DBI.Controller.Core/Models/MemorySnapshot.cs`

Force là một **lớp phủ** đứng trên cùng. Thứ tự ưu tiên khi đọc/ghi:

```
Đọc Input :  Force?  → InputSnapshot
Ghi Output:  Force?  → giá trị force thắng, logic bị bỏ qua
```

```csharp
public interface IForceLayer {
    void SetForce(string tag, object value);
    void ClearForce(string tag);
    void ClearAllForces();
    bool IsForced(string tag);
    IReadOnlyDictionary<string, object> GetAllForces();
}
```

`MemorySnapshot` triển khai `IForceLayer`. Áp dụng force:
- **Sau** `SwapInputBuffers()` — logic thấy giá trị đã force
- **Sau** `SwapOutputBuffers()` — driver nhận giá trị đã force

⚠️ Force **phải sống sót** qua `ClearAllOutputs()` của SafetyCatch? **KHÔNG.** Khi SafetyCatch kích hoạt (fault), **xoá toàn bộ force** và đưa output về safe state. An toàn thắng tiện lợi.

## Task 11.2 — Force qua IPC

`ForceTag` / `GetForceList` (đã định nghĩa contract ở phase-02).

Force **không** được lưu vào `.dbiproj` — nó là trạng thái runtime tạm thời, không phải cấu hình project. Deploy mới → xoá hết force.

## Task 11.3 — Force Table

```
Force table                                    ⚠️ 3 TAG ĐANG BỊ FORCE
┌───┬──────────────┬────────┬──────────────┬───────────────┬──────────┐
│ 🔒│ Name         │ Type   │ Monitor value│ Force value   │          │
├───┼──────────────┼────────┼──────────────┼───────────────┼──────────┤
│ ☑ │ StartButton  │ Bool   │ ● TRUE       │ TRUE          │ [Clear]  │
│ ☑ │ SensorProduct│ Bool   │ ● FALSE      │ FALSE         │ [Clear]  │
│ ☑ │ Temperature  │ Real   │   85.0       │ 85.0          │ [Clear]  │
└───┴──────────────┴────────┴──────────────┴───────────────┴──────────┘
                                       [ ⛔ CLEAR ALL FORCES ]
```

## Task 11.4 — Chỉ báo an toàn (bắt buộc)

Force đang bật thì **không được phép bỏ sót**:

| Vị trí | Chỉ báo |
|---|---|
| Status bar | Banner đỏ `#C72C3B` nhấp nháy: **⚠️ 3 TAG ĐANG BỊ FORCE** |
| Project Tree | Node Force Table có badge số lượng |
| Watch Table | Dòng bị force có icon 🔒 + nền hồng nhạt |
| Đóng Studio | Dialog cảnh báo: *"3 tag vẫn đang bị force và sẽ TIẾP TỤC bị force sau khi đóng Studio. Xoá force trước khi đóng?"* → `[Xoá force rồi đóng]` `[Đóng, giữ force]` `[Huỷ]` |

## Task 11.5 — Xác nhận khi tạo force

```
┌──────────────────────────────────────────────┐
│ ⚠️  CẢNH BÁO AN TOÀN                          │
│                                              │
│ Force "ConveyorRun" = TRUE sẽ ghi đè logic   │
│ chương trình và điều khiển TRỰC TIẾP thiết   │
│ bị vật lý.                                   │
│                                              │
│ Đảm bảo khu vực máy an toàn trước khi tiếp.  │
│                                              │
│  ☐ Tôi xác nhận khu vực máy đã an toàn       │
│                                              │
│              [ Huỷ ]  [ Force ]              │
└──────────────────────────────────────────────┘
```

Nút `Force` chỉ bật khi đã tick. **Không** có tuỳ chọn "đừng hỏi lại".

---

## Definition of Done

### Checkpoint 2026-07-28

- [x] `IForceLayer` nằm trong `MemorySnapshot`, không dùng decorator.
- [x] Force override input/output cho Bool, Int, Real.
- [x] Runtime IPC ForceTag/GetForceList đã nối; deploy/fault clear force.
- [x] Force regression test pass.
- [ ] Force Table UI, safety confirmation dialog/banner và reconnect UX đang tiếp tục triển khai.

- [ ] ✅ Điều kiện tiên quyết: `Select-String "is not MemorySnapshot" Drivers\*\*.cs` ra **rỗng** (DoD phase-00)
- [ ] Force nằm **trong** `MemorySnapshot`, **không** làm decorator bọc `IMemoryImage`
- [ ] `IForceLayer` hoạt động cho cả 3 kiểu dữ liệu
- [ ] Force áp dụng đúng cả chiều đọc Input và ghi Output
- [ ] Test: force Output → driver nhận giá trị force, **không** phải giá trị logic tính ra
- [ ] Test: force Input → logic đọc giá trị force, không phải giá trị driver đọc về
- [ ] **Test an toàn:** SafetyCatch kích hoạt → xoá toàn bộ force + output về safe state
- [ ] **Test an toàn:** Deploy mới → xoá toàn bộ force
- [ ] Force không được ghi vào `.dbiproj`
- [ ] Force Table hiển thị đúng danh sách, Clear từng cái và Clear All hoạt động
- [ ] Banner đỏ ở status bar hiện khi có force, biến mất khi hết
- [ ] Watch Table đánh dấu 🔒 dòng bị force
- [ ] Dialog xác nhận có checkbox bắt buộc; không có "đừng hỏi lại"
- [ ] Đóng Studio khi còn force → cảnh báo với 3 lựa chọn rõ ràng
- [ ] Mất kết nối Studio → force **vẫn giữ nguyên** ở Runtime (đúng ngữ nghĩa PLC), Studio hiện lại đúng khi nối lại
