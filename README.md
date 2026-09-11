# Kumo.H.NotifyIcon.Reactor

用 [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) 实现的、独立于 Reactor 原生托盘实现的一组 tray icon 组件库。

**一句话概括**：调用方式 1:1 对齐 [microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) **v0.1.0-preview.15** 的托盘 API（`TrayIconSpec` / `ReactorTrayIcon` / `ReactorApp.OpenTrayIcon` / `UseTrayIcon`），而实现完全托底在 H.NotifyIcon 上；本库自身的编译依赖是 NuGet 上的 **Microsoft.UI.Reactor 0.1.0-preview.14**。

## 版本基线

| 项目 | 值 |
| --- | --- |
| API 参照 | microsoft-ui-reactor `v0.1.0-preview.15`（commit `f17ce2dd`），`src/Reactor/Hosting/Shell/TrayIconSpec.cs`、`ReactorTrayIcon.cs`、`TrayFlyoutHostWindow.cs`，`src/Reactor/Core/RenderContext.cs#UseTrayIcon` |
| NuGet 依赖 | `Microsoft.UI.Reactor` **0.1.0-preview.14** |
| 托盘后端 | `H.NotifyIcon.WinUI` 2.5.0-dev.2 |
| 目标框架 | `net10.0-windows10.0.26100.0` |

## 为什么会有这个库

Reactor 的原生 tray 是自研的 Win32 `Shell_NotifyIcon` 实现（隐藏窗口、回调分发、flyout 宿主窗口都在框架内部）。本库保留完全相同的调用体验，但：

- 图标注册/隐藏/更新、Explorer 重启后的自动恢复、多图标 Id 管理交给 H.NotifyIcon；
- flyout 内容仍由 Reactor 自己的 `ReactorHost` 渲染；
- 调用方代码与 preview15 完全同形，将来要迁回原生 API（或反向迁移）只需要替换 `HNotifyApp` → `ReactorApp`、`HNotifyTrayIconSpec` → `TrayIconSpec`。

## 快速开始

```csharp
using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit; // 纯托盘/托盘常驻应用必须显式控制退出

ReactorApp.Run(_ =>
{
    var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(
        WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "app.ico")),
        "My App",
        WindowKey.Of("main-tray")));

    tray.Click += (_, _) => OpenMainWindow();
    tray.RightClick += (_, _) => tray.ShowFlyout(
        VStack(4,
            Button("打开主窗口", OpenMainWindow),
            Button("退出", () => ReactorApp.Exit()))
        .Width(240)
        .Padding(8));
});
```

组件的用法（对应 preview15 的 `Component.UseTrayIcon` / `RenderContext.UseTrayIcon`）：

```csharp
// 类组件：从 HNotifyComponent 派生即可获得 protected UseTrayIcon
sealed class TrayHost : HNotifyComponent
{
    public override Element Render()
    {
        var tray = UseTrayIcon(new HNotifyTrayIconSpec(
            WindowIcon.FromPath(iconPath), "Component Tray", WindowKey.Of("component-tray")));

        UseEffect(() =>
        {
            if (tray is null) return () => { };
            EventHandler onRightClick = (_, _) => tray.ShowFlyout(menu);
            tray.RightClick += onRightClick;
            return () => tray.RightClick -= onRightClick;
        }, tray ?? (object)"no-tray");

        return TextBlock("Tray ready");
    }
}

// 函数组件：RenderContext 扩展方法（调用写法与 preview15 的实例方法一致）
Element View(RenderContext context)
{
    var tray = context.UseTrayIcon(new HNotifyTrayIconSpec(
        WindowIcon.FromPath(iconPath), "Function Tray", WindowKey.Of("function-tray")));

    context.UseEffect(() =>
    {
        if (tray is null) return () => { };
        EventHandler onRightClick = (_, _) => tray.ShowFlyout(menu);
        tray.RightClick += onRightClick;
        return () => tray.RightClick -= onRightClick;
    }, tray ?? (object)"no-tray");

    return TextBlock("Tray ready");
}
```

> 注意：Reactor 的 Hook 分析器（`REACTOR_HOOKS_005`）要求 `UseTrayIcon` 这类调用位于 `Component.Render` 覆写、
> 或以 `Use` 开头的自定义 Hook 方法内。函数组件里如果把它写在普通方法中，请包一层 `Use*` 帮助方法（或按需抑制该告警）。

## API 对照表

| Reactor preview15 | Kumo.H.NotifyIcon.Reactor | 备注 |
| --- | --- | --- |
| `TrayIconSpec(Icon, Tooltip, Key, IsVisible)` | `HNotifyTrayIconSpec(Icon, Tooltip, Key, IsVisible)` | 位置参数与默认值一致；额外提供 `System.Drawing.Icon` 构造与 `FromDrawingIcon` 工厂 |
| `ReactorApp.OpenTrayIcon(spec)` | `HNotifyApp.OpenTrayIcon(spec)` | 同样的 UI 线程校验、同样的失败回滚（注销 + dispose + 重抛） |
| `ReactorApp.TrayIcons` | `HNotifyApp.TrayIcons` | copy-on-write 快照，任意线程可读 |
| `ReactorApp.FindTrayIcon(key)` | `HNotifyApp.FindTrayIcon(key)` | |
| `ReactorApp.TrayIconOpened/Closed` | `HNotifyApp.TrayIconOpened/Closed` | 事件参数为 `HNotifyTrayIcon` |
| `ReactorTrayIcon.Id/Key/Spec/Icon/Tooltip/IsVisible` | `HNotifyTrayIcon` 同名成员 | `Icon` 为 `WindowIcon?`，另有 `DrawingIcon` |
| `ReactorTrayIcon.Update(spec)` | `HNotifyTrayIcon.Update(spec)` | 只重放发生变化的字段；`Update` 在已释放时抛 `ObjectDisposedException` |
| `Click / DoubleClick / RightClick` | 同名事件 | 见下方“点击语义” |
| `ShowFlyout(Element) / HideFlyout()` | 同名方法 | 进程内单飞窗，内容尺寸自适应 |
| `Close()` / `Dispose()` | 同名 | 幂等 |
| `Component.UseTrayIcon(spec)` | `HNotifyComponent.UseTrayIcon(spec)`（protected） | 见“组件模式” |
| `RenderContext.UseTrayIcon(spec)` | `HNotifyRenderContextExtensions.UseTrayIcon(context, spec)` | 扩展方法，调用写法一致 |

## 与 preview15 原生托盘实现的差异（务必阅读）

1. **内存图标**：preview15 新增 `WindowIcon.FromBytes` / `FromRgba`，preview14 没有这些 API。本库用
   `HNotifyTrayIconSpec.FromDrawingIcon(icon, tooltip, key, isVisible)`（或 `System.Drawing.Icon` 构造）覆盖这个场景，
   icon 由调用方持有、库内部克隆后交给 H.NotifyIcon 管理。
2. **资源图标**：`WindowIcon.FromResource("ms-appx:///...")` 会尽力解析为磁盘文件（未打包应用查
   `AppContext.BaseDirectory`，打包应用查 `Package.Current.InstalledLocation`），解析失败则跳过图标加载并写
   `Debug` 诊断——与原生行为一致（通知区域需要真实 HICON）。
3. **关闭策略**：Reactor preview14 的 `ShutdownPolicy` 只统计 Reactor 自己的窗口/托盘；本库的图标不在它的统计里。
   托盘常驻应用请使用 `ShutdownPolicy.Explicit` 并在菜单里调用 `ReactorApp.Exit()`。
4. **多图标**：H.NotifyIcon 用 Guid 区分同一进程内的多个图标。带有 `Key` 的 spec 会把 Key 映射为稳定的
   CustomName/Guid；**同一进程内开多个图标时请为每个图标提供不同的 `Key`**。
5. **Flyout**：进程内单实例宿主窗口（与原生一致，任意时刻只能显示一个托盘 flyout）；位置跟随光标、尺寸按内容
   测量自适应（原生是固定 280×360）；点击外部或按 Esc 关闭。
6. **点击语义**：单击 → `Click`；双击 → `Click`（一次）+ `DoubleClick`。H.NotifyIcon 会抑制双击尾随的抬起事件，
   而原生实现会触发两次 `Click` + 一次 `DoubleClick`。单击不会被“双击等待时间”延迟。
7. **线程模型**：与 preview15 一致，所有 mutator（含 `ShowFlyout`）必须在 Reactor UI 线程调用，否则抛
   `InvalidOperationException`。
8. **图标更新**：`Update`/属性 setter 只在图标源引用变化时重新加载；`System.Drawing.Icon` 每次都会克隆，
   调用方保留所有权。

## 构建

```powershell
# 整个解决方案（库 + 示例；示例会按平台/主机 CPU 自动选择 win-arm64 / win-x64）
dotnet build .\Kumo.H.NotifyIcon.Reactor.slnx

# 只构建库
dotnet build .\src\Kumo.H.NotifyIcon.Reactor\Kumo.H.NotifyIcon.Reactor.csproj

# 单独构建示例（也可以显式指定平台）
dotnet build .\samples\HNotifyTrayIcon.Sample\HNotifyTrayIcon.Sample.csproj -p:Platform=ARM64

# 打包
dotnet pack .\src\Kumo.H.NotifyIcon.Reactor\Kumo.H.NotifyIcon.Reactor.csproj -c Release
```

## 目录结构

```text
Kumo.H.NotifyIcon.Reactor/
├─ Kumo.H.NotifyIcon.Reactor.slnx
├─ src/Kumo.H.NotifyIcon.Reactor/
│  ├─ HNotifyTrayIconSpec.cs          # 对齐 preview15 TrayIconSpec
│  ├─ HNotifyTrayIcon.cs              # 对齐 preview15 ReactorTrayIcon（H.NotifyIcon 后端）
│  ├─ HNotifyApp.cs                   # 对齐 ReactorApp 的托盘注册表
│  ├─ HNotifyComponent.cs             # 对齐 Component.UseTrayIcon
│  ├─ HNotifyRenderContextExtensions.cs
│  ├─ HNotifyFlyoutHostWindow.cs      # ReactorHost 渲染的 flyout 宿主窗口
│  ├─ HNotifyIconResolver.cs          # WindowIcon / System.Drawing.Icon → HICON
│  └─ HNotifyThreading.cs             # UI 线程校验
└─ samples/HNotifyTrayIcon.Sample/    # preview15 同形调用示例
```
