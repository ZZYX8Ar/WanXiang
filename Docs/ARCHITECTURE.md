# 万相 · Unity 技术架构设计

> 版本 v1.0 · 2026-09-12
> 目标引擎：Unity 2022.3.62f1c1 (LTS 中国版)
> 架构基线：QFramework v1.x · Addressables · Input System · HybridCLR

---

## 0. 结论先行

**你的整体方案可行，但有 3 处必须先改，其中 1 处是定时炸弹。**

| # | 问题 | 位置 | 为什么致命 |
|---|------|------|-----------|
| 1 | `BinaryFormatter` 已被 Unity 移除 | `DataMgr.SaveData/LoadData` | **存档格式一旦上线就换不掉**。等你被迫换方案时，老玩家存档全部读不出 |
| 2 | `streamingAssetsPath` 不能用 `File` 读 | `BinaryDataMgr.LoadTable` | 编辑器里永远测不出来，**打包 Android 即崩** |
| 3 | 配置表放 StreamingAssets | `BinaryDataMgr.Data_Binary_Path` | 包内只读，**无法热更**。而配置表恰恰是最需要热更的东西 |

除此之外的方案方向（Excel→二进制 + SO 分工、Addressables 热更、新输入系统、UI 框架优先）**都是对的**，下面给出完整设计。

---

## 1. 可行性评估

### 1.1 致命问题一：BinaryFormatter 已被 Unity 移除

你的 `Lesson2/DataMgr.cs` 用 `BinaryFormatter` 做存档序列化：

```csharp
BinaryFormatter bf = new BinaryFormatter();
bf.Serialize(ms, t);
```

这段代码在你的 2021.3 / 2022.3 上还能跑（只有编译警告），但：

- 微软在 .NET 5+ 将 `BinaryFormatter` 标记为 **obsolete（SYSLIB0011）**，理由是反序列化可导致远程代码执行（RCE），属于**不安全 API**，官方明确建议移除；
- **Unity 自 2023.1 起已将其从运行时中彻底移除**，`System.Runtime.Serialization.Formatters.Binary` 命名空间不再存在；
- 这意味着你一旦升级引擎（2023 LTS、Unity 6），存档代码**直接编译不过**。

**真正麻烦的不是"换方案"，而是"什么时候换"。**

存档文件和配置表不一样：配置表出问题，重导一次就行；存档是玩家几十小时的数据。一旦 v1.0 用 BinaryFormatter 上线，你就被锁死了——换格式意味着所有老玩家存档作废，或者写一个专门读 BinaryFormatter 的兼容层（而那时引擎已经不支持这个 API，等于无解）。

**结论：存档格式现在就要用自己的二进制格式，不能等。**

序列化方案对比：

| 方案 | 性能 | 包体 | 跨版本兼容 | Unity 支持 | 建议 |
|------|------|------|-----------|-----------|------|
| **自定义二进制 + 表头** | 最高 | 最小 | 完全可控 | 全版本 | ✅ **推荐** |
| MessagePack-CSharp | 高 | 小 | 好 | 支持良好 | 数据结构复杂时可选 |
| MemoryPack | 最高 | 最小 | 一般 | 需 C# 11 / 较高版本 | 暂不推荐（Unity 侧受限） |
| JsonUtility + 压缩 | 低 | 中 | 好 | 全版本 | 仅调试期临时用 |
| `BinaryFormatter` | 中 | 大 | ❌ | ❌ 已移除 | **禁用** |

### 1.2 致命问题二：streamingAssetsPath 在 Android 上不可 `File` 读

```csharp
// BinaryDataMgr.LoadTable
using (FileStream fs = File.Open(Data_Binary_Path + typeof(K).Name + ".zzy",
                                FileMode.Open, FileAccess.Read))
```

`Application.streamingAssetsPath` 的返回值随平台变化：

| 平台 | 实际路径 | 能否 `File` 读 |
|------|---------|---------------|
| Windows / macOS / 编辑器 | `.../Assets/StreamingAssets` | ✅ 可以 |
| iOS | `.../Data/Raw` | ✅ 可以 |
| **Android** | `jar:file:///data/app/xxx.apk!/assets` | ❌ **不可以** |

Android 上这是 **APK（压缩包）内部的虚拟路径**，`File.Open` 会失败甚至抛异常。APK 里没有"文件系统"，只有 zip 条目。

**这条最阴险的地方在于：编辑器里 100% 正常，iOS 也正常，只有 Android 崩。** 如果开发期一直用编辑器验证，这个问题会一直潜伏到第一次真机打包。

正规做法：Android 必须走 `UnityWebRequest`（它有 `file://` 与 `jar://` 协议支持）。

> **但更好的做法是根本不用 StreamingAssets 放配置表** —— 见问题三。

### 1.3 致命问题三：配置表放 StreamingAssets 无法热更

你要 Addressables 热更，但 `StreamingAssets` 的内容在 AAB/APK 内部，属于**包内只读资源**，无法通过任何热更机制更新。

而配置表（数值平衡、技能数据）**恰恰是整个游戏里最需要热更的东西**：开服后修一个数值 bug，你不可能让所有玩家重新下载安装包。

**解决：配置表二进制放进 Addressables 的 Remote Group，走 CDN。** 这一步同时解决了问题二——Addressables 自带平台路径处理，不用你自己操心 `jar://`。

### 1.4 其它需要修正的点

| 位置 | 问题 | 严重度 | 建议 |
|------|------|--------|------|
| `BinaryDataMgr` 反射按字段顺序读 | 用 `GetFields()` 顺序对齐二进制，**字段顺序一变就静默错位**——读出的数据全错，但不报错 | 🔴 高 | 表头写入字段名 + 类型签名，加载时校验，不匹配直接抛异常 |
| `LoadTable` 的 `File.Exists` 分支 | 只 `Debug.Log` 没有 `return`，文件不存在时会继续往下走然后崩 | 🟡 中 | 改为抛异常或返回失败 |
| XOR 单字节加密（`b = 199`） | 单字节 XOR = 没有加密，已知明文攻击瞬间还原 | 🟡 中 | 见 §1.5 |
| 无存档版本号 | v1.0 的存档到 v1.2 直接读错或读不出 | 🔴 高 | 存档头写 `version` + 迁移链 |
| 单例 + 静态全局 | `BinaryDataMgr.Instance` 与 QFramework 的 IOC 冲突，无法注入、无法 Mock、无法管理生命周期 | 🟡 中 | 改造为 `Utility` 接入 `Architecture` |
| 直接用 `Application.persistentDataPath` | 散落各处、不便测试、不便多账号 | 🟢 低 | 抽象 `IPathProvider` |

### 1.5 关于"加密"这件事，说句实话

你的参考代码里用了 `b = 199` 的单字节 XOR。这个做法**不建议继续**，但理由可能和你想的不一样：

**单机游戏的本地数据，加了密也防不住。** 动手的人可以直接改内存、改 DLL、改运行时。单字节 XOR 更是等于明文——只要有一处已知明文（比如字段名或一个固定数值），整份文件立刻还原。

真正有用的防护是**换目的**，而不是换算法：

| 数据类型 | 该防什么 | 该怎么做 |
|---------|---------|---------|
| 玩家本地存档（单机） | 防手滑改坏 | **不加密**，加 CRC32 校验即可 |
| 数值配置表 | 防竞品扒数值 | 二进制 + 字段名混淆（成本低、收益合理） |
| 付费 / 排行榜 / 抽卡结果 | 防作弊 | **服务端校验，不在客户端做加密** |

**把加密成本花在服务端校验上，比在客户端 XOR 一百遍有用。** 本项目作为单机肉鸽，建议：存档加 CRC32 + 版本号，不加密。

### 1.6 Unity 版本建议

你机器上有 `2021.3.45f1c1` 和 `2022.3.62f3c1`。

**建议锁定 2022.3.62f3c1（LTS 中国版）**：

| 版本 | 评价 |
|------|------|
| 2021.3.45 | ⚠️ 可用但偏旧，Addressables 版本落后，部分 Input System API 缺失 |
| **2022.3.62** | ✅ **推荐**。LTS 生命周期长、Addressables/Input System 支持成熟、HybridCLR 完整支持、插件生态稳定 |
| 2023.x / Unity 6 | ❌ 不建议。BinaryFormatter 已移除、插件适配滞后、热更方案需重新验证 |

> **锁定版本是商业项目的第一条纪律。** 中途升级引擎会带来不可控的返工，尤其是涉及热更（HybridCLR 对引擎版本敏感）。

---

## 2. 总体架构

### 2.1 分层与依赖方向

五层，**依赖只能自上而下，禁止反向依赖，禁止跨模块横向调用**：

```
┌─────────────────────────────────────────────────────────┐
│  L1  Application 应用层                                  │
│  启动流程 · 场景状态机 · 全局上下文                       │
├─────────────────────────────────────────────────────────┤
│  L2  Module 业务模块层                                   │
│  战斗 · 图鉴 · 肉鸽地图 · 融合 · 背包 · 设置              │
│  （每个模块内部自成 MVCS，模块之间通过 Event 通信）        │
├─────────────────────────────────────────────────────────┤
│  L3  Framework 框架层                                    │
│  UI 框架 · 资源 · 输入 · 音频 · 存档 · 配置表 · 事件      │
├─────────────────────────────────────────────────────────┤
│  L4  Core 核心层                                         │
│  QFramework · UniTask · Newtonsoft.Json · 第三方库        │
├─────────────────────────────────────────────────────────┤
│  L5  Engine 引擎层                                       │
│  Unity 2022.3 LTS                                       │
└─────────────────────────────────────────────────────────┘
```

**依赖规则（用 asmdef 强制约束，不靠自觉）：**

- L1 可以引用 L2/L3/L4
- L2 可以引用 L3/L4，**不能引用 L1，不能引用其它 L2 模块的具体类**（只能引用其 `IEvent` 定义）
- L3 可以引用 L4，**不能引用 L2**（框架层不认识任何业务）
- L4 谁都不引用（除了引擎）

**为什么要用 asmdef 强制而不是靠代码规范：** 人一定会偷懒。当项目到第 8 个月，某个模块为了图快直接 `FindObjectOfType<BattleManager>()`，整个架构就开始烂。asmdef 会让这种代码**直接编译不过**——把架构约束从"约定"变成"编译器强制"。

### 2.2 程序集划分

> **实现状态（2026-09-13）**：下表 ✅ 的 5 个 asmdef 已建成并通过编译验证，
> 类型落位已用反射逐个核对。标 ⬜ 的留待对应阶段创建。
>
> **命名变更说明**：本节初稿写的是 `WanXiang.Framework.asmdef`，实际落地时改名为
> **`WanXiang.Runtime`**。原因：目录名 `Framework/` 与程序集名 `WanXiang.Framework`
> 同名会让「程序集」和「目录」两个概念难以区分（说「Framework 没引用 Core」时
> 分不清指哪个）。程序集名用 `Runtime` 与 `Editor` 形成对照，语义更清晰。

```
Assets/
├── WanXiang/
│   ├── Core/                    WanXiang.Core.asmdef              ✅ 零依赖基座
│   ├── Framework/               WanXiang.Runtime.asmdef           ✅ 框架运行时
│   │   ├── UI/                    └ 分层 Canvas / 面板基类 / 栈管理 / 动效  ✅
│   │   ├── Inputs/                └ 输入系统  ✅ 见 §6
│   │   ├── Boot/                  └ 场景启动引导（UIBootstrap / InputBootstrap）✅
│   │   ├── Res/                   └ 资源加载接口（P4 接 YooAsset）
│   │   ├── Audio/                 └ 音频（P6）
│   │   ├── Save/                  └ 存档  ✅
│   │   ├── Config/                └ 配置表读取  ✅
│   │   └── Integration/         WanXiang.Integration.QFramework.asmdef ✅
│   ├── Modules/                 WanXiang.Modules.asmdef           ⬜ 业务模块（P5/P6）
│   │   ├── Battle/
│   │   ├── Bestiary/
│   │   └── Roguelike/
│   ├── Game/                    WanXiang.Game.asmdef              ⬜ 主程序集（启动）
│   ├── Editor/                  WanXiang.Editor.asmdef            ✅ 编辑器扩展，不进包
│   └── Samples/                 WanXiang.Samples.asmdef           ✅ 示例与冒烟测试
├── Plugins/Demigiant/DOTween/
│   └── Modules/                 DOTween.Modules.asmdef            ✅ 见下方说明
├── Hotfix/                      WanXiang.Hotfix.asmdef            ⬜ HybridCLR 热更程序集
└── AOT/                         WanXiang.AOT.asmdef               ⬜ AOT 补充元数据
```

#### 依赖方向（铁律）

```
        Core  ←──────────┬──────────────┬───────────┐
          ↑              │              │           │
      Runtime ───────────┘              │           │
          ↑                             │           │
   Integration.QFramework               │           │
          ↑                             │           │
      Editor / Samples ─────────────────┘           │
                                                    │
      Modules / Game / Hotfix ──────────────────────┘
```

**箭头只能朝上（依赖 Core，不能反向）。** Unity 的 asmdef 会在编译期强制这一点，
不需要靠自律。

#### 三条容易踩的实现细节

**① asmdef 无法反向引用 `Assembly-CSharp`。**
在给目录加 asmdef 之前，那里的代码属于 `Assembly-CSharp`（预定义程序集），
可以随便引用别人；加了 asmdef 之后就变成独立程序集，**只能引用显式列出的程序集**。

**② `autoReferenced` 只影响预定义程序集，asmdef 之间必须显式引用。**
`autoReferenced: true` 的意思是「允许 `Assembly-CSharp` 自动引用我」，
不代表「其它 asmdef 能自动引用我」。两个 asmdef 之间一定要写进 `references`。
忘了写的症状是 `CS0246: 找不到类型或命名空间`，即使那个类明明就在工程里。

**③ `UnityEngine.UI`（uGUI）不需要写进 asmdef 的 `references`。**
它是引擎自带的程序集（`com.unity.ugui` 以 DLL 形式提供），对 asmdef 自动可见。
实测确认：不写也能用 `Image` / `CanvasGroup`。不确定的包先试着不写，
编译报错再加——多加无用引用会让依赖图变脏。

#### 为什么给 DOTween 的 Modules 目录单独建 asmdef

DOTween 的分发形态很特殊：

| 部分 | 形态 | 落在哪个程序集 |
|------|------|---------------|
| `DOTween.dll` | 预编译 DLL | `DOTween`（有自带 asmdef） |
| `Modules/DOTweenModuleUI.cs` 等 | 源码 | 无 asmdef 时 → `Assembly-CSharp-firstpass` |

`DOFade` / `DOAnchorPos` 这些**uGUI 扩展方法在 Modules 里**，也就是说它们在
`Assembly-CSharp-firstpass`。而 asmdef **无法反向引用 `-firstpass`**（见上面 ①）。

所以：不处理的话，我们的 `WanXiang.Runtime` 永远写不出 `CanvasGroup.DOFade(...)`。

解法是给 Modules 目录补一个 `DOTween.Modules.asmdef`，把这段源码从
`-firstpass` 里「挖」出来变成独立程序集，再让 `WanXiang.Runtime` 引用它。
**这是必须做的一步，不是可选优化。**

**热更相关的程序集规则（重要，后面 §5.3 展开）：**

- `WanXiang.Hotfix` 是唯一可热更的程序集，业务逻辑尽量往这里放
- `WanXiang.AOT` 存放热更代码**会被 AOT 泛型实例化引用到的类型**，需要生成补充元数据
- 主程序集 `WanXiang.Game` 只保留启动器与极少量不可热更的逻辑
- ⚠ **DOTween 不能进热更层**：它以 DLL 分发，属于 AOT 侧。热更层调用时
  只用非泛型快捷方法（`DOMove` / `DOScale` / `DOFade` / `DOAnchorPos`），
  不要用 `DOTween.To<T>` 这类泛型 API——后者需要额外补充元数据

### 2.3 目录规范

**代码目录**（`Assets/WanXiang/`）按「一层架构角色 + 一层业务分层」组织：

```
Assets/WanXiang/Modules/Battle/
├── Model/          BattleModel, UnitModel        （数据，无逻辑）
├── System/         BattleSystem, ElementSystem   （逻辑，无显示）
├── Command/        CastSkillCommand              （玩家意图 → 状态变更）
├── Event/          BattleEvent, UnitEvent        （跨模块通信）
├── Utility/        ElementUtility                （无状态工具）
├── View/           BoardView, UnitView           （MonoBehaviour，只管显示）
└── Config/         BattleConfig (SO)
```

**资源目录**：

> **命名变更说明**：本节初稿写的是 `Assets/GameRes`，但工程里已经先有了
> `Assets/ArtRes`。**沿用 `ArtRes`** —— 一个工程里出现两个平行的资源根
> 是最容易让项目变乱的做法，改了名字也不能解决问题。以现状为准，改文档。

```
Assets/ArtRes/                                    ← 所有资源，与代码分离
├── UI/Panels/      面板 Prefab
├── UI/Sprites/     图集
├── Prefabs/
├── Audio/
└── Config/         ScriptableObject 资产
```

**代码与资源物理分离**（`Assets/WanXiang` vs `Assets/ArtRes`）。商业项目里美术和程序是两条线，混在一起会让版本管理变成灾难（美术提交时误改代码、代码提交时误删资源）。

#### 空目录不要预先创建

Git 不跟踪空目录，Unity 也会给每个目录生成 `.meta`。提前建一堆空目录的结果是：
要么它们进不了版本库（下次拉取后又消失），要么只留下一串无内容的 `.meta`。

**目录在真正有文件放进去的那一刻创建。** 本节的作用是「决定新文件该放哪」的判据，
不是一张待办清单。

---

## 3. 数据层设计

> ### 实现变更（2026-09-12 确定）：配置表导出工具改用 Luban
>
> 本章的设计原则**完全不变** —— 三类数据边界（3.1）、"表只存资源 ID 不存资源引用"、
> 运行时按 key 异步加载，这些与用什么工具导表无关。**变的只是"Excel → 二进制 + C# 类"这一步的生成器**：
> 由"自写 NPOI 工具"改为 **Luban**。
>
> **影响范围**
>
> | 组件 | 处置 |
> |------|------|
> | `Core/Serialization/`（WXBinary / WXSchemaHash / WXChecksum） | ✅ **全部保留** —— 存档序列化仍用它 |
> | `Framework/Save/SaveSystem.cs` | ✅ **全部保留** —— Luban 只管配置表，不管存档 |
> | `Framework/Config/ConfigTable.cs` | ⚠ 降级为备选方案（**不删除**） |
> | `Editor/ConfigTool/ExcelConfigExporter.cs` | ⚠ 降级为备选方案（**不删除**） |
>
> 保留备选路径的理由：Luban 有一项待验证风险（生成的 C# 代码基于较新 .NET API，
> 需确认在 Unity 2022.3 下可编译，见 §9）。验证未通过可立即退回，不必重写。
>
> **四条接入要点（血泪教训，务必先看）**
>
> 1. **`outputCodeDir` 绝不能指向业务代码目录 —— Luban 生成时会清空整个输出目录。**
>    必须专门建一个只放生成物的目录（本项目约定：`Assets/WanXiang/Config/Generated/`）。
>    这是 Luban 最容易造成实际损失的坑，且无法撤销。
> 2. **版本选择**：Luban 在 2023 下半年重构过，旧版为 Classic、新版为 Next（需 .NET SDK 8.0+）。
>    单人开发建议 **Classic** —— 它的教程与踩坑记录最多，出问题能搜到答案；
>    这一点比"流程更优雅"重要得多。以官方文档当前推荐为准。
> 3. **与热更的配合是它相对自写工具的核心优势**：生成代码不调用反射，
>    因此兼容 HybridCLR 与 Obfuz 混淆 —— 配置表才能安全地独立热更。
>    同时注意：热更**只能改数值不能改表结构**（增删字段/改类型需随代码一起更）。
> 4. **默认全量加载**。表少时无感，表多了会造成启动白屏。
>    本项目表量不大（30 只异兽 + 技能 + 五行 + 节气），早期不必处理；
>    若后续表数量破百，改用按模块懒加载并配 `UniTask` 异步。

### 3.1 三类数据的边界 —— 回答你的 Excel vs ScriptableObject 问题

分界线只有**一条判据**：

> **这份数据是否需要引用 Unity 的资源对象（Prefab / Sprite / AudioClip / Material）？**
> 需要 → ScriptableObject；不需要 → Excel 转二进制。

| 数据 | 存储方式 | 理由 |
|------|---------|------|
| 异兽表、技能表、五行表、节气表、掉落表 | **Excel → 二进制** | 量大、策划频繁改、**需热更**、纯数值 |
| 关卡节点拓扑、怪物编队 | **Excel → 二进制** | 同上 |
| UI 面板配置（UIPanelConfig） | **ScriptableObject** | 需引用面板 Prefab 与遮罩材质 |
| 特效 / 音效配置 | **ScriptableObject** | 需引用 Particle / AudioClip |
| Addressables 分组 | **SO**（工具自带） | 工具链要求 |
| 玩家存档 | **自定义二进制** | 运行时生成、需版本迁移 |
| 战斗运行时状态（棋盘、血量） | **仅内存** | 临时数据，不落盘；存档时快照 |

**关键补充规则（容易踩坑）：**

> **二进制表里只存资源 ID（string / int），绝不存资源引用。**

Excel 表里写 `spriteKey = "beast_jumang"`，运行时通过 Addressables 按 key 异步加载，由 `ResourceCache` 缓存。这样二进制表与 Unity 资源彻底解耦，配置表才能独立热更。

如果需要在 SO 里维护 `key → AssetReference` 的映射，做一个 `AssetKeyMapConfig` 的 SO 即可。

### 3.2 配置表工具链

**流程：**

```
Excel (.xlsx)  →  [Editor 工具解析]  →  .cs 数据类 + .bytes 二进制
                                              ↓
                                     进 Addressables Remote Group
                                              ↓
                              运行时 ConfigManager 异步加载
                                              ↓
                                     注入 QFramework Model
```

**Excel 表结构约定（第 4 行开始是数据）：**

| 行 | 内容 | 示例 |
|----|------|------|
| 1 | 字段名（英文，与 C# 字段对应） | `id` `name` `element` `atk` |
| 2 | 中文注释 | 编号 / 名称 / 五行 / 攻击 |
| 3 | 类型 | `int` `string` `int` `float` |
| 4 | 主键标记（`key`） | `key` |
| 5+ | 数据 | `1` `句芒` `0` `120` |

**为什么第 3 行要写类型：** 这是给你的参考代码补上最关键的一环。原 `BinaryDataMgr` 靠反射猜类型，一旦有人把 `int` 改成 `float`，二进制不会报错，只会读出垃圾数据。有了显式类型声明，生成的代码和二进制格式都是确定的。

**二进制文件格式（我在你原格式上加了三层保护）：**

```
┌─────────────────────────────────────┐
│ Magic       "WXCF"       4 bytes    │  ← 文件标识，防误读
│ FormatVer   int          4 bytes    │  ← 配置表格式版本
│ SchemaHash  int          4 bytes    │  ← 字段名+类型的哈希，变了就报错
│ RowCount    int          4 bytes    │  ← 行数
│ KeyField    string       4+n bytes  │  ← 主键字段名
│ ─────────────────────────────────── │
│ Rows        [RowCount × 字段数据]    │
└─────────────────────────────────────┘
```

**`SchemaHash` 是核心改进点。** 它把"字段顺序改了会静默读错"变成"加载时立刻抛异常并告诉你哪个表结构变了"。你的参考代码缺的正是这一层——这类 bug 在线上表现为"某些怪的攻击力莫名其妙不对"，排查成本极高。

**运行时接口（替换 `BinaryDataMgr`）：**

```csharp
// Framework 层，不依赖任何业务
public interface IConfigUtility : IUtility
{
    UniTask<T> LoadTableAsync<T>() where T : class, IConfigTable, new();
    bool TryGetTable<T>(out T table) where T : class, IConfigTable;
    void UnloadAll();
}
```

**与 QFramework 的集成方式：**

```csharp
public class ConfigModel : AbstractModel
{
    private readonly IConfigUtility _config;

    protected override void OnInit()
    {
        _config = this.GetUtility<IConfigUtility>();
    }

    public async UniTask InitializeAsync()
    {
        // 按依赖顺序加载所有表，再由各自 System 建立索引
        await _config.LoadTableAsync<BeastTable>();
        await _config.LoadTableAsync<SkillTable>();
        this.SendEvent<ConfigLoadedEvent>();
    }

    public BeastRow GetBeast(int id) { ... }
}
```

**对比你的原方案，改进了什么：**

| 原方案 | 新方案 | 收益 |
|--------|--------|------|
| `BinaryDataMgr.Instance` 单例 | `IConfigUtility` 接入 IOC | 可注入、可 Mock、可测试 |
| 反射猜字段类型 | Excel 显式声明类型 + 生成代码 | 类型改动会被发现 |
| 无 schema 校验 | `SchemaHash` 校验 | 字段顺序问题从"线上排查 3 天"变成"启动即报错" |
| `Application.streamingAssetsPath` | Addressables Remote | 跨平台 + 可热更 |
| 同步 `File.Open` | 异步 `LoadAssetAsync` | 不卡首帧 |
| 全表常驻内存 | 分表加载 + 按需卸载 | 大表可控内存 |

### 3.3 存档系统

**这是参考代码里第二危险的地方**（第一是 BinaryFormatter）。存档必须做到三件事：**版本迁移、原子写入、损坏可恢复**。

**存档文件格式：**

```
┌──────────────────────────────────────────┐
│ Magic        "WXSV"          4 bytes     │
│ SaveVer      int             4 bytes     │  ← 存档格式版本
│ GameVer      string          4+n          │  ← 游戏版本（用于统计与兼容判断）
│ PayloadLen   int             4 bytes     │
│ CRC32        uint            4 bytes     │  ← 校验，防写入中断导致的半截文件
│ ──────────────────────────────────────── │
│ Payload      [自定义二进制序列化数据]      │
└──────────────────────────────────────────┘
```

**三个必须实现的机制：**

**① 版本迁移链（最重要）**

```csharp
public interface ISaveMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    byte[] Migrate(byte[] oldData);
}

// 加载时按链条逐级升版：v1 → v2 → v3 → 当前版本
// 绝不做"分支判断当前版本"，否则每加一版就多一层 if
```

**为什么必须做：** 你 3 个月后加一个"异兽亲密度"字段，老存档里没有这个字段。如果没有迁移链，要么读不出（玩家数据全丢），要么读出默认值（还可能覆盖回写，把老存档逐步污染）。**迁移链是唯一能让你安全迭代存档结构的设计。**

**② 原子写入（防断电 / 防杀进程）**

手机游戏中玩家随时可能杀掉进程。直接覆写存档 = 有概率写出半截文件，存档直接损坏。

```
写入顺序：
1. 写 save.slot1.tmp
2. Flush + 关闭文件
3. 删除 save.slot1.bak（如果存在）
4. 重命名 save.slot1 → save.slot1.bak
5. 重命名 save.slot1.tmp → save.slot1
```

任何一步中断，都能从 `.bak` 恢复。**这是"玩家存档损坏"类事故的标准解法。**

**③ CRC32 校验 + 备份槽**

加载时校验 CRC，不通过则尝试从 `.bak` 恢复，再失败才提示用户。不要静默地把损坏存档重置为空档——那等于删了玩家的号。

**接口设计（接入 QFramework）：**

```csharp
public interface ISaveUtility : IUtility
{
    UniTask<SaveData> LoadAsync(int slot);
    UniTask SaveAsync(int slot, SaveData data, bool immediate = false);
    bool HasSave(int slot);
    void DeleteSave(int slot);
}
```

**自动存档节流：** 不要在每次状态变更时立刻写盘（移动端 IO 有代价，且 NVMe/TF 卡寿命有限）。用脏标记 + 定时器（如 30 秒）+ 关键节点强制存（通关、获得重要物品、退出游戏）。

---

## 4. UI 框架

> 你说"UI 框架最重要"，这一章写得最细。UI 框架没搭好，后面每一个界面都是在给自己挖坑——
> 而且这个坑是**复利**：第 30 个界面时代码会烂到没人敢改。

> **实现状态（已交付）**：`Assets/WanXiang/Framework/UI/` 下的 6 个文件已实现本章 4.1–4.7 的全部内容
> （分层 Canvas、面板基类与生命周期、栈状态重算、遮罩与射线拦截、加载去重、延迟加载指示、LRU 缓存、返回键策略）。
> 配套冒烟测试见 `Assets/WanXiang/Samples/UI/UIFrameworkSmokeTest.cs`（零配置，挂上即跑）。
> **尚未实现**：4.8 的列表对象池（建议随 P6 图鉴一起做，那时才有真实复用需求）。
> 数值参数与实测行为见 `README.md` §3。

### 4.1 设计目标

一个好 UI 框架要解决 6 件事，缺一件后期都会痛：

| # | 目标 | 不做的后果 |
|---|------|-----------|
| 1 | **分层明确** | 弹窗被背景挡住、HUD 盖住对话框 |
| 2 | **生命周期清晰** | 面板关闭后事件没解绑，内存泄漏 + 空引用崩溃 |
| 3 | **栈式返回** | Android 返回键行为错乱，玩家直接在论坛骂 |
| 4 | **异步加载不卡顿** | 打开背包卡 3 帧，体验劣化但难定位 |
| 5 | **数据与显示解耦** | View 里写满业务逻辑，改一个界面要读 800 行 |
| 6 | **加载可与逻辑分离** | 没法做预加载、没法优化首次打开耗时 |

### 4.2 分层设计（用独立 Canvas，不是靠 sortingOrder 混排）

**关键决策：每一层用独立的 `Canvas` 组件，而不是所有 UI 共用一个 Canvas 然后调 `sortingOrder`。**

原因在 Unity 的 UI 合批机制：**任何 UI 元素发生变动，会导致该 `Canvas` 下的整个网格重建（rebuild）。** 如果所有界面共用一个 Canvas，那么 HUD 上血条每帧跳动，都会导致背包里那 200 个格子一起重建。

分层之后，重建范围被限制在层内 —— 这是移动端 UI 性能最重要的一条优化。

| 层 | Canvas 名 | 排序 | 内容 | 缓存策略 |
|----|-----------|------|------|---------|
| 0 | `UI_Background` | 0 | 场景背景、视差 | 常驻 |
| 1 | `UI_Main` | 1000 | 主界面、常驻 HUD（血条/技能CD环） | 常驻 |
| 2 | `UI_Normal` | 2000 | 常规面板（背包、图鉴、异兽详情） | LRU 缓存 |
| 3 | `UI_Popup` | 3000 | 弹窗、确认框、二级选择 | 单次 |
| 4 | `UI_Overlay` | 4000 | 高优先弹窗（获得神品、通关结算） | 单次 |
| 5 | `UI_Toast` | 5000 | 飘字、轻提示、断线重连 | 常驻 |
| 6 | `UI_Loading` | 6000 | 加载遮罩、场景切换黑屏 | 常驻 |
| 7 | `UI_Debug` | 9000 | 调试面板（仅开发版编译） | 常驻 |

**每一层的补充规则：**

- 每层 Canvas 的 `Render Mode` 用 `Screen Space - Overlay`。
  **这是实现时相对本设计（原写 `Screen Space - Camera`）的一处简化，需要知悉**：
  Overlay 下 UI 永远绘制在最上层，无法与场景中的特效相互穿插；好处是不需要额外相机、
  性能更好、不受场景相机切换影响。若后续确实需要"粒子从 UI 后面穿到前面"这类效果
  （例如神品出场），改为 `Screen Space - Camera` + 一个专用 UI Camera 即可 ——
  改动点在 `UISystem.CreateRoot()` 一处，面板业务代码完全不用动。**在第一个特效需求出现前保持 Overlay。**
- 每层挂 `GraphicRaycaster` + `CanvasGroup`，用 `CanvasGroup.blocksRaycasts` 控制整层交互开关，**比逐个禁用按钮高效**
- `UI_Popup` 及以上层需要"点击遮罩关闭"能力：加一个全屏透明 `Image` 作为遮罩，由框架统一管理，面板只声明 `bool closeOnMaskClick`

### 4.3 面板生命周期

```
                    ┌──────────────┐
                    │  OnCreate    │  首次实例化，绑定 Model/Event
                    └──────┬───────┘
                           ↓
   ┌───────────────────────────────────────────┐
   │                                           │
   │   ┌──────────┐    ┌──────────┐            │
   │   │ OnOpen   │───→│ OnOpened │            │
   │   └──────────┘    └────┬─────┘            │
   │                        │                  │
   │        ┌───────────────┼───────────────┐  │
   │        ↓               ↓               ↓  │
   │   ┌─────────┐    ┌──────────┐   ┌────────┐│
   │   │OnPause  │    │OnResume  │   │OnUpdate││
   │   │(被盖住) │←──→│(恢复)    │   │(每帧)  ││
   │   └─────────┘    └──────────┘   └────────┘│
   │        │                                  │
   │        └──────────────┬───────────────────┘
   │                       ↓
   │                 ┌──────────┐
   │                 │ OnClose  │  解绑事件、停止协程
   │                 └────┬─────┘
   └──────────────────────┼────────────────────┘
                          ↓
              ┌──────────────────────┐
              │ 常驻？ → 保留          │
              │ 缓存？ → 入 LRU 池     │
              │ 单次？ → 销毁 + 释放    │
              └──────────────────────┘
```

**`OnPause` / `OnResume` 是最容易被忽略、但必须有的两个回调。**

场景：玩家打开背包 → 点开异兽详情 → 详情面板盖住了背包。此时背包应该停止响应输入、停止动画、停止计时器。如果框架没有 `OnPause`，背包里的"冷却倒计时"会继续跑，或者玩家能点到被盖住的按钮（如果 `blocksRaycasts` 没管好）。

**这套生命周期由框架统一调用，业务面板只重写自己需要的。**

### 4.4 栈管理

```csharp
public interface IUISystem : ISystem
{
    UniTask<T> OpenAsync<T>(object payload = null) where T : UIPanelBase;
    void Close<T>() where T : UIPanelBase;
    void CloseTop();
    void CloseAll(UILayer? layer = null);
    T Get<T>() where T : UIPanelBase;
    bool IsOpen<T>() where T : UIPanelBase;
}
```

**栈规则：**

| 操作 | 行为 |
|------|------|
| `OpenAsync<T>` | 入栈；若栈顶被遮挡则对栈顶发 `OnPause` |
| `Close<T>` | 出栈；对新的栈顶发 `OnResume` |
| `CloseTop` | 关闭栈顶（Android 返回键的默认行为） |
| `CloseTo<T>` | 关闭到指定面板为止，中间的全部出栈 |

**Android 返回键处理策略（必须明确定义，否则线上必出 bug）：**

```
按返回键
  ├─ 有 Loading 层显示？        → 忽略（不打断加载）
  ├─ Popup 栈非空？            → 关掉栈顶弹窗
  ├─ Normal 栈非空？           → 回到上一层（通常是主界面）
  ├─ Toast 里有提示？          → 忽略
  └─ 在主界面？                → 弹出"再按一次退出"提示，2 秒内再按才退出
```

最后一条是移动端标准做法。**不要直接退出游戏**——玩家误触退出会非常愤怒。

### 4.5 异步加载与缓存策略

| 策略 | 适用面板 | 加载时机 | 释放时机 |
|------|---------|---------|---------|
| **Resident 常驻** | 主界面、HUD、Loading、Toast | 启动时预载 | 永不释放 |
| **Cached 缓存（LRU）** | 背包、图鉴、编队 | 首次打开异步加载 | LRU 超限（如 6 个）时释放最久未用 |
| **Transient 单次** | 结算、活动、剧情弹窗 | 打开时加载 | 关闭即释放 |

**三个必须处理的细节：**

**① 加载中的防连点**

玩家手指快，连点两次"背包"按钮会触发两次异步加载。框架必须在 `Loading` 状态时，对同一面板的重复请求直接返回同一个 `UniTask`（而不是发起第二次加载）。

```csharp
// 用 Dictionary<Type, UniTask<T>> 记录正在加载的面板
if (_loadingTasks.TryGetValue(typeof(T), out var existing)) return existing;
```

**② 快速加载不闪 Loading**

如果资源在本地（已在内存或包内），加载可能只需 1 帧。此时若弹出 Loading 遮罩再瞬间消失，会产生难看的一闪。

处理：延迟 100ms 再显示 Loading，若期间加载完成则取消显示。用 `UniTask.Delay` + `CancellationToken` 实现。

**③ 预加载**

在进入主界面后、玩家还在看开场动画时，后台预载背包/图鉴。等玩家真的点进去时，资源已在内存，打开是瞬间的。

**这件事的收益极高、成本极低**——用户感知到的"流畅"，80% 来自这类预加载。

### 4.6 数据绑定的边界

用 QFramework 的 `BindableProperty<T>` 做 Model → View 的单向绑定：

```csharp
// Model 侧
public class PlayerModel : AbstractModel
{
    public BindableProperty<int> Gold { get; } = new BindableProperty<int>(0);
    public BindableProperty<int> Level { get; } = new BindableProperty<int>(1);
}

// View 侧（面板基类提供 Bind 辅助）
protected override void OnCreate()
{
    GoldText.BindTo(_playerModel.Gold, g => g.ToString());
    LevelText.BindTo(_playerModel.Level, l => $"Lv.{l}");
}
```

**但绑定要划清边界，别过度使用：**

| 场景 | 用什么 | 理由 |
|------|--------|------|
| 数值实时刷新（金币、等级、血条） | **BindableProperty 绑定** | 变动频繁，绑定省心 |
| 列表（背包格子、图鉴列表） | **手动刷新 + 差量更新** | 绑定到 `List` 会造成全量重建 |
| 一次性展示（弹窗文案、结算数据） | **打开时传入 payload** | 无变更需求，绑定是浪费 |

**列表千万别绑定整个 List。** 一个 200 格的背包，每次变动都重建 200 个 GameObject，会直接掉帧。正确做法是对象池 + 差量更新（只有变化的格子刷新）。

**对象池是 UI 框架的必备件**，不是优化项。背包/图鉴这类滚动列表，不用对象池几乎必然产生 GC 峰值。

### 4.7 与 QFramework 的集成方式

```csharp
// UIManager 作为 System 接入 Architecture
public class UISystem : AbstractSystem, IUISystem
{
    private readonly Dictionary<Type, UIPanelBase> _panelCache = new();
    private readonly List<UIPanelBase> _stack = new();
    private readonly Stack<UIPanelBase> _popupStack = new();
    private IResUtility _res;

    protected override void OnInit()
    {
        _res = this.GetUtility<IResUtility>();
        // 监听全局事件：金币变化 → 通知所有打开的面板
        this.RegisterEvent<PlayerGoldChangedEvent>(e => RefreshPanels<GoldDisplay>(e));
    }
}

// Architecture 注册
public class GameApp : Architecture<GameApp>
{
    protected override void Init()
    {
        RegisterUtility<IConfigUtility>(new ConfigUtility());
        RegisterUtility<IResUtility>(new AddressableResUtility());
        RegisterUtility<ISaveUtility>(new SaveUtility());
        RegisterUtility<IInputUtility>(new InputSystemUtility());

        RegisterModel<PlayerModel>(new PlayerModel());
        RegisterModel<ConfigModel>(new ConfigModel());

        RegisterSystem<IUISystem>(new UISystem());
        RegisterSystem<IBattleSystem>(new BattleSystem());
    }
}
```

**关键点：`UISystem` 不认识任何具体业务面板。** 业务面板通过 `payload` 传入数据，通过 `Event` 与 Model 通信。这样加一个新界面不用改框架任何代码。

### 4.8 面板基类（代码骨架）

```csharp
public abstract class UIPanelBase : MonoBehaviour
{
    [SerializeField] private UILayer _layer = UILayer.Normal;
    [SerializeField] private UICachePolicy _cachePolicy = UICachePolicy.Cached;
    [SerializeField] private bool _closeOnMaskClick = true;

    public UILayer Layer => _layer;
    public UICachePolicy CachePolicy => _cachePolicy;
    public bool CloseOnMaskClick => _closeOnMaskClick;
    public PanelState State { get; internal set; } = PanelState.None;

    private readonly List<IDisposable> _disposables = new();

    /// 框架调用：首次创建。绑定 Model 与 Event 都写在这里
    internal void InternalCreate() { OnCreate(); State = PanelState.Created; }

    /// 框架调用：每次打开
    internal async UniTask InternalOpenAsync(object payload)
    {
        gameObject.SetActive(true);
        State = PanelState.Opening;
        await OnOpenAsync(payload);
        State = PanelState.Opened;
        OnOpened();
    }

    internal void InternalPause()  { if (State == PanelState.Opened) { State = PanelState.Paused; OnPause(); } }
    internal void InternalResume() { if (State == PanelState.Paused) { State = PanelState.Opened; OnResume(); } }

    internal void InternalClose()
    {
        State = PanelState.Closing;
        OnClose();
        // 解绑所有事件，防止内存泄漏与空引用
        foreach (var d in _disposables) d.Dispose();
        _disposables.Clear();
        gameObject.SetActive(false);
        State = PanelState.Closed;
    }

    /// 提供注册订阅的辅助方法，统一收集到 _disposables 便于统一解绑
    protected void Register<T>(Action<T> handler) where T : struct
    {
        var unregister = this.RegisterEvent<T>(handler);
        _disposables.Add(new ActionDisposable(unregister));
    }

    protected abstract void OnCreate();
    protected virtual UniTask OnOpenAsync(object payload) => UniTask.CompletedTask;
    protected virtual void OnOpened() { }
    protected virtual void OnPause() { }
    protected virtual void OnResume() { }
    protected virtual void OnClose() { }
}
```

**`_disposables` 这个设计值得说明。** 你的 Gameplay 代码里如果手动 `RegisterEvent` 而忘了 `UnregisterEvent`，面板关闭后事件依然持有它，会造成：
- 内存泄漏（面板对象无法 GC）
- 空引用崩溃（事件触发时面板已销毁）
- 逻辑错乱（关闭的面板还在响应事件）

**框架强制用 `Register<T>()` 收集，`InternalClose` 统一解绑**，从根上杜绝这类问题。这是把"依赖开发者自觉"变成"框架保证"。

### 4.9 常见坑（每一条都是真金白银换来的）

| 坑 | 现象 | 正确做法 |
|----|------|---------|
| 一个 Canvas 装所有 UI | HUD 血条跳动导致背包重建，移动端掉帧 | **分层独立 Canvas** |
| `SetActive(false)` 当关闭 | 协程与事件没停，逻辑继续跑 | 明确生命周期回调 + 解绑 |
| 事件忘记解绑 | 面板关掉还响应事件，偶发空引用 | 框架统一收集 Dispose |
| `Instantiate/Destroy` 频繁创建列表项 | GC 峰值、滚动卡顿 | **对象池** |
| 绑定整个 List | 每次变动重建全部格子 | 差量更新 |
| 打开面板时同步 `LoadAsset` | 卡帧，且无法预加载 | **异步 + 预加载** |
| 不使用 `CanvasGroup.blocksRaycasts` | 弹窗下层还能点到 | 按层统一开关交互 |
| 返回键逻辑散落在各面板 | 各面板各写一套，行为不一致 | **框架统一处理** |
| 面板 Prefab 里直接引用资源 | 资源无法独立热更 | 只存 key，运行时按 key 加载 |
| UI 里 `Update()` 轮询数据 | 性能差、逻辑分散 | 事件驱动 + 绑定 |

### 4.10 UI 资源与命名规范

```
Assets/GameRes/UI/
├── Panels/
│   ├── Common/          通用面板（Loading, Confirm, Toast）
│   ├── Main/            主界面 HUD
│   ├── Battle/          战斗（棋盘、技能条、结算）
│   ├── Bestiary/        图鉴
│   └── Roguelike/       肉鸽地图
├── Sprites/             图集（按模块切图集，避免全量常驻）
└── Fonts/

命名：Panel_Backpack.prefab / Panel_BeastDetail.prefab
      Item_BeastCard.prefab / Item_GridSlot.prefab
      前缀统一用 Panel_ / Item_ / Popup_ / Widget_
```

**图集必须按模块切分，不要打一张大图集。** 一张 4096×4096 的通用图集会让启动时常驻几十 MB 内存。战斗图集、图鉴图集、UI 通用图集分开，随模块加载与卸载。

---

## 5. 资源与热更

### 5.1 先澄清一个关键概念：Addressables 是「资源热更」，不是「代码热更」

你说"要有热更新框架，要用最新的 Addressables"——这里需要拆成**两条独立的技术线**，它们是不同的问题：

| | 资源热更 | 代码热更 |
|---|---------|---------|
| **更新什么** | 美术图、UI Prefab、音频、配置表 | C# 逻辑（bug 修复、新功能） |
| **技术方案** | **Addressables** | **HybridCLR**（C# 原生）或 XLua（Lua） |
| **能修什么** | 换个图标、改个数值、调个布局 | 改战斗公式、修逻辑 bug、加新玩法 |
| **做不到什么** | ❌ 修不了任何逻辑 bug | ❌ 改不了引擎代码与 AOT 部分 |

**这两个必须都有。** 只做 Addressables，你上线后发现"技能伤害算错了"，只能发新包等审核——而修数值和修逻辑是两件事，很多 bug 是后者。

**它们是配合关系：** 热更的程序集（DLL）本身也作为 Addressable 资源分发，走同一条 CDN 下载链路。所以架构上是：**Addressables 提供分发通道，HybridCLR 提供代码执行能力。**

### 5.2 Addressables 组织方案

**分组策略（Addressable Groups）：**

```
Groups/
├── BuiltIn_Static/          （Pack Together / 本地）
│   └── 启动必需的资源，跟随包体
├── Config/                  （Remote / 独立分组）
│   └── 所有配置表 .bytes        ← 最常热更，必须独立
├── Code_Hotfix/             （Remote / 独立分组）
│   └── Hotfix.dll.bytes       ← 热更程序集，必须独立
├── UI_Common/               （Remote）
├── UI_Battle/               （Remote / 按需下载）
├── Beast_Art/               （Remote / 按需下载）
│   ├── Group_Beast_Mu        ← 按五行分组，玩家只下自己遇到的
│   ├── Group_Beast_Huo
│   └── ...
└── Audio/
```

**分组的三条原则：**

1. **配置表与热更 DLL 单独成组。** 它们体积小（几十 KB ~ 几 MB）、更新最频繁，必须能独立下载，不能和几百 MB 的美术资源绑在一起。
2. **按"使用场景"而不是"资源类型"分组。** 玩家在战斗里才会用到的资源放一起，图鉴里的放一起。这样玩家只需要下载即将用到的部分。
3. **依赖关系要显式检查。** Addressables 的隐式依赖经常导致"只引用了 A，却下载了整个 B 图集"。用 `Analyze` 工具定期检查 Bundle 依赖。

**标签（Labels）用于替代运行时查表：**

```
Label_Season_Spring     春季相关资源
Label_Rarity_Legend     神品
Label_Preload_Battle    进入战斗前预载
```

### 5.3 环境隔离与 Profile

**必须有至少三套环境，且互相不可混用：**

| 环境 | Profile | CDN 路径 | 用途 |
|------|---------|---------|------|
| **Dev** | `Dev_Remote` | 内网 / 本地 file:// | 日常开发，可随时改随时生效 |
| **QA** | `QA_Remote` | 测试 CDN | 打包给测试，模拟真实下载 |
| **Prod** | `Prod_Remote` | 正式 CDN + 多节点 | 线上玩家 |

**踩坑警告：** 我见过不止一个项目因为**开发期误连正式 CDN**，把测试包推送的配置表覆盖了线上环境，导致线上玩家数值全乱。**这种事故的排查成本极高，且影响所有玩家。**

防护措施：
- 每个环境的 CDN 路径写在独立的 Profile 里，通过脚本切换，不允许手填
- 打包脚本强制校验：QA 包不可能带上 Prod 的 CDN 地址（写一个 Build 前置检查）
- 正式 CDN 的写入权限只给发布流程，开发机只有读权限

### 5.4 代码热更选型

| 方案 | 语言 | 性能 | 开发体验 | 学习成本 | 适用 |
|------|------|------|---------|---------|------|
| **HybridCLR** | C# 原生 | 接近原生（IL 解释 + 部分 AOT） | 好，可以用完整 C# 与 IDE 调试 | 中（需理解 AOT 概念） | ✅ **新项目推荐** |
| XLua | Lua | 中等（需 C#/Lua 交互） | 一般，双语言维护 | 高 | 团队已有 Lua 积累时 |
| ILRuntime | C# | 较低（纯解释执行） | 一般 | 中 | 已被 HybridCLR 取代 |
| 不做代码热更 | — | — | — | — | 仅适合纯单机无长线运营 |

**我的建议：选 HybridCLR。**

理由：
1. **单语言维护。** 整个项目只用 C#，不需要在 C# 和 Lua 之间反复横跳。这对一个人的团队是决定性的——双语言意味着每次改逻辑都要想"这段该放哪边"，还要处理跨语言调用的坑。
2. **性能好。** 你的战斗是自动战斗、多单位、每回合大量数值计算，纯解释执行（ILRuntime）会有明显压力，HybridCLR 的 AOT+Interpreter 混合模式好得多。
3. **调试体验。** 可以直接断点、看堆栈。Lua 方案的错误定位通常要痛苦得多。

**但必须知道 HybridCLR 的三条硬约束：**

| 约束 | 说明 | 影响 |
|------|------|------|
| **必须 IL2CPP** | Mono 后端不支持 | 打包时间变长，需接受 |
| **AOT 泛型限制** | 热更代码里用到 `List<自定义泛型>` 这类 AOT 未实例化的泛型，需要**补充元数据** | 这是最大的坑，初期就要建立 AOT 补充流程 |
| **引擎代码不可热更** | Unity 自身的类、`MonoBehaviour` 的序列化字段结构不可改 | 加字段到 MonoBehaviour 需谨慎（走 `[SerializeField]` 兼容方案） |

> **关于 AOT 补充元数据：** 这是 HybridCLR 项目初期最容易翻车的地方。表现是"编辑器里跑得好好的，打包后报 `ExecutionEngineException`"。规避方式是在开发早期就建立"热更程序集 → AOT 泛型扫描 → 生成补充元数据 DLL"的自动化流程，不要等到上线前才发现。

### 5.5 启动流程

```
[App 启动]
    ↓
1. 加载内置 catalog（本地）
    ↓
2. 请求远端 version.json，对比资源版本号
    ↓
3. 有更新？
   ├─ 是 → 加载远端 catalog → 计算差异 → 显示下载进度条 → 下载
   └─ 否 → 跳过
    ↓
4. 加载 Hotfix.dll.bytes（从 Addressables）
    ↓
5. 补充 AOT 元数据（加载 AOT 修补 DLL）
    ↓
6. Assembly.Load(hotfixBytes)
    ↓
7. 反射调用热更入口 GameEntry.Start()
    ↓
8. 初始化：配置表 → 存档 → UI 框架 → 输入
    ↓
9. 进入主界面
```

**关键设计点：**

- **第 2~3 步要在"检查更新"界面完成**，不能让玩家看黑屏
- **下载失败要能重试**，且要区分"网络异常"与"磁盘空间不足"（移动端常见）
- **热更失败必须有兜底**：如果新版本热更代码崩溃率异常，要能一键回滚到上个资源版本（服务端改 version.json 即可）
- **强更 vs 热更**：引擎升级、原生插件变更 → 必须发新包（强更）；逻辑与资源 → 走热更

### 5.6 灰度与回滚

| 机制 | 做法 |
|------|------|
| **灰度发布** | CDN 按 10% → 50% → 100% 放量，观察崩溃率与关键指标 |
| **一键回滚** | 资源版本号指向上一个版本，客户端下次启动自动回退 |
| **强制更新** | 当检测到客户端版本低于最低支持版本，提示去商店更新 |
| **版本兼容矩阵** | 明确"哪个客户端版本能读哪些资源版本"，避免新版资源被老客户端加载 |

---

## 6. 输入系统（Input System + 改键）

> **实现状态（2026-09-13）**：P3 已交付，并在真机（Play 模式）验证通过。
>
> | 交付物 | 位置 | 状态 |
> |---|---|---|
> | 输入资产（4 张 Map、17 个 Action） | `Assets/ArtRes/Input/WanXiang.inputactions` | ✅ |
> | 运行时框架 | `Assets/WanXiang/Framework/Inputs/`（4 个文件） | ✅ 编译 0 error |
> | 场景入口 + EventSystem 改造 | `Framework/Boot/InputBootstrap.cs` | ✅ Play 模式实测 |
> | QFramework 接入 | `Framework/Integration/`（2 个文件） | ✅ 类型落位已核对 |
> | 资产生成工具 | `Editor/InputTool/InputAssetGenerator.cs` | ✅ |
> | 包依赖 | `com.unity.inputsystem@1.19.0` + `activeInputHandler = Both` | ✅ |
>
> **本章的两处重点**：§6.1 的「后端必须选 Both」（不选会打死 QFramework），
> 与 §6.3 的「初稿伪代码三处错误」（其中两处不报错、只静默失效）。

### 6.1 为什么必须用新 Input System

| 能力 | 旧 Input Manager | 新 Input System |
|------|-----------------|----------------|
| 改键（运行时重绑定） | ❌ 需要自己写一套 | ✅ 内置 `PerformInteractiveRebinding` |
| 多设备（键鼠 + 手柄 + 触屏） | ⚠️ 手工适配 | ✅ 统一抽象 |
| 绑定持久化 | ❌ 自己处理 | ✅ `SaveBindingOverridesAsJson` |
| 输入缓冲 / 连招 | ❌ | ✅ `InputAction` 的交互器 |
| 设备热插拔 | ⚠️ 需自己监听 | ✅ 自动处理 |

你的"改键功能"这个需求，基本就决定了必须用新 Input System —— 自己实现一套完整的重绑定（含冲突检测、持久化、UI 显示）成本远高于引入官方方案。

#### ⚠ 安装时必做的一步：输入后端必须选 `Both`，不能选 `New`

装完 Input System 包后 Unity 会问「是否启用新输入后端」，这个选择存在
`ProjectSettings.asset` 的 `activeInputHandler` 字段里，三个取值：

| 值 | 含义 | 后果 |
|----|------|------|
| `0` | Input Manager (Old) | 新 Input System 装了也不生效，等于白装 |
| `1` | Input System Package (New) | **旧 API 全部在运行期抛异常** |
| `2` | Both | 新旧并存，各用各的 |

**本工程已设为 `Both`（2026-09-13）。** 原因是选 `New` 会直接打死 QFramework：

| 位置 | 代码 | 影响 |
|------|------|------|
| `QFramework/.../UIKit/Scripts/Extension/UIRectTransform.cs:35,45` | `Input.mousePosition` | UIKit 的 `InRect()` / `GetLocalPosInRect()` 失效 |
| `QFramework/.../ConsoleKit/Framework/ConsoleWindow.cs:69,72` | `Input.GetKeyUp(KeyCode.F1)` | 控制台窗口（F1）打不开 |

这两处都是 QFramework 的**真代码**，不是文档注释里的示例。
`UIRectTransform.InRect()` 是 UIKit 判断「点击是否落在某个 RectTransform 上」的
核心方法。它一旦抛 `InvalidOperationException`，UI 点击判断会在**运行期**炸掉——
编译期完全正常，定位成本很高。

选 `Both` 的代价：多一点包体、运行时多一层输入事件转发。对 PC / Steam 项目
完全可以接受，换来的是 QFramework 和所有第三方插件照常工作。

> **通用判据**：装新输入系统前先跑一遍
> `grep -rn --include=*.cs -E "\bInput\.(GetKey|GetMouseButton|mousePosition)" Assets/`
> 看有多少存量代码依赖旧 API。本项目查出的就是上表两处。

### 6.2 Action Map 组织

```
WanXiang.inputactions
├── Map: UI               （UI 导航，由 InputSystemUIInputModule 使用）
├── Map: Gameplay         （战斗中的玩家干预：释放绝技、天时覆盖、加速）
├── Map: Global           （全局：返回、菜单、截图）
└── Map: Debug            （仅开发版：GM 指令）
```

**切 Map 的时机必须由框架统一管理**，不能让各面板自己 `Enable/Disable`：

| 场景 | 启用 | 禁用 |
|------|------|------|
| 主界面 | UI + Global | Gameplay |
| 战斗中 | Gameplay + Global | UI |
| 打开弹窗 | UI + Global | Gameplay |
| 加载中 | — | 全部 |

**如果不管，最典型的事故是：** 战斗中打开设置面板，玩家按"空格"想确认，结果同时触发了"绝技释放"，直接把大招交了。这类 bug 在测试期很难复现，线上却很致命。

### 6.3 改键实现

> **实现状态（2026-09-13）**：已交付，代码在 `Assets/WanXiang/Framework/Inputs/InputService.cs` 的 `RebindAsync`。
>
> ⚠ **本节初稿的伪代码有三处是错的**，下面已按真实实现改正。之所以把错误也记下来，
> 是因为每一个都会「真的出错」，而且其中两个不报错 —— 详见后面的错误表。

```csharp
public async UniTask<InputRebindResult> RebindAsync(
    InputMapType map, string actionName, int bindingIndex, CancellationToken ct)
{
    var action = FindAction(map, actionName);
    string previousPath = action.bindings[bindingIndex].effectivePath;

    // 1. 进入监听状态：先禁用所有 Map，并记下原上下文待恢复
    var contextBefore = _context;
    _isRebinding = true;
    DisableAllMaps();

    var rebind = action.PerformInteractiveRebinding(bindingIndex)
        .WithControlsExcluding("<Mouse>/position")   // 排除指针移动这类噪声
        .WithControlsExcluding("<Mouse>/delta")
        .WithCancelingThrough("<Keyboard>/escape")
        .OnMatchWaitForAnother(0.1f);                // 防抖

    try
    {
        rebind.Start();

        double deadline = Time.realtimeSinceStartupAsDouble + 10f;
        while (!rebind.completed && !rebind.canceled      // ← canceled，一个 L
               && !ct.IsCancellationRequested
               && Time.realtimeSinceStartupAsDouble < deadline)
        {
            await UniTask.NextFrame(CancellationToken.None);
        }

        // 超时或外部取消：主动 Cancel，让 RebindingOperation 走完自己的收尾流程，
        // 避免留下悬挂的设备监听
        if (!rebind.completed && !rebind.canceled) rebind.Cancel();
    }
    finally
    {
        rebind.Dispose();
        _isRebinding = false;
        ApplyContext(contextBefore, force: true);   // 必须 force，期间 _context 被搅过
    }

    // 2. 冲突检测（见细节 ①）
    // 3. 返回结果 —— 是否落盘由调用方决定，框架不碰文件系统
}
```

#### 初稿的三个错误

| # | 初稿写的 | 为什么错 | 正确做法 |
|---|---------|---------|---------|
| ① | `rebind.cancelled` | Input System 全库用**美式拼写** `canceled`（一个 L）。C# 生态里英式拼写也常见，这是高频拼错点 | `rebind.canceled` |
| ② | `_inputSystem.DisableAllMapsExcept(RebindMap)` | 这个方法不存在。而且「保留一张 RebindMap」的思路也不对 —— 改键要监听的是**任意设备上的任意输入**，不需要任何 Map 处于启用状态 | `DisableAllMaps()`；`RebindingOperation` 自己直接监听设备 |
| ③ | `await UniTask.WaitUntil(() => !rebind.action.actionMap.enabled \|\| rebind.cancelled \|\| rebind.completed)` | **逻辑反了**。我们自己刚把所有 Map 禁用，所以 `!actionMap.enabled` 一开始就为真，`WaitUntil` 会立刻返回 —— 改键根本不会等待 | 只等 `completed` / `canceled` / 超时 / ct 这四种结束条件 |

> **③ 是最危险的一个**：它不会报编译错误，也不会抛异常，只会让改键**静默失效** ——
> 玩家点了「改键」，界面一闪就退回来了，看起来像"点了没反应"。
> 这类 bug 靠测试很难发现，因为功能"没崩"，只是不工作。

另外，初稿没提**超时**。没有超时的话，玩家点了改键又反悔、直接去点别的地方，
监听会一直挂着 —— 此后所有按键都被吃掉。框架默认 10 秒超时
（`InputService.DefaultRebindTimeoutSeconds`）。

#### 四个必须处理的细节

**① 冲突检测。** 玩家把「确认」和「取消」绑到同一个键，一定要提示。

检查范围要**跨 Map** —— 「绝技」在 Gameplay、「返回」在 Global，只查当前 Map 必然漏。

但本工程**刻意不把 UI Map 纳入扫描**（见 `InputService.ConflictScanMaps`），两个原因：

- UI Map 的按键由 EventSystem 消费，而上下文切换已经保证「UI 开时 Gameplay 关」，
  两者不会同时吃同一次按键
- UI 里有一处**故意**的重复：`UI/Cancel` 与 `Global/Back` 都绑 Escape（语义不同、接收方不同）。
  把它纳入扫描，会让这处设计被反复误报成冲突

**② 持久化。** 用 `InputActionAsset.SaveBindingOverridesAsJson()` 拿到字符串，
存进**存档系统**（不是 PlayerPrefs —— 那样不方便做多账号与云同步）。

框架只负责导出/导入字符串（`SaveOverrides()` / `LoadOverrides(json)`），**不碰文件系统**。
这样「多账号」「云同步」「存档回滚」都不会跟输入系统耦合。

**③ UI 显示同步。** 改键后，所有显示按键提示的地方（如「按 [空格] 释放绝技」）都要更新。
正确做法是**不存死字符串，运行时取值**：

```csharp
// 错误 —— 只要有一个地方硬编码，改键之后它就永远停在旧键上
promptText.text = "按 [空格] 释放绝技";

// 正确
promptText.text = "按 [" + input.GetDisplayString(
    InputMapType.Gameplay, InputActionNames.Ultimate) + "] 释放绝技";
```

> `GetDisplayString` 内部走 `InputAction.GetBindingDisplayString`，
> **它会把 overridePath 考虑进去** —— 所以改键之后这里拿到的自动就是新键，
> UI 不需要自己记任何东西。
>
> 实测输出（未改键状态）：
> `Ultimate → Space`（手柄那条是 `A`）、`OverrideCelestial → Q`、
> `ToggleSpeed → Left Shift`、`Back → Escape`、`Menu → Tab`、`Screenshot → F12`。

**④ 恢复默认。** 提供「单项重置」（`ResetBinding`）与「全部重置」（`ResetAllBindings`）——
玩家把键位改乱了会想一键还原。另提供 `HasOverrides`，UI 据此决定
「恢复默认」按钮是否可点（没改过就该是灰的）。

### 6.4 与 QFramework 集成

> **实现状态（2026-09-13）**：已交付。接口名与文件划分和初稿不同，见下方修正说明。

| 文件 | 位置 | 职责 |
|---|---|---|
| `InputDefines.cs` | `Framework/Inputs/` | 枚举、名字常量、上下文 → Map 映射表 |
| `IInputService.cs` | `Framework/Inputs/` | 纯 C# 接口，**不继承 `IUtility`** |
| `InputService.cs` | `Framework/Inputs/` | 默认实现，不依赖 QFramework |
| `InputRebindResult.cs` | `Framework/Inputs/` | 改键结果与冲突信息 |
| `InputBootstrap.cs` | `Framework/Boot/` | 场景入口 + EventSystem 改造 |
| `InputEvents.cs` | `Framework/Integration/` | QFramework 强类型事件定义 |
| `QFrameworkInputService.cs` | `Framework/Integration/` | Utility 包装 + 事件翻译 |
| `InputAssetGenerator.cs` | `Editor/InputTool/` | 生成 `.inputactions` 的编辑器工具 |

#### 与初稿的三处修正

**① 接口不继承 `IUtility`。** 初稿写的是 `IInputUtility : IUtility`。
但 `IUtility` 来自 QFramework —— 让它出现在 `WanXiang.Runtime` 上，等于把整个框架
和 QFramework 焊死，以后想换架构框架就得改运行时核心。改成两层：

```
WanXiang.Runtime                    IInputService（纯 C#）  ← InputService
        ↑
WanXiang.Integration.QFramework     InputServiceUtility : IUtility  ← 约 30 行的包装
```

代价是一个薄包装类，收益是核心层干净。实测确认类型落位：
`InputService` → `WanXiang.Runtime`，`InputServiceUtility` → `WanXiang.Integration.QFramework`。

**② 方法签名不带 `InputAction`。** 初稿的 `RebindAsync(InputAction action, int bindingIndex)`
会把资产内部对象交给业务层，然后就会有人开始直接改它
（改 `enabled`、加 binding），上下文切换的纪律当场瓦解 —— 而那正是本系统存在的理由。
改成 `RebindAsync(InputMapType map, string actionName, int bindingIndex)`，
业务层永远拿不到内部对象。需要按键名？用 `GetDisplayString`。需要改键？用 `RebindAsync`。

**③ 事件翻译放在集成层。** `InputService` 只能发 `ActionPerformed("Ultimate")` 这种
带字符串的事件 —— 字符串是它在无 QFramework 环境下唯一能保证的表示。
翻译成强类型事件的工作由 `InputServiceUtility` 完成。

业务代码**不直接引用 `InputActionAsset`**，只通过事件接收：

```csharp
this.RegisterEvent<UltimateTriggeredEvent>(e => { /* 释放绝技 */ });
```

而不是：

```csharp
this.RegisterEvent<InputActionPerformedEvent>(e =>
{
    if (e.ActionName == "Ultimate") { }   // ← 字符串比较，写错了不报错、只是静默失效
});
```

这样做的收益：改键、多设备、输入重映射全部被封在 Utility 内，业务层完全无感。
将来要加手柄或触屏虚拟摇杆，业务代码一行不用改。

#### EventSystem 也必须跟着改（初稿漏掉的一环）

新输入后端下，uGUI 默认给的事件模块 `StandaloneInputModule`（走旧 `Input`）
要换成 **`InputSystemUIInputModule`**。

本工程的 UI 框架原本不创建 EventSystem，场景里也没有 —— 也就是说**换后端之后 UI 会点不动**。
这是个隐蔽的坑：UI 画得好好的，就是不响应点击，控制台也未必有报错。

`InputBootstrap` 负责这一环：找到或创建 EventSystem → 停用并移除旧模块 →
挂上新模块 → 把输入资产交给它（模块会按 Action 名字自动认领
`Point` / `Click` / `Navigate` / `Submit` / `Cancel`）。

> 这也是生成器里 UI Map 必须用**标准名字**的原因 —— 名字对不上，模块对应字段就留空，
> 那一项交互静默失效，且不会有任何提示。

实测验证结果（Play 模式，`SampleScene`）：

```
[UI]   UISystem 已启动。层级数 8，缓存上限 8。
[输入] InputService 已启动。资产「WanXiang」，共 4 张 Map，初始上下文 None（Debug Map 已启用）。

EventSystem.current          = [EventSystem]      ✓ 自动创建（场景原本没有）
  StandaloneInputModule(旧)   = 已移除             ✓
  InputSystemUIInputModule(新) = 已装上            ✓
  actionsAsset               = WanXiang           ✓

运行时切上下文（真实服务，非模拟）：
  SwitchToContext(Gameplay) → UI=·  Gameplay=✓  Global=✓    期望 ·✓✓
  SwitchToContext(Modal)    → UI=✓  Gameplay=·  Global=✓    期望 ✓·✓
```

---

## 7. 商业制作流程

你说"整体制作按照商业游戏的制作流程来开发"。这一章是**流程纪律**——这些东西在项目初期不值钱，在项目后期救命。

### 7.1 分支策略

```
main         ← 只放已发布版本，永远可出包
  ↑
release/*    ← 发布分支，只接 hotfix
  ↑
develop      ← 集成分支，日常合入
  ↑
feature/*    ← 功能分支，一个功能一条
hotfix/*     ← 紧急修复，从 main 拉，修完合回 main 与 develop
```

**三条纪律：**
1. **禁止直接提交到 `main` / `develop`**，一律走 PR + 代码审查（即使一个人开发，也要走 PR——它可以强迫你在合并前重新看一遍改了什么）
2. **每个 PR 必须能编译、且不破坏已有功能**（用 CI 强制校验编译）
3. **Unity 的 `.meta` 文件必须与资源一起提交**，且禁止在多人协作时删除重建（会丢失所有引用）

### 7.2 版本号规范

**游戏版本与资源版本必须分离** —— 这是热更架构的前提：

| 版本 | 格式 | 示例 | 何时变 |
|------|------|------|--------|
| **App 版本** | `主.次.修订` | `1.2.0` | 发新安装包时 |
| **资源版本** | `日期.序号` | `20260912.3` | 每次热更资源 |
| **存档版本** | `int` 自增 | `7` | 存档结构变更时 |
| **配置表版本** | `int` 自增 | `12` | 表结构变更时 |

**玩家设备上要能同时看到这四个版本号**（放在设置页角落或点击 logo 五次），否则客服无法定位问题。

### 7.3 CI/CD

最低限度要有的三条流水线：

| 流水线 | 触发 | 做什么 |
|--------|------|--------|
| **PR 检查** | 每次 PR | 编译检查 + 单元测试 + 代码规范扫描 |
| **每日构建** | 每天定时 | 出 QA 包 + 上传到测试 CDN + 自动热更配置表 |
| **发布构建** | 手动触发 | 出正式包 + 上传 CDN + 生成版本清单 + 灰度配置 |

**工具：** Unity 官方 `unity-builder`（GitHub Actions）或在 Windows 机器上自建 Jenkins。对独立开发者，**GitHub Actions + unity-builder 性价比最高**（免费额度对独立项目够用）。

**CI 必须做的事：**
- 自动跑 `Addressables Analyze`，报告重复依赖与冗余资源
- 记录包体大小与资源体积趋势（**体积膨胀要早发现**）
- 热更前自动备份上一版资源（回滚的底气）

### 7.4 质量卡口（性能预算）

**在项目初期就定预算，不要等优化阶段。**

| 指标 | 目标（移动端） | 检查时机 |
|------|--------------|---------|
| 帧率 | 中端机稳定 30 FPS，高端 60 | 每次构建 |
| 内存峰值 | < 800 MB（含美术常驻） | 每次构建 |
| 安装包体 | < 300 MB（含基础资源） | 每次构建 |
| DrawCall | < 120（战斗场景） | 每周 |
| UI 重建 | < 5 次/帧 | 每周 |
| 首次启动 | < 8 秒（含热更检查） | 每次构建 |
| 面板打开 | < 200 ms（已缓存） | 每周 |

**帧率与内存要接入运行时监控**，在开发版里显示实时曲线。等到"感觉卡了"再优化，已经欠了太多债。

### 7.5 监控与埋点

| 项目 | 工具 | 关注 |
|------|------|------|
| **崩溃收集** | Firebase Crashlytics / Bugly | 崩溃率、堆栈、设备分布 |
| **性能** | Unity Profiler（开发）+ 自定义上报（线上） | 卡顿、内存峰值 |
| **埋点** | 自建轻量上报 | 关卡通过率、流失点、面板停留 |
| **热更成功率** | 自建上报 | 下载失败率、回滚次数 |

**崩溃收集是上线第一优先级。** 没有它，你在论坛里看到的抱怨会变成一场猜测游戏。

**埋点的最小集：** 新手引导每一步的流失率、每一幕的失败率、每个面板的停留时长。**这三个就够了**——它们能告诉你"玩家卡在哪"。

### 7.6 必须建立的工具（编辑器扩展）

对商业项目，这三个工具能显著降低长期成本：

1. **配置表工具链**（Excel 一键导出 + 校验 + 生成代码）
2. **资源检查工具**（未使用资源、重复依赖、图集溢出、命名规范）
3. **GM 调试面板**（改数值、跳关、给资源、模拟热更）

**第 3 项最容易被忽略但最重要。** 没有 GM 工具，测试一个"第三幕守关 Boss 的掉落"需要先玩 30 分钟——一次测试 30 分钟，迭代速度就被彻底锁死。

---

## 8. 开发路线

按依赖顺序分 7 个阶段，每阶段有明确交付物与验收标准。

| 阶段 | 状态 | 内容 | 新增依赖包 | 验收标准 |
|------|------|------|-----------|---------|
| **P0 工程地基** | ✅ 已完成 | 建工程、锁版本、asmdef 分层、目录规范、Git 与 .gitignore | UniTask 2.5.11 / DOTween 1.3.030 / QFramework | 三层 asmdef 依赖违规能被编译器拦住 |
| **P1 数据层** | ✅ 已交付 | 配置表工具链（Excel→二进制）+ 存档系统（版本迁移 + 原子写入） | — | ① 改字段顺序后加载**立刻报错**而非静默读错 ② 存档 v1 能被当前版本正确迁移 ③ 杀进程不会损坏存档 |
| **P2 UI 框架** | ✅ 已交付 | 分层 Canvas、面板基类、栈管理、异步加载、返回键、面板动效 | DOTween（已验证引用链打通） | ① 打开/关闭 100 次无内存泄漏 ② 快速连点不重复加载 ③ 弹窗与 HUD 层级正确 |
| **P3 输入系统** | ✅ 已完成 | Action Map、上下文切换、改键、持久化 | Input System 1.19.0 ✅ | 改键后重启游戏配置仍在；战斗中开弹窗不会误触技能 |
| **P4 资源与热更** | ⬜ **下一步** | YooAsset 分组、Profile 环境、启动流程、HybridCLR 接入 | HybridCLR 8.14.1 + YooAsset 2.3.19 | ① 改一张配置表能热更生效 ② 改一行战斗逻辑能热更生效 ③ 能一键回滚 |
| **P5 战斗原型** | ⬜ 可与 P0–P3 穿插 | 3×3 棋盘、自动战斗、五行结算（对应 GDD 的 STEP 1） | Luban（独立命令行工具，非 UPM 包） | 灰盒下连看 10 场不无聊 |
| **P6 业务模块** | ⬜ | 图鉴、融合、肉鸽地图、设置等 | — | 一局完整通关 35-50 分钟 |

> **P4 行的修正**：本节初稿写的是「Addressables 分组」，与 §9 决策表的
> **YooAsset（非 Addressables）** 矛盾，已改正。以 §9 为准。

### 依赖包总览（截至 2026-09-13）

| 包 | 版本 | 状态 | 装法 | 备注 |
|----|------|------|------|------|
| UniTask | `2.5.11` | ✅ 已装 | OpenUPM | scopedRegistry 已配 |
| DOTween | `1.3.030` | ✅ 已装 | 本地 `.unitypackage` 导入 | 需补 `Modules/DOTween.Modules.asmdef` |
| QFramework | 用户导入版 | ✅ 已装 | 用户导入 | 8 个 asmdef |
| MCP for Unity | `10.2.0` | ✅ 已装 | git URL | 见工作区 MCP 自检脚本 |
| Input System | `1.19.0` | ✅ 已装 | Unity 官方源 | **2022.3 上的上限版本，见下方警告** |
| HybridCLR | `8.14.1` | ⬜ P4 再装 | OpenUPM | |
| YooAsset | `2.3.19` | ⬜ P4 再装 | OpenUPM | 3.x 是重写版，先用成熟的 2.x |
| Luban | 最新 | ⬜ P5 前后 | 独立 CLI，非 UPM 包 | 需 .NET SDK 8.0+ |

> ⚠ **Input System 的版本天花板**：`1.20.0` 起要求 Unity 6（`minUnity: 6000.0`）。
> 在本工程的 2022.3 上，**`1.19.0` 是能装的最高版本**，不要写成 `1.x` 让包管理器
> 自己解析——它可能会挑到一个装不上的版本然后报一堆解析错误。

**关键顺序说明：**

- **P1 在 P2 之前**：UI 框架要能读配置（面板配置表）才能跑
- **P2 在 P4 之前**：UI 框架的资源加载接口要先定义好，再接 YooAsset（用接口隔离，前期用 `Resources` 占位）
- **P5 战斗原型可以并行启动**：它不依赖 P4 的热更（本地开发阶段可以先不热更）。**不要等所有框架都完美了再验证玩法**——玩法不好玩，框架再好也没用

> **给独立开发者的建议：P5 战斗原型和 P0-P2 框架可以穿插进行。** 每搭完一块框架，就用战斗原型验证一下它好不好用。框架是给玩法服务的，不是反过来。

---

## 9. 技术决策（已确认）

> 本章原为待决策项，**2026-09-12 已全部确认**，记录如下。

### ① 代码热更方案 → **HybridCLR**（已确认）

| | HybridCLR | XLua |
|---|-----------|------|
| 语言 | C# 原生 | Lua |
| 你的学习成本 | 低（就是 C#） | 需另学 Lua 与交互方式 |
| 与 QFramework 配合 | 直接可用 | 需写 C#/Lua 桥接层 |
| 长期维护成本 | 单语言 | 双语言 |

**决策：HybridCLR。** 你在学 XLua 是有价值的（理解热更原理、能看懂 Lua 项目），但**作为本项目的主力热更方案，HybridCLR 更适合一个人开发的项目**——双语言维护的隐性成本会随着项目变大而指数上升。

另有两条佐证：ILRuntime 作者已宣布暂停重大更新，不适合新项目；XLua 已趋于稳定，只适合已有 Lua 积累的老项目。

**随之而来的必做项：** AOT 泛型补充元数据的自动化流程（这是 HybridCLR 最大的坑，必须早建，见 §5）。

### ② 开发顺序 → **P1 数据层 → P2 UI 框架**（已执行完毕）

- P1 已交付：二进制序列化核心 + 配置表框架 + 存档系统 + Excel 导表工具链
- P2 已交付：分层 UI 框架（详见 §4 顶部的实现状态说明）

### ③ 目标平台 → **PC / Steam 优先，预留 Android**（已确认）

| 影响面 | 本项目的处理 |
|--------|-------------|
| UI 分辨率适配 | 参考分辨率 1920×1080 + 宽高折中（`CanvasScaler` 已实现） |
| 返回键 | 框架层已统一处理（`UISystem.HandleBack`），Android 接入时直接可用 |
| 输入系统 | 键鼠 + 手柄双套（改键需求见 §6），触屏留待 Android 阶段 |
| 安全区 | 移动端接入时需要补 `SafeArea` 组件（PC 无此问题） |
| 图集/包体 | PC 可放宽，Android 阶段需重新规划分包 |

### ④ 附带决策

| 决策项 | 结论 | 说明 |
|--------|------|------|
| 渲染管线 | **Universal 2D（URP）** | 依据 GDD 第五章美术定调「扁平矢量绘本风 + 中国传统色，深色等宽描边、硬投影、无写实光影」—— 这是 Sprite 的原生工作方式。3D 要做等宽描边必须上后处理 Outline 且难以对准，硬投影需假投影，得不偿失。2D Renderer 对图集合批也更友好 |
| 资源热更 | **YooAsset**（非 Addressables） | 与 HybridCLR 有完整可运行的开源参考工程 `qframework-hotfix`（Unity 2022.3.62f2） |
| Unity 版本 | 建议锁定 **2022.3.62f1c1** | HybridCLR 官方推荐 2022.3.x |
| 配置表工具 | **Luban** | 详见 §3 顶部的实现变更说明。关键理由：融合表需 `ref` 引用校验、技能需嵌套结构、与 HybridCLR 原生配合。GitHub **4,500+ Star**，Unity 中国资源商店上架（与 HybridCLR、Obfuz 同属 Code Philosophy）。⚠ **待验证**：生成的 C# 基于较新 .NET API，需确认在 Unity 2022.3 可编译 |


---

## 附录：本文档与参考代码的对照

| 参考代码 | 问题 | 本文档对应改造 |
|---------|------|---------------|
| `Lesson2/DataMgr.cs` 的 `BinaryFormatter` | Unity 2023+ 已移除，存档格式锁定风险 | §3.3 自定义二进制 + 版本迁移链 |
| `Lesson2/DataMgr.cs` 的 XOR 加密 | 单字节 XOR 等于无加密 | §1.5 换目的而非换算法 |
| `ExcelData/BinaryDataMgr.cs` 的 `File.Open(streamingAssetsPath)` | Android 上必然失败 | §3.2 走 Addressables 加载 |
| `ExcelData/BinaryDataMgr.cs` 配置表放 StreamingAssets | 无法热更 | §3.2 §5.2 放 Remote Group |
| 反射按字段顺序读表 | 静默错位 | §3.2 表头 + `SchemaHash` 校验 |
| 反射猜字段类型 | 类型变更不被发现 | §3.2 Excel 显式声明类型 + 生成代码 |
| `LoadTable` 的 `File.Exists` 无 return | 文件缺失时继续执行并崩溃 | §3.2 抛异常 |
| `BinaryDataMgr.Instance` 单例 | 与 QFramework IOC 冲突 | §3.2 `IConfigUtility` 接入 Architecture |
| `Student.cs` 手工二进制读写 | 思路正确，但字段顺序无保护 | §3.3 加 Magic + 版本 + CRC32 |
| 无存档版本机制 | 迭代即丢档 | §3.3 迁移链 |
| 无原子写入 | 杀进程可能损坏存档 | §3.3 tmp → bak → rename |

---

*文档结束 · v1.0 · 2026-09-12*
