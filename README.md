# Tsp — A 股量化研究桌面工作台

C# / Avalonia 桌面客户端，目标是复刻 [shy3130/tick-stock-panel](https://github.com/shy3130/tick-stock-panel)
（TSP，MIT 许可，Python + React）这套 A 股量化研究系统。本仓库是**复刻实现**，不是参考项目的一部分，也不与其共享代码。

- 复刻对象：[shy3130/tick-stock-panel](https://github.com/shy3130/tick-stock-panel)（TSP，MIT 许可；后端 Python + FastAPI + Polars + DuckDB，前端 React；本机上另有一份克隆在 `D:\repo\front\tick-stock-panel`）
- 设计文档（唯一权威）：[`docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md`](docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md)
- 当前版本：`0.1.0`（`Directory.Build.props` 的 `<Version>`，唯一版本源）
- 目标运行环境：.NET 10 / 桌面端为 Avalonia（**不使用 WPF**）

---

## 当前阶段：P0 已完成，P1–P7 未开始

阶段划分见 spec §5.7。**只有 P0 落地了**；P1–P7（19 页视觉全铺、真数据源、指标流水线、策略引擎、
回测引擎、监控告警、开放接口与 AI）**均未开工**，本仓库现在没有对应的代码。

### P0 实际交付了什么

| 能力 | 落点 |
|---|---|
| 构建基线 | `dotnet build Tsp.slnx` → 0 警告 0 错误；CPM 集中包管理 |
| 冻结的数据契约 | `src/Tsp.Contracts`：纯 DTO（`Instruments` / `Market` / `Watchlist`），不引用任何包 |
| API 契约与夹具实现 | `ITspApiClient` + `FixtureApiClient`（读本地 JSON）；`HttpApiClient` 属 P2 |
| 可导航的壳 | 侧栏 19 页 + 底部「设置」入口；未注册的路由键停在**空状态**，不崩、不白屏（界面目前没有可达该状态的入口，这是路由表自身的保证，由单元测试覆盖） |
| 页面内容 | **只有占位页**：显示页名、路由键与核心/扩展徽标。P1 才做真实页面 |
| 主题 | 暗色为默认，亮色可切；颜色全部走 `Themes/Tokens.axaml` 的令牌，运行中切换实时生效 |
| 夹具角标 | 侧栏顶部常驻「夹具数据 · 非真实行情」，可见性恒等于客户端的自报身份 |
| 指数条 | 侧栏底部四只核心指数的点位与涨跌幅，**红涨绿跌** |
| 取数失败态 | 夹具读不到时显示可读错误，不抛异常穿透到 UI |

### P0 没有交付什么（别期待）

没有服务端（`Tsp.Server` / `Tsp.Server.Core` 属 P2，尚未创建），没有真实数据源，
没有指标计算、策略引擎、回测、监控告警、Open API 与 AI 助手。
19 个页面**只有导航与占位**，没有图表、没有交互、没有筛选与弹窗。
P0 交付的是「能启动、能导航、契约已冻结、界面由夹具驱动」的骨架。

---

## 界面上的数据全部是夹具数据

所有数据都来自随源码提交的 JSON 夹具：`src/Tsp.Desktop/Fixtures/`
（`index-quotes.json` / `instruments.json` / `watchlist.json` / `daily-bars/600519.SH.json`）。
它们是**离线样例**（手写的示例数字），不是从任何行情源抓来的，也**不是实时数据**。

界面为此在侧栏顶部常驻「夹具数据 · 非真实行情」角标：角标是否显示由
`ITspApiClient.IsFixtureData` 决定（`FixtureApiClient` 恒为 `true`），不由视图按数据形状猜——
这样 P2 换成真实现时，角标会随客户端身份一起消失，而不是靠人去记得删。

---

## 构建、测试与运行

前置：**.NET SDK 10**（本仓库在 SDK 10.0.403 上开发与验证；全部项目 TFM 为 `net10.0`）。

```bash
dotnet build Tsp.slnx                  # 0 警告 0 错误是提交基线
dotnet test                            # 跑全部测试
dotnet test tests/Tsp.Contracts.Tests  # 跑单个测试项目
dotnet run --project src/Tsp.Desktop   # 启动桌面壳（跑起来才算验证，见下）
```

测试分布在两个项目里，与被测项目一一对应：

| 项目 | 覆盖 |
|---|---|
| `tests/Tsp.Contracts.Tests` | DTO 的序列化往返、缺字段取默认值、版本从 `Directory.Build.props` 传导 |
| `tests/Tsp.Desktop.ViewModels.Tests` | 路由注册表、导航服务与返回栈、夹具客户端、主窗口 ViewModel 的状态与失败路径 |

**UI 与主题效果不靠单元测试**：ViewModel 层刻意不引用 Avalonia，因此可直接构造并单测；
而界面外观（主题是否实时切换、角标是否看得清、长中文页名是否撑破布局）必须在真实
`dotnet run` 里核对。P0 刻意不引入 `Avalonia.Headless.XUnit`（其 12.1.6 依赖 `xunit.v3`，
会让测试栈分裂）。

## P0 的验收定义

spec §5.7 给 P0 定的验收：`dotnet build` **0 警告 0 错误**；应用能启动；19 项可导航；
暗/亮主题切换生效。逐条展开即：

1. `dotnet build Tsp.slnx` → 0 警告 0 错误；
2. `dotnet test` → 全部通过；
3. `dotnet run --project src/Tsp.Desktop` 后：
   - 窗口以**暗色**打开；
   - 侧栏 19 项逐条可导航，每项都落在对应的占位页上；
   - 底部「设置」入口可用（设置页在 spec §5.6 的 19 页表格之外）；
   - 「夹具数据 · 非真实行情」角标可见且读得清；
   - 指数条显示四只指数，且**上涨为红、下跌为绿**；
   - 未注册的路由键会停在空状态（界面目前没有可达该状态的入口，这是路由表自身的保证，由单元测试覆盖）；
   - 「切换主题」在两套主题间来回切换，侧栏、内容区与分隔线一并换色。

---

## 仓库结构

```
Tsp.slnx
├─ src/Tsp.Contracts/           纯 DTO 类库（net10.0）：不引用任何包
├─ src/Tsp.Desktop.ViewModels/  路由注册表、导航服务、API 契约与夹具实现（不引用任何 View / Avalonia）
├─ src/Tsp.Desktop/             Avalonia 启动壳：Views / Themes / Fixtures
├─ tests/*.Tests/               xunit 测试，与被测项目一一对应
├─ Directory.Build.props        共享工程元数据（版本 / 产品 / 仓库地址）
├─ Directory.Packages.props     NuGet 包版本集中管理（CPM）
├─ NuGet.config                 固定还原源为 nuget.org 并配套包源映射
├─ AGENTS.md                    项目开发规范（改代码前先读）
└─ docs/superpowers/            spec 与实施计划
```

**依赖方向（强制，禁止反向）**：`Tsp.Desktop → Tsp.Desktop.ViewModels → Tsp.Contracts`。
客户端不得引用服务端程序集——否则 P2 用真客户端替换夹具的可替换性就失效了。

---

## 文档

| 文档 | 路径 | 说明 |
|---|---|---|
| 设计文档（spec） | `docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md` | **唯一权威**：目标/非目标/反目标、约束、已定的 how、阶段划分、风险登记 |
| P0 实施计划 | `docs/superpowers/plans/2026-11-10-p0-skeleton-and-contracts.md` | 本阶段的逐任务计划（P1–P7 各自单独成篇，写到该阶段前再产出） |
| 开发规范 | `AGENTS.md` | 依赖方向、命名、CPM、构建基线、验证习惯、提交与发版流程 |

## 代码规范来源

`.editorconfig` 的主体自 `E:\wpf-float-agent\.editorconfig` 逐字复制。
其中 `[*.axaml]` 段是**有意追加**的：参考项目是 WPF 项目、没有 axaml 这个概念，
所以该段与源文件不一致是预期结果，不是漂移。详见 `AGENTS.md` §6.1。

## 许可证

**尚未选定**。本仓库目前不带 `LICENSE` 文件——选哪个许可证由仓库所有者决定，尚未作出。
