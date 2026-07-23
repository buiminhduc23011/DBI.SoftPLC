# Plan: DBI.Controller (Soft PLC Runtime in C#/.NET)
Created: 2026-07-23 22:30  
Status: 🟡 In Progress  
Brief: [docs/BRIEF.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/docs/BRIEF.md)

## Overview
DBI.Controller là một **Soft PLC Runtime** viết bằng C#/.NET 8+, đóng vai trò như một Automation Runtime trên PC/Edge Device. Logic điều khiển của người dùng được viết bằng C# thuần trên Object IO trừu tượng, decouple hoàn toàn khỏi phần cứng và driver truyền thông.

## Tech Stack
- **Framework:** .NET 8.0 C#
- **Host Engine:** Microsoft.Extensions.Hosting / Custom High-Precision Scan Loop
- **Plugin Loading:** System.Runtime.Loader (`AssemblyLoadContext`)
- **Protocol Drivers:** Modbus TCP (`NModbus` / `FluentModbus`), Factory I/O SDK, Simulation Driver
- **Studio UI (Phase 06):** WPF / Avalonia UI (Cross-platform)
- **Testing:** xUnit, FluentAssertions, Moq

## Phases Overview

| Phase | Name | Status | Progress | Output Files |
|---|---|---|---|---|
| 01 | Solution Setup & Architecture Bootstrap | ✅ Complete | 100% | Solution `.sln`, `.csproj` projects |
| 02 | Core Interfaces & SDK Primitives | ⬜ Pending | 0% | `DBI.Controller.Core`, `DBI.Controller.SDK` |
| 03 | Runtime Scan Engine & Memory Image | ⬜ Pending | 0% | `DBI.Controller.Runtime` |
| 04 | MVP Protocol Drivers Implementation | ⬜ Pending | 0% | `Driver.Simulation`, `Driver.Modbus`, `Driver.FactoryIO` |
| 05 | Testing Harness & Sample Projects | ⬜ Pending | 0% | `DBI.Controller.Testing`, `Sample.Conveyor` |
| 06 | Diagnostics & Studio Configuration GUI | ⬜ Pending | 0% | `DBI.Controller.Diagnostics`, `DBI.Controller.Studio` |

## Quick Commands
- Start Phase 1: `/code phase-01`
- Check progress: `/next`
- Technical Design (DB/API/Architecture): `/design`
- Save context: `/save-brain`
