# Phase 01: Solution Setup & Architecture Bootstrap
Status: ⬜ Pending  
Dependencies: None  

## Objective
Thiết lập cấu trúc C# Solution (`DBI.Controller.sln`) chuẩn .NET 8.0, định hình các project class library, console app, và thiết lập cấu hình chung (Build Props, Code Style, Target Framework).

## Requirements
### Functional
- [ ] Khởi tạo `.gitignore` chuẩn cho .NET / C#.
- [ ] Tạo Solution `DBI.Controller.sln`.
- [ ] Khởi tạo cấu trúc các thư mục: `src/`, `Drivers/`, `Samples/`, `tests/`, `docs/`.

### Non-Functional
- [ ] Sử dụng **.NET 8.0 SDK**.
- [ ] Áp dụng `Directory.Build.props` chung cho toàn bộ solution để đồng bộ `TargetFramework=net8.0`, `Nullable=enable`, `ImplicitUsings=enable`.

## Implementation Steps
1. [ ] Tạo `.gitignore` và `Directory.Build.props` tại root directory.
2. [ ] Tạo `src/DBI.Controller.Core/DBI.Controller.Core.csproj` (Class Library).
3. [ ] Tạo `src/DBI.Controller.SDK/DBI.Controller.SDK.csproj` (Class Library).
4. [ ] Tạo `src/DBI.Controller.Runtime/DBI.Controller.Runtime.csproj` (Console / Host App).
5. [ ] Tạo `src/DBI.Controller.Diagnostics/DBI.Controller.Diagnostics.csproj` (Class Library).
6. [ ] Tạo `src/DBI.Controller.Testing/DBI.Controller.Testing.csproj` (Class Library).
7. [ ] Tạo `Drivers/DBI.Controller.Driver.Simulation/DBI.Controller.Driver.Simulation.csproj`.
8. [ ] Tạo `Drivers/DBI.Controller.Driver.Modbus/DBI.Controller.Driver.Modbus.csproj`.
9. [ ] Tạo `Drivers/DBI.Controller.Driver.FactoryIO/DBI.Controller.Driver.FactoryIO.csproj`.
10. [ ] Link toàn bộ project vào solution `DBI.Controller.sln`.
11. [ ] Chạy `dotnet build` xác nhận build thành công 100%.

## Files to Create/Modify
- `.gitignore` - Bỏ qua bin/obj/.vs
- `Directory.Build.props` - Set .NET 8.0 config chung
- `DBI.Controller.sln` - Solution file
- `src/DBI.Controller.Core/DBI.Controller.Core.csproj`
- `src/DBI.Controller.SDK/DBI.Controller.SDK.csproj`
- `src/DBI.Controller.Runtime/DBI.Controller.Runtime.csproj`
- `src/DBI.Controller.Diagnostics/DBI.Controller.Diagnostics.csproj`
- `src/DBI.Controller.Testing/DBI.Controller.Testing.csproj`
- `Drivers/DBI.Controller.Driver.Simulation/DBI.Controller.Driver.Simulation.csproj`
- `Drivers/DBI.Controller.Driver.Modbus/DBI.Controller.Driver.Modbus.csproj`
- `Drivers/DBI.Controller.Driver.FactoryIO/DBI.Controller.Driver.FactoryIO.csproj`

## Test Criteria
- [ ] Chạy `dotnet build` không lỗi compiler hay warning nghiêm trọng.
- [ ] Cấu trúc thư mục khớp 100% với kiến trúc đã thống nhất trong `BRIEF.md`.

---
Next Phase: [phase-02-core-sdk.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-02-core-sdk.md)
