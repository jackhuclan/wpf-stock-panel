# AGENTS.md — Tsp 项目通用开发规范

面向 AI 编码助手与本仓库开发者的项目约定。**改动代码前请先读完本文件**；
详细风格规则以 `.editorconfig` 为准，本文件只提炼必须遵守的高频约定。

---

## 1. 项目概览

| 项 | 内容 |
|---|---|
| 产品 | **Tsp**：A 股量化研究桌面工作台（单用户本地应用） |
| 技术栈 | .NET 10 / C# / Avalonia 12.1.6 客户端 + ASP.NET Core Web API 服务端（P2 起） |
| 分层 | `Tsp.Desktop`（Avalonia 壳）→ `Tsp.Desktop.ViewModels`（可脱离 UI 单测）→ `Tsp.Contracts`（纯 DTO） |
| 复刻对象 | `D:\repo\front\tick-stock-panel`（TSP，MIT，Python + React） |
| 设计文档 | `docs/superpowers/specs/2026-11-10-tsp-csharp-desktop-design.md`（**spec，唯一权威**） |
| 实施计划 | `docs/superpowers/plans/`（按阶段成篇，P0 为 `2026-11-10-p0-skeleton-and-contracts.md`） |
| 仓库 | `github.com/jackhuclan/wpf-stock-panel`，主分支 `main` |
| 当前阶段 | P0（骨架与契约）进行中 |

**当前仓库状态**：`Tsp.Contracts` 与 `Tsp.Contracts.Tests` 已存在；
`Tsp.Desktop.ViewModels` 由 P0 的 Task 3 创建，`Tsp.Desktop` 由 Task 6 创建；
服务端的 `Tsp.Server` / `Tsp.Server.Core` **属 P2，P0 不建**。

---

## 2. 工程结构与依赖方向

```
Tsp.slnx
├─ src/Tsp.Contracts/           纯 DTO 类库（net10.0）：不引用任何包，不依赖任何本项目程序集
├─ src/Tsp.Desktop.ViewModels/  路由注册表、导航服务、API 客户端（net10.0，不引用任何 View/Avalonia）
├─ src/Tsp.Desktop/             Avalonia 启动壳（WinExe）：Program/App 只做 DI 装配，禁止写业务逻辑
├─ tests/*.Tests/               xunit 测试，与被测项目一一对应
├─ Directory.Build.props        全解决方案共享工程元数据（版本/产品/仓库地址）
├─ Directory.Packages.props     NuGet 包版本集中管理（CPM）
├─ NuGet.config                 固定还原源与包源映射（见 §4）
├─ .editorconfig                格式与命名规则（权威）
└─ docs/superpowers/            spec 与 plan
```

**依赖方向（强制，禁止反向）**：`Tsp.Desktop → Tsp.Desktop.ViewModels → Tsp.Contracts`。

- **客户端不得引用服务端程序集**，服务端不得引用客户端程序集；
- `Tsp.Contracts` 是最底层，**不引用任何 NuGet 包**，保持纯净；
- ViewModel 层**不得**引用 Avalonia —— 它必须能被单元测试直接构造，这是 P0 全部契约可测的前提。

---

## 3. 版本与工程元数据（Directory.Build.props）

- `<Version>` 是**唯一版本源**，同时作用于 AssemblyVersion / FileVersion / NuGet 包版本；发布升级只改这一处；
- 当前版本 **0.2.2**，语义化版本，**分档**：每次改动（含**子计划**收口）→ 补丁位 +1；**阶段性计划**收口（P0/P1/P2…）→ 次版本 +1、补丁位归零；破坏性变更 → 主版本 +1；
- `Deterministic=true`、`IncludeSourceRevisionInInformationalVersion=true`（文件属性可见 git 提交号）、`GenerateDocumentationFile=false`，不要随意改动；
- `Product`、`Description`、`RepositoryUrl` / `PackageProjectUrl` 集中在此维护，**csproj 内不得重复定义**这些全局属性；
- `tests/Tsp.Contracts.Tests/BaselineTest.cs` 钉住了版本从本文件传导到程序集这条链路——改 `<Version>` 时它会失败，这是**预期行为**，同步更新断言即可。

## 4. NuGet 包管理（Directory.Packages.props，CPM）

- `ManagePackageVersionsCentrally=true`、`CentralPackageVersionOverrideEnabled=false`：
  **所有包版本统一在 `Directory.Packages.props` 维护**；
- csproj 中 `PackageReference` 只写 `Include`，**禁止写 `Version`**（写了会直接报 NU1008）；
- 新增依赖：先在 `Directory.Packages.props` 对应分组（`MVVM 与 DI` / `Avalonia` / `测试`）登记版本，再在 csproj 引用；
- 引入 preview 包需在登记处注明原因，升级正式版后移除注释；
- **TFM 一律 `net10.0`**：不得引入只支持 `net10.0-windows` 的包（如 `StockSharp.*` 为 WPF，客户端禁止引用）；
- 客户端**不得使用 WPF**；
- `NuGet.config` 固定还原源为 nuget.org 并配套包源映射：本仓库全部依赖都是公开包，
  固定单一源既消除 CPM 下的 NU1507，也保证任意机器（含 CI）还原结果一致。
  **不要把本机私有源写进这个文件**。

## 5. 构建与测试

```bash
dotnet build Tsp.slnx                  # 0 警告 0 错误是提交基线
dotnet test                            # 跑全部测试
dotnet test tests/Tsp.Contracts.Tests  # 跑单个测试项目
dotnet run --project src/Tsp.Desktop   # 本地调试运行（P0 Task 6 起可用）
```

- **`dotnet build Tsp.slnx` 必须 0 警告 0 错误**——这是每个任务的验收底线，不接受"只是警告"；
- 警告要**改代码或改工程设置**来消除，**不得**为消警告而放宽 `.editorconfig`（有异议先讨论）；
- 全部测试通过是提交前提；每个任务的测试目标数写在对应 plan 里。

---

## 6. 代码风格（提炼自 .editorconfig）

### 6.1 格式化

- UTF-8、文件末尾保留换行、去除行尾空白；
- 缩进：C# / Markdown 4 空格；csproj / props / axaml / xml / yaml 2 空格；
- **`.editorconfig` 的出处与 `[*.axaml]` 段的来历**：文件主体自 `E:\wpf-float-agent\.editorconfig`
  逐字复制，而其中 `[*.axaml]` 段是**随后有意追加**的——参考仓库是 WPF 项目，没有 axaml 这个概念。
  因此那段与源文件不一致是**预期结果**，不是复制走神、也不是漂移；比对两份文件时不要据此判定复制不认真
  （`.editorconfig` 文件内同一位置也留了这句话）；
- 大括号独立成行（Allman：`csharp_new_line_before_open_brace = all`）；
- 命名空间用**文件作用域**（`csharp_style_namespace_declarations = file_scoped`），
  且命名空间与文件夹路径一致（`dotnet_style_namespace_match_folder`）；
- `using` 置于命名空间之外，`System.*` 排前；隐式全局 using 覆盖的 `System.*` 不要重复写；
  提交前清理多余 using（IDE0005）。

### 6.2 命名约定（本仓库强制）

| 对象 | 规则 |
|---|---|
| 私有/内部实例字段 | `_camelCase` |
| 私有/内部静态字段 | 同样 `_camelCase`（**不用 `s_` 前缀**，本仓库特有约定） |
| 常量字段 | `PascalCase` |
| 接口 | `I` 前缀 |
| 类型/方法/属性 | `PascalCase` |
| 局部变量/参数 | `camelCase` |

### 6.3 语言偏好

- 内置类型用关键字（`string`/`int`），不用 `String`/`Int32`；
- 不用 `this.` 限定成员；`var` 仅在类型显而易见时可用；
- 优先：模式匹配、`is null` 判空、`??`、`?.`、switch 表达式、对象/集合初始化器、显式元组名；
- 修饰符顺序：`public private protected internal static extern new virtual abstract sealed override readonly unsafe volatile async`；
- 分析器级别已调优（IDE0005/IDE0130 = suggestion，CS0219/CS0168 = warning，CA1050/CA1812 关闭）。

### 6.4 提交前自检清单

- [ ] `dotnet build Tsp.slnx` **0 警告 0 错误**；
- [ ] `dotnet test` 全绿；
- [ ] 多余 `using` 已清理；
- [ ] 新依赖已登记 CPM（且 csproj 里没写 `Version`）；
- [ ] 无调试遗留（临时钩子、假端点配置必须还原/删除）；
- [ ] 行为改动已真实运行验证（见 §9）。

---

## 7. 设计与数据契约约定

- **DTO 一律 `sealed record` + 位置参数**，放 `Tsp.Contracts`，按域分子目录（`Instruments/`、`Market/`、`Watchlist/`…），
  命名空间与文件夹一致；
- **价格与金额用 `decimal`**（A 股 2 位 / ETF 3 位小数）；**成交量用 `long`**；
  计算层（P3 起）内部可用 `double`，**只在契约边界用 `decimal`**；
- 契约 DTO 的枚举**不给越界校验**（`System.Text.Json` 默认接受未定义数值枚举），
  合法性校验属 P2 的 API 边界职责；枚举**不挂** `JsonStringEnumConverter`，保持数字线号；
- DTO 需要容忍缺字段时，给位置参数**默认值**（如 `string Name = ""`），使缺字段取默认而非抛异常；
  由此引出的约束：**默认值必须是合法的业务默认**（如 `AssetType.Stock` 必须是 0 号成员）；
- JSON 序列化统一用 `JsonSerializerDefaults.Web`；
- **契约一旦冻结不得由 View 反推修改**——`ITspApiClient` 的签名在 P2 会被真实的 `HttpApiClient` 实现。

### 7.1 界面语义

- **颜色语义：红涨绿跌**——`bull` = `#F04438`、`bear` = `#12B76A`，
  且**仅用于价格与 K 线**，不得用于 UI 状态（成功/失败/警告另用 `AccentBrush` / `WarningBrush` / `DangerBrush`）；
- 数字一律用**等宽字形**；
- **暗色为默认主题**，亮色可切；颜色必须走 `Themes/Tokens.axaml` 的 `ResourceDictionary.ThemeDictionaries`
  （Dark / Light 两组键名**完全一致**），**View 内不得硬编码色值**——否则运行中切换主题不会实时生效；
- 层次靠 1px 边框，**暗色下不用阴影**；
- 长中文页名用 `TextTrimming="CharacterEllipsis"`，不得撑破布局。

---

## 8. MVVM 与 DI 约定

- ViewModel 统一用 CommunityToolkit.Mvvm：`[ObservableProperty]` / `[RelayCommand]`；
  **命令的 `CanExecute` 依赖属性必须挂 `[NotifyCanExecuteChangedFor]`**，否则按钮状态不刷新（高频坑）；
- DI 注册集中在 `App.axaml.cs`，**只做装配，不写业务逻辑**：

| 服务 | 生命周期 |
|---|---|
| `INavigationService` | Singleton |
| `ITspApiClient` | Singleton（夹具目录取 `AppContext.BaseDirectory/Fixtures`） |
| `MainWindowViewModel` / `MainWindow` | Singleton |

- 启动时 `Directory.SetCurrentDirectory(AppContext.BaseDirectory)`，运行期资源路径以 exe 目录为准；
- **异常不得穿透到 UI**：可预期的数据层失败（夹具缺失、JSON 损坏）要捕获后写入 ViewModel 的
  `ErrorMessage` 状态，界面显示可读错误而不是崩溃或白屏；
- **捕获要分层，不能只 `catch (TspApiException)`**，三层各有各的理由：
  `OperationCanceledException` 照常上抛（契约要求——取消是"用户主动中止"，不是失败）；
  `TspApiException` 用其自身面向用户的消息；其余异常兜底成一条可读消息。
  `OutputType=WinExe` 没有控制台，漏出去的异常不会打印任何东西，只表现为**进程凭空消失**，
  因此"契约说实现只抛那一种异常"不构成省掉兜底的理由（见 `MainWindowViewModel.LoadAsync`）；
- 导航到未注册的路由键应停在明确的空状态（`CurrentRoute == null`），**不抛异常**。

---

## 9. 验证习惯（重要）

- **每个行为改动都要真实运行验证**，不要只靠编译通过和单元测试；
- 单元测试只覆盖 ViewModel 层（它不依赖 Avalonia，可脱离 UI 运行）；
  **UI 与主题效果靠真实运行验证**：`dotnet run --project src/Tsp.Desktop`，抓截图核对；
  P0 阶段刻意**不引入** `Avalonia.Headless.XUnit`（其 12.1.6 依赖 `xunit.v3`，会使测试栈分裂）；
- 夹具/配置类失败路径要**真的造出来验一遍**（如把 `Fixtures/` 临时改名后重启），**验完必须改回**；
- 验证用的临时钩子（环境变量、调试入口）验证完必须删除。

---

## 10. Git 与提交规范

### 10.1 提交信息

- **中文 Conventional Commits**：`feat(scope): …` / `fix(scope): …` / `refactor(scope): …` /
  `test(scope): …` / `style(scope): …` / `chore(scope): …` / `docs: …`；
- **涉及代码改动的提交**在描述末尾带版本尾注 `（0.2.2）`，与 `Directory.Build.props` 的 `<Version>` 一致；
- scope 常用值：`scaffold` / `contracts` / `nav` / `ui` / `data` 等；
- **每个功能/修复单独成提交**；文档与对应代码改动同批提交；
  纯文档改动用 `docs: …`，**版本尾注可带可不带**——带上便于与代码提交对齐版本，不带也让 `docs: …` 一眼可辨；
  本仓库两种都有过（早期 `docs:` 提交不带，本轮 P0 交付几个 `docs:` 带了），**不强制其一**。

### 10.2 分支与推送

- 主分支 `main`，提交前确认工作区干净、构建通过、测试全绿；
- **推送必须由用户明确确认**——远端是公开仓库，push 是对外动作，**不得自行 push**；
- `.gitignore` 必须生效：`bin/`、`obj/`、`.vs/`、`release/`、`artifacts/`、`data/`、`*.user` 不入库；
- **含密钥/Token 的本地配置绝不入库**（`config.json`、`appsettings.Local.json`、`.env`、`secrets.json` 已忽略），
  仓库只提供 `*.example.json` 模板；
- `docs/` 是仓库内容，**不得**被忽略。

### 10.3 版本与提交（**改动与子计划升补丁位，阶段性计划升次版本**）

**分档，别混：**

| 情形 | 动作 | 例 |
|---|---|---|
| **每次改动**（功能、修复、元数据、文档，各自都算） | 补丁位 +1 | `0.2.1` → `0.2.2` |
| **子计划收口**（如 P1 拆出的 P1-A / P1-B / P1-C） | 补丁位 +1 | `0.2.2` → `0.2.3` |
| **阶段性计划收口**（P0、P1、P2 … 整个阶段做完） | 次版本 +1、补丁位归零 | `0.2.3` → `0.3.0` |
| 破坏性变更 | 主版本 +1 | `0.3.1` → `1.0.0` |

**不是只有发版才升版**；但**次版本只留给阶段性计划的收口**——子计划收口走补丁位，不要往次版本上跳。

阶段收口常与最后一个子计划的收口同时发生（P1-C 做完即 P1 做完）：此时按**阶段**处理，升次版本。

每个版本号落地要同步四处：

1. 升 `Directory.Build.props` 的 `<Version>`；
2. 同步 `BaselineTest` 的版本断言（它钉的就是 `<Version>` 的传导，不锁它测试会红）；
3. 同步 `README.md` 的「当前版本」行与本节上方第 5 节的「当前版本」；
4. 同一批提交包含代码 + 文档，提交信息尾注**必须等于本次新的 `<Version>`**。

---

## 11. 已知约束（踩坑记录，勿重复踩）

- **CPM 下 csproj 写 `Version=` 直接报 NU1008**，必须去 `Directory.Packages.props` 登记；
- **机器上存在多个 NuGet 源 + CPM 会报 NU1507**，本仓库用 `NuGet.config` 的
  `packageSources` + `packageSourceMapping` 固定为 nuget.org 解决；
- **不要用 `dotnet new avalonia.app`**：本机未装 `Avalonia.Templates`，
  Avalonia 壳工程手写 csproj（P0 Task 6），且不应为一次性脚手架改动全局模板；
- `Avalonia.Diagnostics` **无 12.x 版本**，不要试图引入；
- `AvaloniaUseCompiledBindingsByDefault=true` 时所有 `{Binding}` 必须能编译期解析到属性，
  各处都要写 `x:DataType`，否则编译报错；
- `PasswordBox.Password` 不是依赖属性，不能直接 Binding（若 P1 出现密码输入，需要附加属性桥接）。
