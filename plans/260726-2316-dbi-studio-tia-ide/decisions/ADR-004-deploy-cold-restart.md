# ADR-004: Deploy dùng Cold Restart, thiết kế sẵn đường cho Hot Reload

**Date:** 2026-07-26 | **Status:** ✅ Accepted

## Context

[BRIEF.md §5](../../../docs/BRIEF.md) hứa **Hot Reload** ở Phase 2: *"Tự động bù DLL mới biên dịch, Swap Assembly không cần Restart Runtime"*. Câu hỏi là làm ngay hay để sau.

Hot Reload thực sự khó vì ba lý do:

1. **State của Function Block.** FB giữ trạng thái trong field (`Ton._elapsed`, bộ đếm sản phẩm...). Swap assembly nghĩa là tạo instance mới — state về 0. Muốn giữ phải serialize state cũ → deserialize vào instance mới, mà class có thể đã đổi cấu trúc.
2. **Memory leak.** `PluginLoadContext` đã `isCollectible: true` ([UserProgramLoader.cs:12](../../../src/DBI.Controller.Runtime/Loading/UserProgramLoader.cs)) nhưng chỉ cần **một** reference còn sót (event handler, static field, closure) là context cũ không giải phóng được. Swap 50 lần trong một ngày làm việc → 50 assembly nằm lại trong RAM.
3. **Khó chẩn đoán.** Máy đang chạy, swap xong hành vi lạ — không biết do code mới sai hay do state migrate sai.

## Decision

**v1: Cold Restart.** Nhưng tách interface để cắm Hot Reload sau mà không phá gì.

```
COLD RESTART (v1)
  Stop → ClearAllOutputs → ghi output an toàn xuống thiết bị
       → UnloadProgram → Load assembly mới → OnStart() → Start
  ⏱ gián đoạn ~200–500ms, máy dừng hẳn, output = 0
```

```csharp
public interface IProgramSwapper {
    Task<SwapResult> SwapAsync(byte[] assembly, SwapMode mode, CancellationToken ct);
}

public enum SwapMode { ColdRestart, HotReload }

// v1
public class ColdRestartSwapper : IProgramSwapper { }
// để sau
public class HotReloadSwapper  : IProgramSwapper { }
```

`RuntimeHost` chỉ biết `IProgramSwapper`, không biết cách swap cụ thể. `DeployRequest` mang thêm trường `SwapMode` — v1 Runtime từ chối `HotReload` kèm thông báo *"Chưa hỗ trợ trong phiên bản này"*.

**Điều kiện tiên quyết để sau này làm được Hot Reload** — thiết kế v1 phải tôn trọng ngay:
- `ControllerProgram` **không** giữ reference tới object của Runtime ngoài `IMemoryImage`
- `IOContainer` chỉ gọi vào `IMemoryImage`, không cache gì
- Không đăng ký static event từ user assembly sang Runtime
- Mọi state của FB nằm trong **field của instance**, không dùng `static`

## Consequences

### Tích cực
- v1 đơn giản, dễ hiểu, dễ test, ít đường code
- Deploy là hành động có chủ ý — người dùng chấp nhận máy dừng vài trăm ms
- State FB về 0 sau deploy là hành vi **dễ đoán**, tránh được cả một lớp bug "state cũ lẫn code mới"
- Không phải làm lại `RuntimeHost` khi thêm Hot Reload

### Tiêu cực
- Máy dừng khi deploy — không dùng được cho dây chuyền không được phép ngắt
- Output về 0 trong lúc chuyển đổi: băng tải dừng, van đóng. **Phải cảnh báo người dùng trước** (phase-07 Task 07.3)
- Bộ đếm, timer về 0 sau mỗi lần deploy

### Ràng buộc kéo theo cho phase-02
- `RuntimeHost` dùng `IProgramSwapper`, không gọi `UserProgramLoader` trực tiếp
- `DeployRequest` có trường `SwapMode`, v1 chỉ chấp nhận `ColdRestart`
- Trình tự Stop **bắt buộc**: `ClearAllOutputs()` → `SwapOutputBuffers()` → `WriteOutputsAsync()` **rồi mới** unload. Xả output xuống thiết bị thật trước, không chỉ xoá trong bộ nhớ.
