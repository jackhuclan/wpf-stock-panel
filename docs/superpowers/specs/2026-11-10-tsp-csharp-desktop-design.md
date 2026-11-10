# TSP·C# 桌面工作台 — 设计文档

- 日期：2026-11-10
- 复刻对象：`D:\repo\front\tick-stock-panel`（TSP，MIT，Python + React）
- 规范来源：`E:\wpf-float-agent`（`.editorconfig` / `AGENTS.md` / `Directory.Build.props` / `Directory.Packages.props`）
- 目标产物位置：`E:\wpf-stock-panel`
- 远端仓库：`https://github.com/jackhuclan/wpf-stock-panel.git`（已存在、公开、**当前为空仓库**，本地尚未 `git init`）

---

## 1. 意图与为什么

### 为什么做

验证一件事：**vibe coding 能否把一个项目从服务端到客户端真正完整地做完。**

这个目标决定了本文档的多数取舍：

- 判据不是页面数量或代码行数，而是**一条真实链路是否每层都落地并可运行**；
- 既然要"完整"，宁可窄而深，不要宽而空；
- 过程本身要被观察，因此阶段之间必须存在明确、可验收的中间状态。

### 为什么是 C#

服务端与客户端统一 C#，消除双语言栈的上下文切换。对 vibe coding 的吞吐而言这可能是关键变量，也是本次要验证的假设之一。

### 复刻对象的真实形态

TSP 是一套完整的双栈系统，不是前端项目：

| 层 | 参考实现 |
|---|---|
| 后端 | Python 3.11 / FastAPI / Pydantic v2 / APScheduler / sse-starlette |
| 计算 | Polars（指标、扫表）、vectorbt（部分回测路径） |
| 存储 | Parquet（数据）+ DuckDB（查询），零外部数据库 |
| 前端 | React 18 / Vite / TypeScript / Tailwind / TanStack Query / lightweight-charts / ECharts |
| 开放层 | 65 端点 Open API（六档 scope）、SSE 事件流、MCP 12 工具 |
| 规模 | 后端 770 文件 / 206,451 行；前端 431 文件 / 107,116 行；测试 457 文件 / 91,079 行 |

**规模结论：参考实现约 40 万行源码。本项目不对齐行数，只对齐链路的完整性。**

参考实现已有一个"桌面客户端"，但它只是 Inno Setup + PyInstaller 给同一套 Web UI 套壳（`packaging/tsp.iss`），并非原生客户端。

---

## 2. 目标 / 非目标 / 反目标

### 目标（做到即算做完）

1. **端到端链路真实存在**：数据源 → 落盘 → 指标 → 策略 → 回测 → 监控 → 界面，每层都是真实现，不是桩。
2. **19 个页面**全部在 Avalonia 客户端内可导航、可交互，视觉与参考截图一致。
3. **全栈 C#**：服务端 ASP.NET Core Web API，客户端 Avalonia，运行时不依赖 Python。
4. **真实数据可跑通**：无 API Key 也能经 TickFlow free-api 拉历史日 K，驱动看板 / 自选 / 指数 / 个股 / 策略 / 回测。
5. **每个阶段结束时停在"确实完整"的状态**——可以随时停下来而不留下半成品。

### 非目标（明确不做）

- 代码行数 / 文件数对齐参考实现。
- **付费档能力**：实时行情、分钟 K、五档盘口、竞价数据。free-api 不提供这些；界面照做，数据层留插件接口。
- **无免费源的页面**：概念分析 / 行业分析 / 财务分析（另属同花顺与付费财务源），以及盘中属性的 侧栏实时行情条 / 异动监控 / 模拟盘 / 持仓提醒。这些页面既不是"付费解锁"、也不做 fixture 凑数，而是走**随附种子数据集**（§5.9）：离线、可复现、有真实取数路径与真实数据形状，界面明确标注数据截至日与"非实时"。
  > 这是**我的判断**，非用户决定。理由：种子数据集让"真界面"背后有一条真的取数链路，而硬编码在 View 里的假数据正是 §2 反目标要排除的"壳工程"。
- **登录与鉴权**：单用户本地应用，**不做登录，不做引导向导**（已确认）。完成定义里的"配置数据源"落在设置页，用空状态引导。
- 生产级硬化：多用户、权限体系、审计、容器编排。
- 3000+ 测试的规模对齐；测试按阶段覆盖关键路径。
- 参考实现的页面插件运行时、声明式扩展页、源码扩展导入（`docs/page-sdk.md` 那一整套）。本项目的"扩展"以编译期注册为主。

### 反目标（技术上能跑也算失败）

- **界面被夹具定型**：19 页按 mock 数据的形状写完，切真后端时大面积塌掉。
- **壳工程**：页面能打开，点进去全是占位。
- **只有 UI 没有引擎**：策略页、回测页好看，但扫表和回测是假的。
- **文档与实现脱节**：文档宣称的能力在代码里不存在。

---

## 3. 约束

### 外部约束（不可协商）

- 客户端**只用 Avalonia 12.1.6**，不用 WPF（已确认）。
- MVVM 用 **CommunityToolkit.Mvvm**。
- 代码规范沿用 `wpf-float-agent`：Allman 大括号、文件作用域命名空间、命名空间与文件夹一致、私有/内部字段（含静态）一律 `_camelCase`（**不用 `s_` 前缀**）、常量 PascalCase、内置类型用关键字、CPM 集中包管理且 **csproj 禁止写 Version**、提交前 **0 警告 0 错误**。
- 数据源：TickFlow free-api（`https://free-api.tickflow.org`），无 Key 仅历史日 K。
- **标的清单（universe）**：免费源不提供市场标的清单，而自选、全市场扫描、回测都依赖它。走**随附种子清单 + 数据源插件接口**（§5.9）：种子给出全 A 的 symbol / 名称 / 交易所 / 资产类型，插件接口供将来接真实维表。
- **复权口径**：与参考实现一致——有除权因子则前复权，**无因子则退到原始价并显式记录当前口径**。免费档不提供 `adj_factor`，故默认路径是原始价。该口径会影响全部指标与回测结果，必须落库并在界面标注，不得静默。

### 由约束推出的技术后果（这些都要自己造）

| TSP 用 | C# 现状 | 本项目做法 |
|---|---|---|
| Polars | 无官方 .NET 绑定 | 自研列式帧：`double[]` / `int[]` 列 + 日期索引 + 向量化算子 |
| vectorbt | 无等价库 | 自研回测引擎（逐 bar 循环 + 指标/信号接口） |
| DuckDB | 有 .NET 绑定 | 直接用 DuckDB.NET |
| Parquet | 有 Parquet.Net | 直接用 Parquet.Net |
| TickFlow 官方 SDK | 仅 Python SDK | 按 HTTP 协议直连 free-api |
| lightweight-charts / ECharts | Avalonia 无对应 | **自绘图表控件**（见 §5.5） |
| `StockSharp.Xaml.Charting` 5.0.257 | TFM 仅 `net10.0-windows7.0`，依赖 `StockSharp.Xaml` / `Ecng.Xaml.DXRefs`，属 WPF 包族，许可证为 StockSharp EULA | **不可用，已排除** |

### 视觉约束

- **A 股语义色：红涨绿跌**（`bull` = 红 `#F04438`，`bear` = 绿 `#12B76A`），且仅用于价格与 K 线，不用于 UI 状态。
- 数字一律等宽（tabular-nums）。
- 暗色为默认主题，亮色可切换。
- 设计令牌从参考实现 1:1 移植（见 §5.3）。

---

## 4. 做法

三个候选：

**A. 垂直切片，一层层加宽**
按业务纵切（自选 → 指标+策略 → 看板 → 回测 → 监控），每片从数据获取到界面全部做真，做完一片再开下一片。风险最早暴露，每片结束都是完整状态；代价是初期看不到全貌，页面数量的增长慢。

**B. 后端先行**
先把 ASP.NET Core 的领域模型与 API 铺满（对齐参考实现的端点规模），再整体做 Avalonia 客户端。契约一次成型，客户端不用回头改；代价是很长时间看不到任何界面，而"做完了没有"这个信号来得最晚。

**C. 客户端先行，夹具驱动**
先按截图把页面铺出来，用夹具 provider 喂数据，之后再替换成真后端。视觉进度最快，早期就有可判断的产出；代价是界面可能按假数据的形状定型——这是它最大的风险。

### 选定：C

理由：本次要验证的判据正是"能否真正做完"。B 把这个判断推到最不划算的时间点；C 让早期就产出可检验的东西。**C 的风险用契约冻结对冲**，机制见 §5.2。

### C 的成立条件（不遵守就会塌方）

`Tsp.Contracts` 与 `ITspApiClient` 必须在铺页面前先冻结；夹具与真实现共用同一接口、同一 DTO；铺页面期间任何 DTO 变更都必须同时改接口与夹具，**不得只在 View 里临时凑字段**。

---

## 5. 已定的 how

### 5.1 解决方案结构

```
E:\wpf-stock-panel\
├─ Tsp.slnx
├─ .gitignore                       忽略 bin/ obj/ release/ data/ 与含密钥的配置
├─ .editorconfig                    自 wpf-float-agent 复制（1:1）
├─ Directory.Build.props            共享元数据（Product / Version / Authors / License / RepositoryUrl）
├─ Directory.Packages.props         CPM，唯一版本源
├─ AGENTS.md                        本项目开发规范
├─ README.md
├─ docs/superpowers/specs/          设计文档与实现计划
├─ src/
│  ├─ Tsp.Contracts/                纯 DTO + 枚举，无第三方依赖
│  ├─ Tsp.Server.Core/              域逻辑：数据源 / 存储 / 指标 / 策略 / 回测 / 监控
│  ├─ Tsp.Server/                   ASP.NET Core Web API 宿主
│  ├─ Tsp.Desktop.ViewModels/       ViewModel（不引用任何 View，可单测）
│  └─ Tsp.Desktop/                  Avalonia 启动壳 + Views + 主题 + 图表控件
└─ tests/
   ├─ Tsp.Contracts.Tests/
   ├─ Tsp.Server.Core.Tests/
   └─ Tsp.Desktop.ViewModels.Tests/
```

**依赖方向（强制，禁止反向）**

- `Tsp.Desktop → Tsp.Desktop.ViewModels → Tsp.Contracts`
- `Tsp.Server → Tsp.Server.Core → Tsp.Contracts`
- 客户端**不得**引用服务端程序集——否则夹具与真后端的可替换性失效。

### 5.2 契约冻结（C 的关键机制）

- `ITspApiClient` 定义在 `Tsp.Desktop.ViewModels/Services/`，方法签名只用 `Tsp.Contracts` 的 DTO。
- 两个实现：
  - `FixtureApiClient` — 读 `src/Tsp.Desktop/Fixtures/*.json`（P0/P1 使用）
  - `HttpApiClient` — 打 `Tsp.Server`（P2 起使用）
- P1 只注册夹具实现；P2 起按功能逐个切换，**同一接口、同一 DTO，View 不改一行**。
- **夹具必须自证身份**：`FixtureApiClient` 在界面固定位置显示"夹具数据"角标，避免把假数据当成真结果。

### 5.3 设计令牌移植

来源：`frontend/src/index.css`（CSS variables）+ `frontend/tailwind.config.ts`。
落点：`src/Tsp.Desktop/Themes/Tokens.axaml`（`ResourceDictionary`），亮/暗两套。

| 令牌 | 暗色（默认） | 亮色 |
|---|---|---|
| base | `#0A0A0B` | `#FAFAFA` |
| surface | `#18181B` | `#FFFFFF` |
| elevated | `#212126` | `#F4F4F5` |
| border | `#353539` | `#E4E4E7` |
| fg-primary | `#FAFAFA` | `#18181B` |
| fg-secondary | `#C4C4CB` | `#52525B` |
| fg-muted | `#8E8E96` | `#A1A1AA` |
| accent | `#3B82F6` | 同 |
| bull（涨） | `#F04438` | 同 |
| bear（跌） | `#12B76A` | 同 |
| warning | `#F79009` | 同 |
| danger | `#F04438` | 同 |

- 圆角：card 8 / btn 6 / input 4 / dialog 12
- 缓动：`cubic-bezier(0.16, 1, 0.3, 1)`
- 字体：sans 优先 `Inter` → `HarmonyOS Sans SC` → `PingFang SC`；mono 优先 `JetBrains Mono` → `IBM Plex Mono`
- 层次靠 1px 细边框区分，**暗色下不用阴影**

### 5.4 主窗口骨架

左侧栏（19 页导航 + 底部设置入口）+ 内容区 + 侧栏底部实时行情条（指数快照）+ 菜单未读徽标 + 右下 AI 悬浮球（P7 才有实体）。
导航用 `NavigationService`（路由键 + 返回栈），页面按路由键注册，与参考实现的 `/dashboard`、`/watchlist` 等路径保持同名以便对照。

### 5.5 图表：自绘

`src/Tsp.Desktop/Charts/` 下自研一组轻量控件（`Control` + `DrawingContext`）：

| 控件 | 用途 |
|---|---|
| `KLineChart` | K 线 + 成交量（看板、个股分析） |
| `LineChart` | 净值曲线、分时 |
| `BarChart` | 涨跌分布、连板梯队 |
| `RadarChart` | 情绪雷达 |
| `Heatmap` | 概念 / 行业热度 |
| `DonutChart` | 涨跌家数占比 |

共同基座：坐标轴与刻度、命中测试（hover 提示）、缩放平移、令牌化配色。
`ScottPlot.Avalonia`（5.1.59）仅登记在 CPM 作为后备，默认不启用。

### 5.6 页面清单（19）

| 域 | 页面 | 路由 |
|---|---|---|
| CORE | 看板 | `/dashboard` |
| CORE | 自选 | `/watchlist` |
| CORE | 策略 | `/screener` |
| CORE | 因子 | `/factors` |
| CORE | 回测 | `/backtest` |
| CORE | 市场环境 | `/regime` |
| CORE | 指数 | `/indices` |
| CORE | 数据 | `/data` |
| EXT | 个股分析 | `/stock-analysis` |
| EXT | 连板梯队 | `/limit-ladder` |
| EXT | 概念分析 | `/concept-analysis` |
| EXT | 行业分析 | `/industry-analysis` |
| EXT | 财务分析 | `/financials` |
| EXT | 监控中心 | `/monitor` |
| EXT | 异动监控 | `/abnormal` |
| EXT | 持仓提醒 | `/lots` |
| EXT | 模拟盘 | `/paper` |
| EXT | 信号库 | `/signals` |
| EXT | 复盘 | `/review` |

另有**设置**页，含 数据源 / AI 设置 / 实时监控 / 页面管理 / 网络设置 / 开放接口 / 定义目录 / 配置备份 / 系统设置 / 关于。
CORE / EXT 徽标沿用参考实现语义：CORE 为承载核心能力的页面，EXT 为可整体裁剪的页面。

### 5.7 阶段划分

**P0 — 骨架与契约**
解决方案与 CPM、`.editorconfig`、`AGENTS.md`、`Tsp.Contracts` 首批 DTO、`ITspApiClient`、Avalonia 主窗口骨架 + 19 路由占位 + 主题令牌 + 夹具角标。
*验收*：`dotnet build` 0 警告 0 错误；应用能启动；19 项可导航；暗/亮主题切换生效。

**P1 — 19 页视觉全铺（夹具驱动）**
实现顺序：看板 → 自选 → 指数 → 数据 → 策略 → 因子 → 回测 → 市场环境 → 个股分析 → 连板梯队 → 概念分析 → 行业分析 → 财务分析 → 监控中心 → 异动监控 → 持仓提醒 → 模拟盘 → 信号库 → 复盘 → 设置。含 §5.5 自绘图表控件。
*验收*：每页可交互（排序 / 筛选 / 弹窗 / 图表渲染），数据全部来自夹具且角标可见。

**P2 — 真数据源**
TickFlow free-api 客户端；随附种子数据集与标的清单（§5.9）；数据源插件接口（能力声明 + fail-closed 路由）；Parquet 落盘 + DuckDB 查询；复权口径落地；`HttpApiClient` 按功能逐项替换夹具。
*验收*：自选 / 看板 / 指数 / 个股页显示**真实历史日 K**；无网络时种子数据仍能驱动全部 19 页；复权口径在界面可见。

**P3 — 指标流水线**
列式帧；核心指标（MA / EMA / MACD / RSI / KDJ / ATR / BOLL / 量比 / 涨跌停 / 连板）；enriched 宽表落盘与读取。
*验收*：指标列可查，且与参考实现口径抽样比对一致。

**P4 — 策略引擎**
策略定义（声明式条件）、全市场扫描。
*验收*：策略页扫描返回真实结果。

**P5 — 回测引擎**
T+1 / 费用 / 滑点 / 止损 / 最大持仓；净值曲线 / 夏普 / 最大回撤 / 胜率 / 盈亏比。
*验收*：回测页产出真实净值曲线与交易明细。

**P6 — 监控与告警**
四类规则（策略 / 个股信号 / 价格涨跌 / 全市场异动）、AND/OR、冷却期、严重级别、落盘、SSE 推送、托盘通知。
**"实时"在这里的含义已重定义**：唯一真实数据源是收盘日 K，因此评估由**盘后管道触发**（参考实现默认 15:35 CST）。"实时到达"指**触发即推送**——评估完成的那一刻经 SSE 送达客户端，而不是盘中逐笔。盘中属性的页面（侧栏行情条 / 异动 / 模拟盘 / 持仓提醒）由种子数据集驱动并标注非实时。
*验收*：手动触发一次评估，规则命中产生持久记录，且该记录在客户端**推送到达**（非轮询发现）。

**P7 — 开放接口与 AI**
Open API 端点 + Token/scope、MCP 服务、AI 助手（OpenAI 兼容 + 工具调用）。
**对齐程度取最小代表集，不做参考实现的 65 端点对齐**（我的判断）：约 8–10 个端点覆盖日 K / 指数 / 总览 / 策略 / 告警 / 扩展表读写；两档 scope（读 / 写）；MCP 暴露 3 个工具。验证的是契约与鉴权**真实存在**，而非端点数量。
*验收*：外部脚本可调 API 并受 scope 限制；助手能取数并流式作答。

### 5.8 进程与启动模型

"一条命令启动服务端与客户端"与服务端不得被客户端引用这两件事有张力。解法：**客户端以子进程拉起服务端，经 loopback HTTP 通信**。

- 客户端启动时 spawn `Tsp.Server`，绑定 `127.0.0.1` 上的可用端口，端口由服务端回传（或客户端先探测空闲端口再传入）；
- 数据目录由**服务端独占**，客户端不直接读写文件；
- 客户端启动后轮询服务端 `/health`，未就绪时显示明确的启动中状态，不静默空转；
- 客户端退出时终止整棵子进程树；
- **开发模式**允许客户端跳过 spawn，连一个已在运行的服务端实例（便于单独调试服务端）；
- 依赖方向不变：客户端只通过 `ITspApiClient` / `HttpApiClient` 与服务端对话，程序集层面仍无引用。

### 5.9 种子数据集

解决"无免费源的页面"与"无网络环境"两个问题，**一份数据两用**。

- **内容**：全 A 标的清单（symbol / 名称 / 交易所 / 资产类型 / 上市日）、概念与行业成分、财务五表样例、指数日 K 样例、竞价与异动样例；
- **形态**：随附在服务端输出目录的 `seeds/`（Parquet 为主，清单类用 JSON），由**同一个数据源插件接口**暴露，与 free-api 实现并列——因此它走的是真实取数路径，不是 View 里的硬编码；
- **界面义务**：任何使用种子数据的页面必须显示**数据截至日**与"非实时"标注；
- **体积约束**：仓库保持轻量（目标 < 50 MB），必要时截取时间窗或缩减标的范围；
- **用途之二**：无网络时全部 19 页仍可驱动，测试也用它做确定性夹具。

### 5.10 完成定义

一条命令启动服务端与客户端（§5.8），然后在界面内完成：配置数据源 → 拉真实历史日 K → 算指标 → 配策略 → 扫描 → 回测 → 看净值曲线 → 配监控 → 触发评估 → 看到告警推送。**无需登录，全程无 Python 参与。**

---

## 6. 留给 builder 的

- 每个页面 DTO 的具体字段（本文档只定分层与边界；字段在实现该页时补齐，并同步回填夹具与种子数据集）。
- `Tsp.Contracts` 的命名细节与序列化配置。
- 自绘图表控件的内部结构与复用粒度。
- 服务端数据目录布局（建议与参考实现同构：`data/kline/`、`data/enriched/`、`seeds/`）。
- 种子数据集的选取范围与体积上限的落到实处的取舍（§5.9 只给了约束，没给清单）。
- 测试粒度与关键路径清单。
- `ScottPlot.Avalonia` 是否真正启用。
- 解决方案文件用 `.slnx` 还是 `.sln`。
- 产品显示名（暂用 `Tsp`，`Directory.Build.props` 的 `Product` 待定）。

---

## 7. 风险登记

| 风险 | 影响 | 缓解 |
|---|---|---|
| 夹具定型界面，切真后端塌方 | 高（直接命中反目标） | §5.2 契约冻结；夹具角标；P2 逐功能替换而非一次性 |
| free-api 协议在 C# 侧对不上 | 中（P2 阻塞） | P0/P1 期间用最小探针工程先打通一次取数 |
| 无 Polars，列式计算性能不达标 | 中 | 指标按需现算 + 进程内快照缓存；必要时用 DuckDB 承担聚合 |
| ~~Avalonia 12.1.6 与 net10.0 的兼容性~~ **已验证通过（P0 交付时，2026-11-10）** | 中（P0 阻塞）→ **已消解** | 四个 Avalonia 包的 TFM 均含 `net10.0`，本机 SDK 10.0.403 上整条路径真实跑通：restore → `dotnet build Tsp.slnx`（0 警告 0 错误）→ 启动 → 19 页导航 → 明暗切换，**无需回落 net9.0**。**相关事实**：`Avalonia.Diagnostics` **没有 12.x 版本**（截至 2026-11-10 最高为 11.3.23），故 P0 不引入它——不要再花时间找 12.x（AGENTS §11 同记） |
| 19 页视觉工作量被低估 | 中 | 先做看板 + 自选 + 设置三页确立组件库，其余页复用 |
| 种子数据被误当作实时数据 | 中（直接削弱可信度） | §5.9 强制标注数据截至日与"非实时"；种子实现与在线实现在界面上可区分 |
| 免费档拿不到 universe，自选/扫描/回测全部阻塞 | 高（P2/P4/P5 前置） | 种子清单先顶上（§5.9），插件接口留真实维表接入位 |
| 复权口径静默退化，指标与回测结果被误读 | 中 | 口径落库 + 界面标注（§3）；P3 抽样比对时**按口径分别比对** |
| "实时"期望与 EOD 现实错位 | 中 | P6 已重定义"实时 = 触发即推"（§5.7）；盘中属性页面标注非实时 |
