# T3PP Patch GUI

## Build & Run

### Prereqs
- .NET 8 SDK (desktop workload) on Windows.
- Visual Studio 2022 (optional) with .NET Desktop dev workload.

### Restore
```bash
dotnet restore PatchGUI/PatchGUI.csproj
```

### Debug build
```bash
dotnet build PatchGUI/PatchGUI.csproj -c Debug
```
Run: `PatchGUI/bin/Debug/net8.0-windows7.0/PatchGUI.exe`

### Release build
```bash
dotnet build PatchGUI/PatchGUI.csproj -c Release
```
Run: `PatchGUI/bin/Release/net8.0-windows7.0/PatchGUI.exe`

### Self-contained publish (no runtime needed)
```bash
dotnet publish PatchGUI/PatchGUI.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
Output: `PatchGUI/bin/Release/net8.0-windows7.0/win-x64/publish/PatchGUI.exe`

### Required runtime files
Keep alongside `PatchGUI.exe`:
- `PatchGUI.dll`, `PatchGUI.deps.json`, `PatchGUI.runtimeconfig.json`
- `T3ppNative.dll`
- `ICSharpCode.AvalonEdit.dll`, `Wpf.Ui.dll`, `Wpf.Ui.Abstractions.dll`
- `lang/` folder (`zh_CN.json`, `en_US.json`)

### Cleaning
```bash
dotnet clean PatchGUI/PatchGUI.csproj -c Debug
dotnet clean PatchGUI/PatchGUI.csproj -c Release
```

### Notes
- Tabs are hidden in Release unless you adjust `InitMode()` (uses `#if DEBUG`).
- Delete `bin/`, `obj/`, and `.vs/` before committing.

---

# 构建与运行

## 环境要求
- Windows 下安装 .NET 8 SDK（桌面工作负载）。
- Visual Studio 2022（可选），安装 .NET 桌面开发组件。

## 还原依赖
```bash
dotnet restore PatchGUI/PatchGUI.csproj
```

## Debug 构建
```bash
dotnet build PatchGUI/PatchGUI.csproj -c Debug
```
运行：`PatchGUI/bin/Debug/net8.0-windows7.0/PatchGUI.exe`

## Release 构建
```bash
dotnet build PatchGUI/PatchGUI.csproj -c Release
```
运行：`PatchGUI/bin/Release/net8.0-windows7.0/PatchGUI.exe`

## 自包含发布（无需预装运行时）
```bash
dotnet publish PatchGUI/PatchGUI.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
输出：`PatchGUI/bin/Release/net8.0-windows7.0/win-x64/publish/PatchGUI.exe`

## 运行所需文件
与 `PatchGUI.exe` 放在同一目录：
- `PatchGUI.dll`, `PatchGUI.deps.json`, `PatchGUI.runtimeconfig.json`
- `T3ppNative.dll`
- `ICSharpCode.AvalonEdit.dll`, `Wpf.Ui.dll`, `Wpf.Ui.Abstractions.dll`
- `lang/` 目录（`zh_CN.json`, `en_US.json`）

## 清理
```bash
dotnet clean PatchGUI/PatchGUI.csproj -c Debug
dotnet clean PatchGUI/PatchGUI.csproj -c Release
```

## 说明
- Release 下导航页（Tabs）默认被 `#if DEBUG` 隐藏，若需显示请修改 `InitMode()`。
- 提交前删除 `bin/`、`obj/`、`.vs/` 目录。
