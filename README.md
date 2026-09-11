# Kumo.H.NotifyIcon.Reactor

用 [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) 实现的、独立于 Reactor 原生托盘实现的一组 tray icon 组件库。

**一句话概括**：调用方式对齐 [microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) **v0.1.0-preview.15** 的托盘 API（`TrayIconSpec` / `ReactorTrayIcon` / `ReactorApp.OpenTrayIcon` / `UseTrayIcon`），而实现完全托底在 H.NotifyIcon 上；托盘菜单不再沿用 preview15 的自绘 flyout，改为直接用 H.NotifyIcon 的原生菜单系统。本库自身的编译依赖是 NuGet 上的 **Microsoft.UI.Reactor 0.1.0-preview.14**。

## 版本基线

| 项目 | 值 |
| --- | --- |
| API 参照 | microsoft-ui-reactor `v0.1.0-preview.15`（commit `f17ce2dd`），`src/Reactor/Hosting/Shell/TrayIconSpec.cs`、`ReactorTrayIcon.cs`，`src/Reactor/Core/RenderContext.cs#UseTrayIcon` |
| NuGet 依赖 | `Microsoft.UI.Reactor` **0.1.0-preview.14** |
| 托盘后端 | `H.NotifyIcon.WinUI` 2.5.0-dev.2 |
| 目标框架 | `net10.0-windows10.0.26100.0` |

## 为什么会有这个库

Reactor 的原生 tray 是自研的 Win32 `Shell_NotifyIcon` 实现（隐藏窗口、回调分发、flyout 宿主窗口都在框架内部）。本库保留相同的调用体验，但：

- 图标注册/隐藏/更新、Explorer 重启后的自动恢复、多图标 Id 管理交给 H.NotifyIcon；
- 菜单用 H.NotifyIcon 的原生菜单系统（`PopupMenu` / `SecondWindow`），菜单内容用 Reactor 的菜单 DSL（`MenuItem()` / `MenuSeparator()`）描述、由本库转换——不再实现 preview15 的自绘 flyout 宿主窗口；
- 除菜单外调用方代码与 preview15 同形，将来要迁回原生 API（或反向迁移）只需要替换 `HNotifyApp` → `ReactorApp`、`HNotifyTrayIconSpec` → `TrayIconSpec`。

## 快速开始

```csharp
using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit; // 纯托盘/托盘常驻应用必须显式控制退出

ReactorApp.Run(_ =>
{
    // 让 Explicit 真正生效：关掉 WinUI 默认的“最后一个窗口关闭即退出”
    Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

    var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(
        WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "app.ico")),
        "My App",
        WindowKey.Of("main-tray"))
    {
        ContextMenu = HNotifyMenu.Create(          // Reactor 菜单 DSL → H.NotifyIcon 原生菜单
            MenuItem("打开主窗口", OpenMainWindow),
            MenuSeparator(),
            MenuItem("退出", () => ReactorApp.Exit())),
        MenuActivation = HNotifyMenuActivation.RightClick,
    });

    tray.Click += (_, _) => OpenMainWindow();
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
            EventHandler onClick = (_, _) => OpenMainWindow();
            tray.Click += onClick;
            return () => tray.Click -= onClick;
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
        EventHandler onClick = (_, _) => OpenMainWindow();
        tray.Click += onClick;
        return () => tray.Click -= onClick;
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
| `WindowIcon.FromBytes(bytes)`（preview15 新增） | `HNotifyWindowIcon.FromBytes(byte[] / Stream)` | 临时替身；返回 `System.Drawing.Icon`，可直接传给 spec；仅支持 `.ico` 字节 |
| —（preview15 无原生菜单） | `HNotifyTrayIconSpec.ContextMenu` + `HNotifyMenu.Create(Reactor 菜单 DSL)` + `MenuActivation / ContextMenuMode / ContextMenuTheme` | HNotify 扩展：直接用 H.NotifyIcon 自带菜单，菜单内容就是 Reactor 的 `MenuItem()`/`MenuSeparator()`，见差异第 9 条 |
| — | `HNotifyTrayIcon.ShowContextMenu(point)` / `CloseContextMenu()` | 手动显示/关闭原生菜单（配合 `MenuActivation = None`）；PopupMenu 模式下为阻塞调用 |
| `ReactorApp.OpenTrayIcon(spec)` | `HNotifyApp.OpenTrayIcon(spec)` | 同样的 UI 线程校验、同样的失败回滚（注销 + dispose + 重抛） |
| `ReactorApp.TrayIcons` | `HNotifyApp.TrayIcons` | copy-on-write 快照，任意线程可读 |
| `ReactorApp.FindTrayIcon(key)` | `HNotifyApp.FindTrayIcon(key)` | |
| `ReactorApp.TrayIconOpened/Closed` | `HNotifyApp.TrayIconOpened/Closed` | 事件参数为 `HNotifyTrayIcon` |
| `ReactorTrayIcon.Id/Key/Spec/Icon/Tooltip/IsVisible` | `HNotifyTrayIcon` 同名成员 | `Icon` 为 `WindowIcon?`，另有 `DrawingIcon` |
| `ReactorTrayIcon.Update(spec)` | `HNotifyTrayIcon.Update(spec)` | 只重放发生变化的字段；`Update` 在已释放时抛 `ObjectDisposedException` |
| `Click / DoubleClick / RightClick` | 同名事件 | 见下方“点击语义” |
| `ShowFlyout(Element)` / `HideFlyout()` | **不提供**（该链路已移除） | 托盘弹出统一用 H.NotifyIcon 原生菜单，见差异第 5、9 条 |
| `Close()` / `Dispose()` | 同名 | 幂等 |
| `Component.UseTrayIcon(spec)` | `HNotifyComponent.UseTrayIcon(spec)`（protected） | 见“组件模式” |
| `RenderContext.UseTrayIcon(spec)` | `HNotifyRenderContextExtensions.UseTrayIcon(context, spec)` | 扩展方法，调用写法一致 |

## 与 preview15 原生托盘实现的差异（务必阅读）

1. **内存图标**：preview15 新增 `WindowIcon.FromBytes` / `FromRgba`，preview14 没有这些 API。本库提供同名形态的临时替身
   `HNotifyWindowIcon.FromBytes(bytes | stream)`（返回可直接传入 spec 的 `System.Drawing.Icon`），迁移时只换类名：

   ```csharp
   // 现在（preview14 + 本库）
   new HNotifyTrayIconSpec(HNotifyWindowIcon.FromBytes(iconBytes), "My App", key);

   // 迁到 preview15 后
   new TrayIconSpec(WindowIcon.FromBytes(iconBytes), "My App", key);
   ```

   该替身仅支持 `.ico` 字节（后端是 `System.Drawing.Icon`）；其它内存来源仍可用
   `HNotifyTrayIconSpec.FromDrawingIcon(icon, tooltip, key, isVisible)`（或 `System.Drawing.Icon` 构造），
   icon 由调用方持有、库内部克隆后交给 H.NotifyIcon 管理。
2. **资源图标**：`WindowIcon.FromResource("ms-appx:///...")` 会尽力解析为磁盘文件（未打包应用查
   `AppContext.BaseDirectory`，打包应用查 `Package.Current.InstalledLocation`），解析失败则跳过图标加载并写
   `Debug` 诊断——与原生行为一致（通知区域需要真实 HICON）。
3. **关闭策略**：Reactor preview14/15 的 `ShutdownPolicy` 只统计 Reactor 自己的窗口/托盘；本库的图标不在它的统计里，
   托盘常驻请用 `ShutdownPolicy.Explicit` + 托盘菜单调用 `ReactorApp.Exit()`。
   **但 `Explicit` 必须配合 WinUI 的平台开关才真正生效**：WinUI 默认
   `Application.DispatcherShutdownMode = OnLastWindowClose`——最后一个 XAML 窗口关闭时 XAML runtime 会
   `PostQuitMessage` 结束消息循环（与 Reactor 策略无关，`Explicit` 也拦不住）。应用启动时补一行即可
   （Windows App SDK 1.8+；示例中已演示）：

   ```csharp
   Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
   ```

   之后关掉最后一个窗口进程仍会驻留，只有 `ReactorApp.Exit()` 结束进程；可选但推荐再叠加“关闭即隐藏”
   （保留窗口实例、恢复更快）：

   ```csharp
   opened.Closing += (_, e) =>
   {
       if (e.Reason != WindowCloseReason.UserClosed) return; // 让退出/级联关闭正常走
       e.Cancel = true;
       opened.Hide();                                        // 收进托盘
   };
   ```

   另外：只要进程里还有**任何活着（哪怕隐藏）的应用窗口**，同样不会触发平台退出（例如 ComponentSample 的隐藏托盘宿主窗口，或像 `ReactorTrayIcon.Customize` 启动时持有的隐藏窗口）——这是副作用，不要依赖。
4. **多图标**：H.NotifyIcon 用 Guid 区分同一进程内的多个图标。带有 `Key` 的 spec 会把 Key 映射为稳定的
   CustomName/Guid；**同一进程内开多个图标时请为每个图标提供不同的 `Key`**。
5. **无 Reactor flyout**：不再实现 preview15 的 `ShowFlyout(Element)` / `HideFlyout()`（自绘 flyout 宿主窗口）。托盘弹出统一用 H.NotifyIcon 的原生菜单（见第 9 条）；要在托盘旁弹任意 Reactor 内容面板，请自建窗口实现（本库不提供）。
6. **点击语义**：单击 → `Click`；双击 → `Click`（一次）+ `DoubleClick`。H.NotifyIcon 会抑制双击尾随的抬起事件，
   而原生实现会触发两次 `Click` + 一次 `DoubleClick`。单击不会被“双击等待时间”延迟。
7. **线程模型**：所有 mutator（含菜单相关属性与 `ShowContextMenu` / `CloseContextMenu`）必须在 Reactor UI 线程调用，否则抛 `InvalidOperationException`。
8. **图标更新**：`Update`/属性 setter 只在图标源引用变化时重新加载；`System.Drawing.Icon` 每次都会克隆，
   调用方保留所有权。
9. **菜单（HNotify 扩展）**：preview15 的托盘 API 没有原生菜单概念，本库把 H.NotifyIcon 自带菜单系统作为统一方案，菜单内容直接复用 **Reactor 自己的菜单 DSL**：

   ```csharp
   var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(icon, "My App", key)
   {
       ContextMenu = HNotifyMenu.Create(          // ← 吃 Reactor 的 MenuItem()/MenuSeparator()
           MenuItem("打开主窗口", OpenMainWindow),
           MenuSeparator(),
           MenuItem("退出", () => ReactorApp.Exit())),
       MenuActivation = HNotifyMenuActivation.RightClick,
   });
   ```

   相关成员：`MenuActivation`（默认 `RightClick`；`None` = 只手动显示）、`ContextMenuMode`、`ContextMenuTheme`、
   `CloseContextMenuOnItemClick`，以及 `HNotifyTrayIcon.ShowContextMenu(point)` / `CloseContextMenu()`。要点：
   - `HNotifyMenu.Create(...)` 接受 `MenuItem()` / `MenuSeparator()` / `MenuSubItem()` / `ToggleMenuItem()` / `RadioMenuItem()`，
     转换成 H.NotifyIcon 需要的 `MenuFlyout`；菜单项动作走 `ICommand`（`PopupMenu` 模式下 H.NotifyIcon 只执行 `Command`，不会触发 `Click`）；
   - 图标会转换（`MenuItem(..., icon: "Home")` 的 `Symbol` 名/字形，或 `IconData`），但只有 `SecondWindow` 模式会显示（原生弹出菜单不带图标）；快捷键 / AccessKey / Description 不参与转换；
   - `ToggleMenuItem` / `RadioMenuItem` 只映射初始勾选状态，回调仅在 `SecondWindow` 模式生效；
   - `PopupMenu`（默认）= Win32 原生弹出菜单，`ShowContextMenu` 在菜单关闭前不返回（阻塞）；
   - `SecondWindow` = H.NotifyIcon 用独立窗口承载 `MenuFlyout`（进程会多一个窗口，退出判定要多留意）；
     ⚠️ 实测：**反复创建/销毁图标**时 `SecondWindow` 模式每轮会残留 ~60KB 托管内存（H.NotifyIcon 预览模式的机制），需要反复重建的托盘请用 `PopupMenu` 模式（实测平稳，但菜单项只剩文本）。

## 构建

```powershell
# 整个解决方案（库 + 示例；示例会按平台/主机 CPU 自动选择 win-arm64 / win-x64）
dotnet build .\Kumo.H.NotifyIcon.Reactor.slnx

# 只构建库
dotnet build .\src\Kumo.H.NotifyIcon.Reactor\Kumo.H.NotifyIcon.Reactor.csproj

# 单独构建示例（也可以显式指定平台）
dotnet build .\samples\HNotifyTrayIcon.Sample\HNotifyTrayIcon.Sample.csproj -p:Platform=ARM64

# 组件方式示例（独立单元 + PopupMenu 原生菜单）
dotnet build .\samples\HNotifyTrayIcon.ComponentSample\HNotifyTrayIcon.ComponentSample.csproj -p:Platform=ARM64

# 第二个 sample 的 ARM64 Native AOT 发布（EnableMsixTooling 走 MSIX 工具链，
# 规避 WinAppSDK #6394 的 .pri 丢失问题；AOT 运行与自动退出已验证正常）
dotnet publish .\samples\HNotifyTrayIcon.ComponentSample\HNotifyTrayIcon.ComponentSample.csproj -c Release -p:Platform=ARM64

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
│  ├─ HNotifyWindowIcon.cs            # 对齐 preview15 WindowIcon.FromBytes（临时替身）
│  ├─ HNotifyMenu.cs                  # H.NotifyIcon 原生菜单（HNotify 扩展）
│  ├─ HNotifyApp.cs                   # 对齐 ReactorApp 的托盘注册表
│  ├─ HNotifyComponent.cs             # 对齐 Component.UseTrayIcon
│  ├─ HNotifyRenderContextExtensions.cs
│  ├─ HNotifyIconResolver.cs          # WindowIcon / System.Drawing.Icon → HICON
│  └─ HNotifyThreading.cs             # UI 线程校验
├─ samples/HNotifyTrayIcon.Sample/           # 程序式注册示例（原生菜单：默认 PopupMenu，--menu-second-window 切窗口托管模式）
└─ samples/HNotifyTrayIcon.ComponentSample/  # 独立单元示例：托盘（隐藏宿主 + UseTrayIcon）与主窗口互不依赖、可反复重建（PopupMenu 菜单）
```
