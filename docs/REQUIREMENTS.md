# Bing Wallpaper（极简版）需求分析 / 功能拆解 / TODO

> 目标：一个 Windows 托盘常驻小程序 —— 每天自动把必应当日壁纸设为桌面壁纸，托盘菜单可「上一张 / 下一张」。

---

## 1. 需求分析

### 1.1 原始需求
| # | 需求 | 说明 |
|---|---|---|
| R1 | 每天自动换壁纸 | 使用必应每日一图，无需用户干预 |
| R2 | 上一张 | 回退到更早的一张 |
| R3 | 下一张 | 前进到更新的一张 |
| R4 | 简单托盘 | 常驻通知区，提供上述操作入口 + 退出 |
| R5 | 其他都不要 | 不做画廊、收藏、多屏、设置面板等 |

### 1.2 实测得到的关键约束（已用 curl 验证）
1. **必应归档只有最近 8 天**。`HPImageArchive.aspx?idx=N` 中 `N>7` 会被钳制到第 7 天（实测 idx=8/20/100 都返回同一个 8 天前的图）。
   → **「上一张」超过 7 天前就无法从网络拿到，必须靠本地缓存自己攒。**
2. 归档接口支持直接要 UHD 并指定尺寸：
   `?format=js&idx=0&n=1&mkt=zh-CN&uhd=1&uhdwidth=2560&uhdheight=1440`
   返回 `_UHD.jpg`（约 3.5 MB/张），默认 `_1920x1080.jpg`（约 340 KB/张）。
   本机分辨率为 2560x1440，UHD 更合适。
3. `mkt` 决定图片集合与文案，`zh-CN` 返回中文标题（如「秋色中转动的水轮」）。
4. `urlbase` + `_UHD.jpg` 是稳定可缓存的图片直链；`startdate`(20261010) / `enddate`(20261011) 标识图片归属日。

### 1.3 核心设计决策

| 决策点 | 结论（待确认） | 理由 |
|---|---|---|
| D1 「上一张/下一张」语义 | **在本地图片库中按日期前后浏览**，不是"昨天/明天" | 网络只有 8 天历史，「下一张」无法从网络取 |
| D2 到达边界时 | 到头就置灰菜单项 + tooltip 提示；**不循环** | 循环会让用户分不清当前是不是最新 |
| D3 自动换的触发 | 常驻进程内 **每 10 分钟比较"今天 vs 当前图片日期"** 的轻量 timer | 比 24h 定时器可靠：休眠/关机唤醒后能自动补上 |
| D4 开机自启 | **建议保留**（HKCU\...\Run 一行注册） | 否则托盘程序没启动，"每天自动换"就失效 —— 这属于 R1 的必要条件，不是额外功能 |
| D5 图片来源 | UHD (按屏幕分辨率请求) | 3.5 MB/天可接受，画质明显更好 |
| D6 设置壁纸方式 | `SystemParametersInfo(SPI_SETDESKWALLPAPER)`，**不改** WallpaperStyle 注册表 | 不破坏用户已有的「填充/适应」等桌面设置 |
| D7 存储位置 | `%LOCALAPPDATA%\BingWallpaper\{images/, index.json}` | 不污染程序目录，卸载即删 |
| D8 失败处理 | 静默失败，保留当前壁纸，写日志，下个周期重试；不弹窗 | 用户要求极简 |

### 1.4 功能拆解（MoSCoW）

**Must（本次做）**
- F1 元数据获取：拉取 `/HPImageArchive.aspx`，解析 urlbase / startdate / enddate / copyright / title
- F2 图片下载：按 URL 落盘缓存（同名已存在且大小 > 0 则跳过），超时 15s，失败重试 1 次
- F3 应用壁纸：P/Invoke 设置桌面壁纸
- F4 托盘：NotifyIcon + 图标 + tooltip（显示当前图片日期与标题）+ 右键菜单
- F5 菜单：上一张 / 下一张 / 换今天的 / 退出
- F6 本地索引 `index.json`：已下载条目按日期排序 + 当前索引，重启后位置不丢
- F7 每日自动更新：日期变化 → 拉当天 → 下载 → 应用 → 移到索引末尾
- F8 首次运行：拉当天 + 回填最近 7 天历史（这样立刻就有「上一张」可点）

**Should（建议做，很小）**
- F9 开机自启（默认开，可在菜单里关）→ 见 D4
- F10 单实例互斥（Mutex），重复启动时唤起已有实例

**Won't（明确不做）**
- 图片预览 / 画廊 / 收藏 / 历史日历
- 多显示器分别设置、锁屏壁纸、屏保
- 设置窗口、主题、定时粒度配置
- 自动更新、UWP 打包（安装包与发布流程见阶段 6）
- 切换时弹气泡通知

---

## 2. 技术选型（三选一）

| 方案 | 优点 | 缺点 |
|---|---|---|
| **C# / .NET 8 WinForms（推荐）** | 零第三方依赖；原生 `NotifyIcon`；单文件 exe（~150 KB 框架依赖 / ~15 MB 自包含）；启动快；P/Invoke 直接 | 需编译（本机已装 SDK 8.0.200） |
| Python + pystray | 代码量少 | 需 pip 依赖；打包体积大且麻烦；常驻内存 ~40 MB+ |
| PowerShell + WinForms | 无需编译 | 无 exe、启动慢、常驻体验差、易被杀软拦截 |

→ 推荐 **C# / .NET 8 WinForms**，`net8.0-windows` + `WinExe` + 隐藏主窗体，纯 tray-only 应用。

### 目标文件结构（预告）
```
z09.bing-wallpaper/
├─ src/BingWallpaper/
│  ├─ Program.cs            // 入口、单实例、ApplicationContext
│  ├─ TrayApp.cs            // NotifyIcon + 菜单 + 每日 timer
│  ├─ BingApi.cs            // F1 元数据
│  ├─ ImageStore.cs         // F2 下载 + F6 索引
│  ├─ Wallpaper.cs          // F3 SPI 设置
│  └─ app.ico
├─ docs/REQUIREMENTS.md
└─ README.md
```

---

## 3. TODO List

### 阶段 0 · 脚手架
- [x] 0.1 `docs/` + `README.md` + `.gitignore`
- [x] 0.2 创建 `src/BingWallpaper` 项目（net8.0-windows / WinForms / WinExe / nullable）
- [x] 0.3 csproj：`ApplicationIcon`、`InvariantGlobalization`、AssemblyName、版本信息

### 阶段 1 · 核心能力
- [x] 1.1 `BingApi`：请求归档接口（n=8 覆盖全部历史）+ uhd 尺寸参数，JSON 反序列化
- [x] 1.2 图片 URL 构建：优先用接口返回的 `url`，非 UHD 时回退 `urlbase + "_UHD.jpg"`
- [x] 1.3 `Library.EnsureFileAsync`：HttpClient 单例、20s 超时、1 次重试、先写随机名 `.tmp` 再原子改名
- [x] 1.4 `Library.Load/Save`：`index.json`（`entries[]` + `currentIndex` + `lastAutoKey/Date`），损坏、失效条目自动清理
- [x] 1.5 `Wallpaper.Set`：`SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, 0x01|0x02)` + 失败码检查

### 阶段 2 · 托盘
- [x] 2.1 `NotifyIcon` + `app.ico`（7 尺寸，脚本生成）+ tooltip（`Bing 壁纸 · 2026-10-11 秋色中转动的水轮`）
- [x] 2.2 右键菜单：上一张 / 下一张 / 换今天的 / ─ / 开机自启(勾选) / 退出
- [x] 2.3 边界置灰：`CanGoPrevious/CanGoNext` 驱动 `.Enabled`
- [x] 2.4 单实例 Mutex；退出时 `NotifyIcon.Dispose()`
- [x] 2.5 信号量防重入 + 「处理中…」托盘提示 + 菜单禁用

### 阶段 3 · 自动化
- [x] 3.1 每日 timer（10 分钟 tick）：比 `LastAutoDate` / `LastAutoKey`，两者都变才换
- [x] 3.2 首次运行：回填最近 8 天元数据，当天图先下，其余后台补齐
- [x] 3.3 开机自启读写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`（首次运行默认开启）

### 阶段 4 · 健壮性 / 收尾
- [x] 4.1 全局异常兜底（`Application.ThreadException` + `AppDomain` + `TaskScheduler`）
- [x] 4.2 简单文件日志 `%LOCALAPPDATA%\BingWallpaper\log.txt`（超 1 MB 清空）
- [x] 4.3 `tools/publish.ps1` 产出单文件 exe（343 KB / 自包含 63 MB）；README 使用说明
- [x] 4.4 验收：首次运行 ✅、上一张/下一张 ✅、边界置灰 ✅、重启保位 ✅、
      跨天（伪造 `LastAutoDate`）✅、开机自启（注册表已写入，待重启实测）✅、
      托盘菜单真实点击 ✅、断网/404：同一 catch 分支已覆盖，未做真实断网实测 ⚠️

**实际耗时**：约 1 小时（含实机验证与发布）。

### 阶段 5 · 清理功能（需求追加）
- [x] 5.1 `WallpaperService.CleanupOldImages()`：删除 `RetentionDays`（30 天）之外的图片与索引条目
- [x] 5.2 豁免规则：**当前正在显示的那张不删**，哪怕它已经过期
- [x] 5.3 孤儿清扫：`images\` 下索引里没引用的 `*.jpg` 一并删掉
- [x] 5.4 触发点：启动时、每天自动换图后静默执行；菜单「清理旧图片」手动执行
- [x] 5.5 手动清理后用气泡告知删了几张、释放多少（自动清理不打扰）
- [x] 5.6 验收：11 项断言（过期条目/文件删除、当前图豁免、幂等、索引持久化）+
      真实程序注入 2 张假旧图 → 启动即清理（日志：删除 2 张，释放 1.2 MB）

---

## 4. 已确认的决策（全部按推荐方案）

| # | 决议 |
|---|---|
| 1 | 技术栈：**C# / .NET 8 WinForms**，零第三方依赖，单文件 exe 343 KB |
| 2 | 「上一张/下一张」= **本地库按日期浏览**，到头**置灰**（不循环） |
| 3 | 图片尺寸：**UHD**，且按屏幕物理分辨率请求（4K 屏 → 3840x2160） |
| 4 | **开机自启默认开启**，托盘菜单可关 |
| 5 | 市场地区：固定 `zh-CN`（中文标题显示在托盘 tooltip） |

## 5. 实现结果与验收记录

已实现并实测通过（2026-10-11）：

- 首次运行：下载当日 UHD 图并设为主壁纸，回填最近 8 天元数据，其余 7 张后台静默补齐 ✅
- 托盘：`NotifyIcon` 已注册进 Windows 通知区域（`HKCU\Control Panel\NotifyIconSettings` 有记录），默认在「隐藏的图标」里 ✅
- **托盘 UI（鼠标真实点击验证）**：右键菜单呈现为「上一张 / 下一张 / 换今天的 / ☑ 开机自启 / 退出」，
  处于最新一张时「下一张」正确置灰、「开机自启」正确带勾；点「上一张」→ 桌面壁纸切到 `20261010-*.jpg`、
  `CurrentIndex` 7→6；双击图标（换今天的）→ 切回 `20261011-*.jpg` ✅
- 上一张/下一张：7 步后退 + 7 步前进逐一校验桌面壁纸路径，两端返回 false 且菜单置灰 ✅
- 每日自动换：必应未换图时不重复设置；换图后自动切到最新并记录 `LastAutoDate` ✅
- 索引持久化：重启后条目数与当前位置不变 ✅
- 失效图（404）：标记后翻页自动跳过，重启时清理 ✅

自动化断言 28 项全部通过（临时测试装置，不随仓库发布）。

清理功能追加后另跑 11 项断言（过期删除 / 当前图豁免 / 孤儿清扫 / 幂等 / 索引持久化）全部通过，
并在真实程序里注入 2 条 45、40 天前的假条目验证了启动自动清理。

**实现过程中修正的两个坑**
1. 必应接口的 `startdate` 比本地日历早一天（今天 10-11 的图 `startdate=20261010`、`enddate=20261011`），
   所以统一以 **`enddate` 作为归属日**；但"今天要不要换图"仍然靠 `urlbase` 比对，不靠日期推算，
   否则会遇到"已过零点但必应还没发布新图 → 当天不再检查"的死锁。
2. 文件名直接取 `urlbase` 会产生 `th_id=OHR.xxx.jpg` 这种丑名字，改为只取 `id=` 之后的部分：
   `20261011-OHR.BabcockSP_ZH-CN5266848536.jpg`。

## 6. 与计划的偏差

- 原计划"首次运行回填 7 天历史"改为：**当天图立即下载，其余 7 张后台静默补齐**（首次启动从 25 秒降到 2 秒）。
- 额外加了 3 处健壮性处理（不算功能）：失效图跳过与清理、单实例互斥、原子写入。
- 双击托盘图标 = 换今天的（不是下一张）：幂等、任何时候都不会"没反应"。

## 7. 备注 / 风险
- 必应接口是非官方的公开接口，可能变更；已设计为「失败不影响现有壁纸」。
- 别动 `WallpaperStyle` 注册表，否则会覆盖用户的桌面个性化设置。
- 把图片存到 `%LOCALAPPDATA%` 而非程序目录，避免 exe 放 Program Files 时无写权限。
- 图片按天累积不清理：4K 屏约 2.5 MB/天，一年约 1 GB（见 README 已知限制）。

---

## 8. 阶段 6：打包与自动发布（追加需求，2026-10-12）

### 8.1 需求 R6 与验收标准

**R6：打 tag 触发 GitHub Actions，自动构建 Windows 安装包并发布 GitHub Release。**

| # | 验收标准 | 状态 |
|---|---|---|
| AC1 | `git push origin vX.Y.Z` 后 Release 页出现 `BingWallpaper-Setup-X.Y.Z-win-x64.exe`、便携 zip、`SHA256SUMS.txt` | 待真实发版验证 |
| AC2 | 干净 Win10/11 x64：免 UAC、免预装 .NET；装完托盘出现、每日换图正常 | 待真机验证 |
| AC3 | tag / 安装包文件名 / exe 版本属性 /「应用和功能」版本号四处一致 | ✅ 本地已验证前三处 |
| AC4 | 安装时自动结束旧进程；卸载清掉程序目录、开始菜单项、`HKCU\...\Run\BingWallpaper`、`%LOCALAPPDATA%\BingWallpaper` | ✅ wine 静默安装/卸载已验证 |
| AC5 | `workflow_dispatch` 手动跑能出同样的包且不创建 Release | 待首次线上演练 |

### 8.2 设计决策

| # | 决策 | 理由 |
|---|---|---|
| D9 | 安装器用 **Inno Setup**（≥6.3） | per-user 免 UAC、单文件 setup、脚本简单；GitHub windows 镜像预装 6.7.1 |
| D10 | **per-user 安装**（`PrivilegesRequired=lowest`，装到 `%LOCALAPPDATA%\Programs\BingWallpaper`） | 与 HKCU 自启、`%LOCALAPPDATA%` 数据目录的 per-user 语义一致，不引入 UAC |
| D11 | 安装包内放**自包含单文件**（63 MB）+ 另出便携 zip | 零前置依赖；自包含产物自带 `vcruntime140_cor3.dll`，无需 VC++ 运行时 |
| D12 | 触发 = tag `v*` + 手动 `workflow_dispatch`（手动只出 artifact 不发 Release） | 演练安全；`gh release create --generate-notes` 自动 notes，`-` 后缀自动 Pre-release |
| D13 | **tag 是版本唯一真源**：`-p:Version=` 写入程序集/PE，`/DAppVersion` 写入安装包 | csproj 里的 1.0.0 仅作本地默认 |
| D14 | 安装/卸载时**强制结束** `BingWallpaper.exe`（taskkill），不用 AppMutex 提示 | 托盘程序无主窗口，强杀最顺滑 |
| D15 | 卸载**连数据一起删**（图片/索引/日志） | 用户选择，不留残渣 |
| D16 | 不创建桌面图标（仅开始菜单） | 托盘程序，无需桌面图标 |
| D17 | 代码签名暂缓，先提供 SHA256 校验；预留 SignPath Foundation 申请路径 | 申请流程见 docs/RELEASING.md 第五节 |
| D18 | 许可证 **MIT**；`windows-latest` 跑 CI，ISCC 动态定位 + choco 兜底 | 公开仓库、免费额度；镜像工具漂移不会断 |

### 8.3 功能拆解与完成情况

**Must**
- [x] F11 安装包（Inno）：per-user、开始菜单、覆盖升级、卸载自启项+数据、装完可勾选启动、中文向导
- [x] F12 版本真源：tag → 程序集/PE/安装包版本
- [x] F13 CI 触发：tag push + workflow_dispatch
- [x] F14 产物：Setup.exe + 便携 zip + SHA256SUMS
- [x] F15 Release 自动化：自动建 Release、自动 notes、预发布标记

**Should**
- [x] F16 便携 zip
- [x] F17 PR/push 编译校验（`ci.yml` 完整出包但不发布）

**Won't（本期不做）**
- 代码签名、自动更新（app 内升级）、MSI/MSIX、arm64 安装包、winget 提审

### 8.4 实现文件

```
.github/workflows/release.yml       tag v* → 出包 + gh release create
.github/workflows/ci.yml            push/PR → 完整出包冒烟（不发布）
tools/release.ps1                   本地/CI 共用发布入口（版本校验 → exe → zip → ISCC → 校验和）
tools/publish.ps1                   新增 -Version / -OutputDir
installer/bing-wallpaper.iss        Inno 脚本（per-user、强杀旧进程、卸载清理）
installer/languages/ChineseSimplified.isl   安装向导中文（Inno 官方仓库 6.5.0+ 版本）
LICENSE                             MIT
docs/RELEASING.md                   发版流程、真机验收清单、SignPath 申请步骤
```

### 8.5 验收记录（2026-10-12，本机 Linux 交叉验证）

用 .NET 8.0.419 交叉编译（`EnableWindowsTargeting`），并用 wine + Inno Setup 6.7.3 真实执行 ISCC：

- `pwsh tools/release.ps1 -Version 1.0.1` 一次通过，产物：
  `BingWallpaper.exe` 62.9 MB（PE 版本 1.0.1.0）→ 便携 zip 57.6 MB → Setup 58.4 MB → `SHA256SUMS.txt`
- Setup 在 wine 里 `/VERYSILENT` 安装成功：程序落在 `%LOCALAPPDATA%\Programs\BingWallpaper`，
  开始菜单生成「Bing 壁纸」快捷方式，写 HKCU 卸载项，AppId 正确
- 卸载注入的自启 Run 值 + 伪造的 `images/`、`index.json`、`log.txt` 后静默卸载：
  程序目录、开始菜单项、Run 值、数据目录、卸载项全部清掉；`[Code]` 的 taskkill 分支已执行（wine 下返回 128，属预期）
- 异常路径：非法版本号报错、`-RequireInstaller` 且找不到 ISCC 报错、非 Windows 无 ISCC 时警告并跳过
- 预发布版本 `v1.0.1-beta.1`：FileVersion=1.0.1.0、ProductVersion=1.0.1-beta.1、安装包名带 `-beta.1`
- 待办（需真机）：AC1/AC2/AC5，以及 SmartScreen 提示、覆盖安装、重启自启——清单见 `docs/RELEASING.md`
