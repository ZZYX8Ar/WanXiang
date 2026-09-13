# 万相 · Unity 技术框架

> 面向 Unity 2022.3 LTS 的商业级架构骨架  
> **QFramework v1.x + YooAsset + HybridCLR** · Excel→二进制配置表 · 版本化存档 · 分层 UI 框架 · 新输入系统  
> 目标平台：PC / Steam（架构预留 Android 适配）

---

## 本仓库包含什么

| 路径                                    | 内容                                       |
| ------------------------------------- | ---------------------------------------- |
| `docs/ARCHITECTURE.md`                | **完整技术架构设计文档**（含可行性评估、UI 框架设计、热更方案、商业流程） |
| `Assets/WanXiang/Core/Serialization/` | 二进制序列化核心（**零依赖，可直接使用**）                  |
| `Assets/WanXiang/Framework/Config/`   | 配置表运行时框架                                 |
| `Assets/WanXiang/Framework/Save/`     | 存档系统（版本迁移 + 原子写入）                        |
| `Assets/WanXiang/Framework/UI/`       | **分层 UI 框架**（栈管理、遮罩、LRU 缓存、异步加载）         |
| `Assets/WanXiang/Framework/Boot/`     | UI 启动引导                                  |
| `Assets/WanXiang/Framework/Integration/` | QFramework 接入层（条件编译，可摘除）                |
| `Assets/WanXiang/Samples/UI/`         | 零配置冒烟测试（验证整套 UI 框架）                      |
| `Assets/WanXiang/Editor/`             | Excel 导表工具链 + UI 配置校验菜单                  |

**已有工程可直接把 `Assets/WanXiang/` 整体拷入。** 除以下依赖外，代码只依赖 Unity 自带标准库：

- **NPOI** —— 仅编辑器导表工具需要
- **UniTask**（Cysharp）—— 仅 UI 框架需要
  ```
  https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
  ```

---

## 一、先读这个：你的参考代码有 3 处必须改

| # | 问题                                 | 为什么致命                                                 |
| - | ---------------------------------- | ----------------------------------------------------- |
| 1 | `BinaryFormatter` 已被 Unity 移除      | **存档格式一旦上线就换不掉**。Unity 2023.1 起移除该 API，且存在反序列化 RCE 风险 |
| 2 | `streamingAssetsPath` 不能用 `File` 读 | 编辑器里永远测不出来，**打包 Android 即崩**                          |
| 3 | 配置表放 StreamingAssets 无法热更          | 包内只读。而配置表恰恰最需要热更                                      |

本框架的代码**全部绕开了这三点**。详细分析见 `docs/ARCHITECTURE.md` §1。

---

## 二、接入步骤

### 2.1 基础接入（5 分钟）

1. 把 `Assets/WanXiang/` 整体拷入你的 Unity 工程
2. 为四块目录分别创建 `asmdef`（分层约束，详见架构文档 §2.2）
3. 完成

二进制核心、配置表框架、存档系统此时已可用，不需要任何第三方库。

### 2.2 使用配置表工具（需 NPOI）

> ⚠ **已确定改用 Luban（2026-09-12）** —— 本节的自写 NPOI 工具降级为**备选方案**，代码保留不删。
> Luban 的接入要点、版本选择与必须避开的坑见 `docs/ARCHITECTURE.md` §3 顶部的实现变更说明。
> 选 Luban 的关键理由：**融合表需要 `ref` 引用校验、技能表需要嵌套结构、与 HybridCLR 原生配合**。
> 保留本节的意义：若 Luban 的生成代码未通过 Unity 2022.3 编译验证，可立即退回此方案。

1. 从 NuGet 下载 **NPOI**，解包后把以下 DLL 放进 `Assets/Plugins/NPOI/`：


   ```
   NPOI.dll
   NPOI.OOXML.dll
   NPOI.OpenXml4Net.dll
   NPOI.OpenXmlFormats.dll
   ICSharpCode.SharpZipLib.dll
   ```
   > 建议使用 NPOI 的 .NET Standard 版本，避免 System.Drawing 依赖问题。
2. 在工程根目录创建 `Config/Excel/`，放入你的 `.xlsx` 表
3. 菜单栏 → `WanXiang / 配置表 / 导出全部表`
4. 生成结果：
   - `Assets/WanXiang/Config/Generated/{表名}.g.cs` —— 数据类 + 表类
   - `Assets/GameRes/Config/Binary/{表名}.bytes` —— 二进制

### 2.3 Excel 表结构约定

| 行  | 内容     | 示例                           |
| -- | ------ | ---------------------------- |
| 1  | 字段名    | `id` `name` `element` `atk`  |
| 2  | 中文注释   | 编号 / 名称 / 五行 / 攻击            |
| 3  | **类型** | `int` `string` `int` `float` |
| 4  | 主键标记   | `key`（只在一个列上写）               |
| 5+ | 数据     | `1` `句芒` `0` `120`           |

**支持的类型：** `int` / `long` / `float` / `bool` / `string`，  
以及数组 `int[]` / `float[]` / `string[]`（单元格内用 `;` 分隔，如 `1;2;3`）。

**文件名以 `_` 开头的表会被跳过**（用于存放草稿表）。

#### 示例表

| id      | name   | element | atk   | skills |
| ------- | ------ | ------- | ----- | ------ |
| id      | 编号     | 五行      | 攻击    | 技能列表   |
| int     | string | int     | float | int[]  |
| **key** |        |         |       |        |
| 1       | 句芒     | 0       | 120.5 | 1;2;3  |
| 2       | 祝融     | 1       | 155.0 | 4;5;6  |

导出后生成的代码可以直接使用：

```csharp
var table = new BeastTable();
table.Load(bytes);                       // bytes 从 Addressables 加载

var jumang = table.Get(1);               // 按主键查，找不到会抛异常（配置错误应在开发期暴露）
foreach (var row in table.Rows) { ... }  // 遍历
```

### 2.4 存档使用

```csharp
var save = new SaveService(
    rootDir: Path.Combine(Application.persistentDataPath, "Save"),
    currentSaveVersion: 2,
    gameVersion: "1.2.0");

save.RegisterMigration(new SaveMigrationV1ToV2());

// 读
var result = save.Load(0);
if (result.Success)
{
    if (result.RecoveredFromBackup)
        Debug.LogWarning("上次存档异常，已从备份恢复");
    var player = MySerializer.Deserialize(result.Payload);
}
else Debug.LogError(result.Error);

// 写（延迟落盘，配合定时器）
save.MarkDirty(0, MySerializer.Serialize(player));
save.FlushDirty();          // 关键节点强制落盘
```

**每次修改存档结构时，必须做两件事：**

1. `currentSaveVersion` +1
2. 写一个 `ISaveMigration` 把旧版本 payload 转成新版本，并注册

漏掉第 2 步，老玩家存档会读不出来（框架会明确报错告诉你缺哪个迁移器，而不是静默损坏数据）。

---

## 三、UI 框架（2 分钟跑起来）

### 3.1 先跑冒烟测试

1. 新建场景，建一个空 GameObject，挂上 `UIFrameworkSmokeTest`
2. Play

**应该看到的行为：**

| 操作                | 预期                                             |
| ----------------- | ---------------------------------------------- |
| 启动                | 主界面出现，计时器开始跳动                                  |
| 点「打开背包」           | 全屏背包打开，**主界面计时器停住**（OnPause 生效）                |
| 在背包打开时点主界面按钮      | **点不到**（全屏面板的透明遮罩拦住了射线）                        |
| 点「打开确认弹窗」         | 弹窗出现，遮罩变暗 62%；背包收到 OnPause                     |
| 点弹窗外的空白           | 弹窗关闭（CloseOnMaskClick）                         |
| 点「模拟返回键」          | 从内向外逐层关闭，到主界面时返回 false                         |
| 反复开关背包            | 日志里 `Backpack.OnCreate` **只出现一次**（Cached 策略生效） |

这套动作覆盖了 UI 框架所有容易出问题的路径。**接业务之前先跑通它**，能省掉后面大量的排查时间。

### 3.2 层级设计

每一层是**独立的 Canvas**（不是同一个 Canvas 里改 sortingOrder）。

> 原因：Unity 的 UI 合批以 Canvas 为单位。若所有界面共用一个 Canvas，战斗 HUD 上的血条每帧跳动都会连带把背包那 200 个格子一起重建网格 —— 这是移动端与低端 PC 上最容易压不下来的开销。

| 层级         | sortingOrder | 典型内容            | 需要遮罩 / 暂停下层 |
| ---------- | ------------ | --------------- | ----------- |
| Background | 0            | 背景、视差           | 否           |
| Main       | 1000         | 主界面、常驻 HUD      | 否           |
| Normal     | 2000         | 背包、图鉴、异兽详情      | **是**        |
| Popup      | 3000         | 确认框、二级选择        | **是**        |
| Overlay    | 4000         | 获得神品、通关结算       | **是**        |
| Toast      | 5000         | 飘字、轻提示          | 否           |
| Loading    | 6000         | 加载遮罩、场景切换       | 否           |
| Debug      | 9000         | 调试面板（**仅开发版创建**） | 否           |

**最后两列是同一个判据**（`UILayerUtil.NeedsMask`）：这一层是"覆盖在游戏画面上的界面"，还是"游戏画面本身的一部分"？

- **覆盖层**（Normal / Popup / Overlay）→ 有遮罩，打开时暂停下层
- **常驻层**（其余）→ 无遮罩，不暂停下层

Main 层绝对不能有遮罩 —— 否则主界面会被莫名压暗，而且它自己的按钮会被全屏遮罩拦住。Toast 也不该暂停下层 —— 飘字只是叠加信息。

> 为什么不看 `FullScreen`：那只决定"铺满屏幕"这个视觉结果。
> 一个居中的半屏确认框同样会被遮罩拦住下层输入，下层同样应该停下来。
> 把两件事绑在一起，就会出现"确认框开着，背包的冷却倒计时还在跑"。

### 3.3 写一个面板

```csharp
[UIPanel("Panel_Backpack",                   // 资源 key，留空则推导为 Panel_{类名}
    Layer = UILayer.Normal,                  // 所属层级：同时决定遮罩与「是否暂停下层」
    CachePolicy = UICachePolicy.Cached,      // 缓存策略
    FullScreen = true,                       // 视觉：铺满屏幕（遮罩随之变为全透明）
    CloseOnMaskClick = false)]               // 全屏面板点空白不关闭（防误触）
public sealed class BackpackPanel : UIPanelBase
{
    private Button _closeBtn;
    private Text _goldText;

    // 只执行一次：缓存组件引用、初始化对象池
    protected override void OnCreate()
    {
        _closeBtn = transform.Find("Btn_Close").GetComponent<Button>();
        _goldText = transform.Find("Gold").GetComponent<Text>();
        _closeBtn.onClick.AddListener(() => CloseSelf<BackpackPanel>());
    }

    // 每次打开：订阅事件、绑定数据
    protected override UniTask OnOpenAsync(object payload)
    {
        RegisterEvent<GoldChangedEvent>(
            h => this.RegisterEvent(h),
            h => this.UnRegisterEvent(h),
            OnGoldChanged);
        return UniTask.CompletedTask;
    }

    // 被上层遮挡：停止动画、停掉计时器
    protected override void OnPause() => StopAllTweens();

    // 重新可见
    protected override void OnResume() => RefreshAll();

    // 可选的异步任务，面板关闭时自动取消
    private void OnGoldChanged(GoldChangedEvent e)
    {
        _goldText.text = e.Value.ToString();
    }
}
```

**三条约定，遵守了就不会出大问题：**

1. **组件引用在 `OnCreate`，事件订阅在 `OnOpenAsync`。**
   框架在每次 `OnClose` 时统一解绑订阅、下一次打开重新调用 `OnOpenAsync`。
   把订阅写在 `OnCreate` 会导致第二次打开前事件是断的。
2. **任何异步任务都用 `RunTask(...)` 或传入 `PanelToken`。**
   否则会出现"面板关了，回调还在跑，访问已销毁的组件"。
3. **面板脚本必须挂在 Prefab 根节点上。**
   挂在子节点时框架的层级排序与全屏拉伸会作用在错误对象上，框架会直接报错拒绝加载。

### 3.4 缓存策略怎么选

| 策略           | 关闭后行为             | 用在哪                | 选错的代价       |
| ------------ | ----------------- | ------------------ | ----------- |
| `Resident`   | 永不释放              | 主界面、HUD、Loading   | 内存一直占着      |
| `Cached`（默认） | 保留，超出上限按 LRU 淘汰   | 背包、图鉴、详情（常用往返）     | 该缓存没缓存 → 每次卡 |
| `Transient`  | 立即销毁              | 结算、活动弹窗、确认框        | 不该缓存却缓存 → 内存涨 |

缓存上限默认为 **8**（`UISystem.CacheCapacity`）。这个数字的依据：够覆盖"主界面 → 背包 → 图鉴 → 详情"这类常用往返路径，又不至于让几十个用不到的面板堆在内存里。

### 3.5 生命周期

```
                    ┌───────────────────────────────┐
  OpenAsync ──────► │ Loading → Created             │  框架：加载 Prefab + Instantiate
                    │   ↓  OnCreate()               │  业务：缓存组件引用（只一次）
                    │ Opening                       │
                    │   ↓  OnOpenAsync(payload)     │  业务：订阅事件、绑定数据
                    │ Opened                        │
                    │   ↓  OnOpened()               │  业务：播放入场动画
                    └───────────────────────────────┘
                            │                ▲
              被上层遮挡 │                │ 上层关闭
                            ▼                │
                    ┌───────────────────────────────┐
                    │ Paused → OnPause()            │  业务：停动画、停计时器
                    │   ↑ OnResume()                │  业务：恢复
                    └───────────────────────────────┘
                            │
                    CloseAsync / Close
                            ▼
                    ┌───────────────────────────────┐
                    │ Closing → OnClose()           │  业务：存档、上报
                    │   ↓ 框架自动：解绑订阅 / 取消 PanelToken / 隐藏对象
                    │ Closed                        │
                    │   ↓ 若为 Transient 或被 LRU 淘汰
                    │ OnDestroyed()                 │  业务：释放对象池
                    └───────────────────────────────┘
```

**`OnPause` 这一环是商业项目与练习项目的分水岭。** 少了它，就会出现"背包被详情页盖住，但冷却倒计时还在跑、动画还在播、按钮还能被键盘触发"。

### 3.6 业务可用的 API

下表分两类：**面板内**（继承 `UIPanelBase` 后可直接调用），**外部**（持有 `IUISystem` 实例，或用 `UISystem.Current`）。

| 调用                                            | 位置  | 说明                                    |
| --------------------------------------------- | --- | ------------------------------------- |
| `OpenAsync<T>(payload)`                       | 面板内 | 打开面板，已打开则刷新（不重放 OnOpenAsync）           |
| `OpenPanelAsync<T>(payload)`                  | 面板内 | 打开其它面板                                |
| `CloseSelf<T>()` / `CloseSelf()`              | 面板内 | 关闭自身                                  |
| `Close<T>()` / `Close(panel)`                 | 外部  | 关闭指定面板                                |
| `CloseTop()` / `HandleBack()`                 | 外部  | 关闭栈顶 / 处理返回键（返回 `true` 表示已消费）         |
| `CloseAll(layer?)`                            | 外部  | 关闭全部，或只关闭指定层                          |
| `Get<T>()` / `IsOpen<T>()`                    | 外部  | 取实例 / 是否打开                            |
| `StackDepth`                                  | 外部  | 栈深度（不含常驻层）                            |
| `PreloadAsync<T>()`                           | 外部  | 预加载资源（不实例化），降低首次打开耗时                  |
| `Register(IDisposable)`                       | 面板内 | 注册可自动释放的订阅                            |
| `RegisterEvent<TDelegate>(sub, unsub, handler)` | 面板内 | 注册事件订阅（框架保证一定解绑）                      |
| `RunTask(Func<CancellationToken, UniTask>)`   | 面板内 | 启动与面板同生命周期的异步任务                       |
| `PanelToken`                                  | 面板内 | 面板级取消令牌（关闭时自动取消）                      |
| `State` / `Payload` / `Layer` / `Key`         | 面板内 | 只读状态                                  |
| `UIPanelRegistry.Validate()`                  | 任意  | 校验全部面板配置（有对应 Editor 菜单）                |

### 3.7 框架替你挡掉的坑

| 坑                                         | 框架的处理方式                                  |
| ----------------------------------------- | ---------------------------------------- |
| 忘记解绑事件 → 内存泄漏 / 空引用 / 关闭后仍响应              | `_disposables` 统一收集，`OnClose` 时统一释放     |
| 打开弹窗后仍能点到下层按钮（CanvasGroup 覆盖不住面板外区域）      | 每层一个全屏遮罩；全屏面板用 **alpha=0 但 raycast=true** 的隐形遮罩 |
| 关掉非栈顶面板后，下层面板点不动了                         | `RefreshStackStates()` 按栈形状重算，而非手工维护两处状态  |
| 并发点击导致面板被实例化两次                            | `_pendingOpens` 合并同一面板的并发加载请求             |
| 短加载显示转圈反而显得卡                              | 加载指示延迟 100ms 才显示                         |
| 战斗中点弹窗后按空格，同时触发了绝技释放                      | 面板打开即暂停下层，输入上下文由框架统一切换（P3 接入）            |
| 调试面板混进正式包                                 | `UILayer.Debug` 在非开发版不创建                 |
| 面板脚本挂错节点，布局错乱且无报错                         | 加载时校验并在根节点找不到组件时**显式报错拒绝加载**             |
| 两个面板推导出同一个资源 key                           | `UIPanelRegistry.Validate()` 全量扫描并报出冲突    |

### 3.8 配置校验

菜单：**`WanXiang / UI / 校验面板配置`**

它会扫描**所有程序集**（兼容 asmdef 分层），检查：

- 资源 key 是否为空、是否冲突
- Popup 层面板是否误配了 `Resident` 缓存策略

建议接进 CI 流水线第一步 —— 面板配置错误的典型形态是"那个界面打不开"而不是崩溃，没人点就一路活到上线。

---

## 四、目录结构

```
WanXiang_Framework/
├── docs/
│   └── ARCHITECTURE.md                          技术架构设计（必读）
├── Assets/WanXiang/
│   ├── Core/Serialization/
│   │   ├── WXBinary.cs                          二进制读写器（零依赖）
│   │   ├── WXSchemaHash.cs                      表结构签名
│   │   └── WXChecksum.cs                        CRC32 校验和
│   ├── Framework/
│   │   ├── Config/ConfigTable.cs                配置表基类
│   │   ├── Save/SaveSystem.cs                   存档系统（含迁移示例）
│   │   ├── UI/
│   │   │   ├── UIDefines.cs                     层级/缓存策略/接口定义
│   │   │   ├── UIPanelBase.cs                   面板基类（生命周期 + 订阅回收）
│   │   │   ├── UISystem.cs                      UI 系统核心（栈管理 + 遮罩 + LRU）
│   │   │   ├── UIPanelMask.cs                   遮罩组件
│   │   │   ├── UIPanelRegistry.cs               元数据注册表 + 配置校验
│   │   │   └── ResourcesPanelLoader.cs          Resources 加载器（原型期）
│   │   ├── Boot/UIBootstrap.cs                  启动引导
│   │   └── Integration/QFrameworkUIService.cs   QFramework 接入层（条件编译）
│   ├── Samples/UI/
│   │   └── UIFrameworkSmokeTest.cs              零配置冒烟测试
│   └── Editor/
│       ├── ConfigTool/ExcelConfigExporter.cs    Excel 导表工具
│       └── UITool/UIPanelValidationMenu.cs      UI 配置校验菜单
└── README.md
```

---

## 五、当前进度与下一步

| 阶段             | 内容                                      | 状态                |
| -------------- | --------------------------------------- | ----------------- |
| **P0 工程地基**    | asmdef 分层、目录规范、Git 配置                   | ⬜ 待做（需在真实工程内进行）   |
| **P1 数据层**     | 配置表工具链 + 存档系统                           | ✅ 已交付             |
| **P2 UI 框架**   | 分层 Canvas、面板基类、栈管理、遮罩、LRU 缓存、异步加载        | ✅ **本次交付**（对象池待补） |
| **P3 输入系统**    | Action Map、上下文切换、改键、持久化                 | ⬜ 下一步（含 InputAction 与 UI 的联动） |
| **P4 资源与热更**   | YooAsset 分组、环境 Profile、HybridCLR 接入     | ⬜ 待做              |
| **P5 战斗原型**    | 3×3 棋盘、自动战斗、五行结算                        | ⬜ 待做              |
| **P6 业务模块**    | 图鉴、融合、肉鸽地图、设置                           | ⬜ 待做              |

**P2 已交付的内容：** 8 层独立 Canvas、面板基类与完整生命周期、栈状态重算、遮罩与射线拦截、加载去重（防连点）、延迟加载指示、LRU 缓存淘汰、返回键分层策略、面板复用刷新、QFramework 接入层、冒烟测试。

**P2 待补的部分：**
- **列表对象池**（背包 200 格 / 图鉴网格的复用容器）—— 建议随 P6 图鉴一起做，那时才有真实的复用需求与数据形态
- **YooAsset 版本加载器** —— 随 P4 一起，替换 `ResourcesPanelLoader`
- **UI 动效封装**（DoTween / UniTask 入场出场）—— 有美术需求后再定

---

## 六、已确认的技术决策

| # | 决策项    | 结论                              | 依据                                     |
| - | ------ | ------------------------------- | -------------------------------------- |
| 1 | 代码热更   | **HybridCLR**                   | 国内事实标准；C# 原生单语言，无需维护 Lua 双份逻辑           |
| 2 | 资源热更   | **YooAsset**                    | 与 HybridCLR 有完整可运行的开源参考工程 `qframework-hotfix` |
| 3 | 架构框架   | **QFramework v1.x**             | 1–5 人团队最佳解；低侵入，可与上述两者共存                |
| 4 | 目标平台   | **PC / Steam 优先**，预留 Android | UI 已按"独立 Canvas + 屏幕适配"设计，无需返工            |
| 5 | Unity 版本 | 建议锁定 **2022.3.62f1c1**        | 你机器上已有；HybridCLR 官方推荐 2022.3.x          |
| 6 | 输入系统   | **新版 Input System**             | 改键功能需要 `InputAction` 的 Binding 重绑定能力    |
| 7 | 渲染管线   | **Universal 2D (URP)**          | GDD 第五章定调「扁平矢量绘本风」：深色等宽描边、硬投影、无写实光影 —— 全是 Sprite 的原生工作方式 |
| 8 | 配置表工具  | **Luban**                       | 融合表需 `ref` 引用校验、技能需嵌套结构；GitHub 4,500+ Star，与 HybridCLR 同属 Code Philosophy |

> 注意：**Addressables 与 HybridCLR 是两条独立的技术线** —— Addressables 管资源热更，不做代码热更。我们的方案是 YooAsset 管资源、HybridCLR 管代码，且热更 DLL 本身也作为资源包分发。

---

## 七、合规与依赖说明

- 本框架代码为原创，可自由用于你的项目
- 第三方依赖：
  - **NPOI**（Apache 2.0，仅编辑器导表工具需要）
  - **UniTask**（MIT，仅 UI 框架需要）
  - **QFramework**（MIT，可选）
- 不含任何需要授权的商业库
