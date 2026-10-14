# 万相 · 音频素材署名清单（Audio CREDITS）

> 生成：2026-10-13 ｜ 用途：游戏内「制作名单 / CREDITS」页 + 发行合规备查
> 覆盖范围：已装入 `WanXiang/Assets/Resources/Audio/` 的全部第三方音频素材

---

## 一、许可速查

| 许可 | 能否商用 | 必须署名 | 能否闭源发行 |
|---|---|---|---|
| **CC0**（公共领域奉献） | ✅ | ❌ 不需要 | ✅ |
| **CC-BY**（署名） | ✅ | ✅ **必须** | ✅ |
| **CC-BY-SA**（署名-相同方式共享） | ✅ | ✅ 必须 | ⚠️ 有传染性，慎用 |
| Pixabay License | ✅ | ❌ | ✅ |

⚠️ 本项目**未使用任何 CC-BY-SA 素材**，避免传染性条款波及游戏本体授权。

---

## 二、必须署名的素材（CC-BY）—— 请放进游戏 CREDITS 页

### 1. `bgm_ccby_samurai.mp3`
- 作品：**Samurai**
- 作者：**TAD**
- 许可：CC-BY 4.0
- 来源：https://opengameart.org/content/samurai
- 署名行：`"Samurai" by TAD — CC-BY 4.0 — via OpenGameArt.org`

### 2. `bgm_ccby_calm.ogg`
- 作品：**Calm BGM**
- 作者：**syncopika**
- 许可：CC-BY 3.0
- 来源：https://opengameart.org/content/calm-bgm
- 署名行：`"Calm BGM" by syncopika — CC-BY 3.0 — via OpenGameArt.org`

### 3. `bgm_ccby_arukas.mp3`
- 作品：**Arukas Bloom**
- 作者：**TAD**
- 许可：CC-BY 4.0
- 来源：https://opengameart.org/content/arukas-bloom
- 署名行：`"Arukas Bloom" by TAD — CC-BY 4.0 — via OpenGameArt.org`

### 4. `bgm_ccby_lightbattle.ogg`
- 作品：**Light battle theme**
- 作者：**Alexandr Zhelanov**
- 许可：CC-BY 4.0
- 来源：https://opengameart.org/content/light-battle-theme
- 署名行：`"Light battle theme" by Alexandr Zhelanov — CC-BY 4.0 — via OpenGameArt.org`

---

### 5–18. Incompetech 系列（共 14 首，作者统一为 **Kevin MacLeod**）

- 作者：**Kevin MacLeod**（incompetech.com）
- 许可：**CC BY 4.0**
- 来源：https://incompetech.com/music/royalty-free/

**⛔ Kevin MacLeod 要求的标准署名格式**（游戏 CREDITS 页请用这个，别自己缩写）：

```
Music by Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0 License
http://creativecommons.org/licenses/by/4.0/
```

若想逐曲列出（非必须，但更规范）：

| 工程文件 | 曲名 |
|---|---|
| `bgm_km_ripples.mp3` | Ripples |
| `bgm_km_guzheng_city.mp3` | Guzheng City |
| `bgm_km_shenyang.mp3` | Shenyang |
| `bgm_km_senbazuru.mp3` | Senbazuru |
| `bgm_km_zen_moment.mp3` | That Zen Moment |
| `bgm_km_mountain_emperor.mp3` | Mountain Emperor |
| `bgm_km_snow_queen.mp3` | The Snow Queen |
| `bgm_km_frost_waltz.mp3` | Frost Waltz |
| `bgm_km_water_prelude.mp3` | Water Prelude |
| `bgm_km_temple_manes.mp3` | Temple of the Manes |
| `bgm_km_goblin_king.mp3` | The Path of the Goblin King v2 |
| `bgm_km_wizardtorium.mp3` | Wizardtorium |
| `bgm_km_clash_defiant.mp3` | Clash Defiant |
| `bgm_km_devastation.mp3` | Devastation and Revenge |

逐曲格式：`"<曲名>" Kevin MacLeod (incompetech.com) — CC BY 4.0`

**替代方案**：若不想在游戏里挂这段署名，可在 incompetech.com 购买「免署名授权」（no-attribution license），单价通常几美元/首。

### 关于「要不要预先压缩」—— 实测与结论

**先分清两件事，它们完全不同：**

| 层次 | 做什么 | 影响 |
|---|---|---|
| ① 源文件瘦身 | 现在就把 mp3 转成低码率 ogg | **只影响 git 仓库体积**，不影响 build |
| ② Unity 导入设置 | 导入时编码进包（Vorbis） | **决定 build 包体**；Unity **一定会重新编码** |

**关键：Unity 导入时本来就要压一次。** 所以「预先压」= 在 Unity 压之前多损失一次（有损转有损），
除非你另有目的（省 git），否则**不划算**。

**实测数据（2026-10-13 用本机 ffmpeg 跑）**

14 首源文件合计 **90.9 MB**，且**源码率并不统一**：

| 曲目 | 大小 | 源码率 |
|---|---|---|
| zen_moment | 11.48 MB | **160 kbps**（10 分钟长曲） |
| clash_defiant | 11.46 MB | 高码率 |
| snow_queen | 8.40 MB | 高码率 |
| ripples | 7.84 MB | **320 kbps** |
| mountain_emperor | 6.11 MB | **256 kbps** |
| guzheng_city | 1.65 MB | 低码率 |

转码后体积（实测）：

| 曲目 | 源 | q6 ≈192k | q4 ≈128k |
|---|---|---|---|
| ripples（320k 源） | 7.84 MB | 4.16 MB | 2.84 MB |
| mountain_emperor（256k 源） | 6.11 MB | 4.05 MB | 2.66 MB |
| zen_moment（**160k 源**） | 11.48 MB | 9.83 MB | 7.21 MB |

⇒ 整体只降到约 **50%（q4）～ 70%（q6）**，不是"压到十分之一"。因为**有些源本来就中低码率，没有多少可压的余量**。

**听感上的实际影响**

| 转换 | 听感 |
|---|---|
| 320 → 192 kbps (q6) | **几乎听不出**。要好耳机 + 安静环境 + 逐段细听才可能分辨 |
| 320 → 128 kbps (q4) | BGM 场景下有损但**基本可接受**；镲片、弦乐泛音、混响尾音在好设备下能听出"毛边" |
| **160 → 128 kbps** | ⚠ **损失比上面明显**，因为起点已经不高。zen_moment 这类不建议再压 |

本项目曲目里，**太鼓/镲（Mountain Emperor、Clash Defiant）对码率最敏感**；古筝、二胡单旋律相对不敏感。

**结论与三条路线**

1. **【推荐】不预先压**，让 Unity 从高码率源单次转码 —— 音质最干净。
   包体用导入设置控制：Load Type = `Streaming`、Compression = `Vorbis`、Quality ≈ 70–80。
   （21 首 BGM 在 q70 下估算约 60–70 MB 进包；想再小就减曲目，而不是减质量）
2. **【若嫌 git 大】把这 14 首加进 `.gitignore`**，需要时用 `_fetch_incompetech.py` 重新拉 ——
   URL 稳定、授权明确，**可完美复现**。既不占仓库也不损失音质。**本方案最划算。**
3. 【若一定要压】用 **q6（192k）** 而不是 q4，损失最小；且**跳过 zen_moment 这类本来就 160k 的**。

**AB 试听样本**：`_试听对比/` 下有 3 首的 q6 / q4 版本，和 `Assets/Resources/Audio/BGM/` 里的原始文件对比着听：
```
bgm_km_ripples_q6.ogg   vs  bgm_km_ripples_q4.ogg   vs  (原始) bgm_km_ripples.mp3
bgm_km_mountain_emperor_q6.ogg / _q4.ogg
bgm_km_zen_moment_q6.ogg / _q4.ogg
```

---

## 三、免署名素材（CC0）—— 仅作溯源存档

| 文件 | 作品 | 作者 | 来源 |
|---|---|---|---|
| `bgm_cc0_oriental1.ogg` | Asianoriental2 | Tozan | https://opengameart.org/content/asianoriental2 |
| `bgm_cc0_chinese_chip.ogg` | Chipnese | Spring Spring | https://opengameart.org/content/chipnese |
| `bgm_cc0_battle_prepare.mp3` | Prepare your swords | bojidar-bg | https://opengameart.org/content/prepare-your-swords |

CC0 不要求署名，但保留来源便于日后追溯与致谢。

---

## 四、程序化音效（自制 · 100% 自有权利）

`Assets/Resources/Audio/SFX/sfx_*.wav` 共 **110 个**，由 `_synth_sfx.py` 用纯代码合成
（噪声 + 指数包络 + 一阶低通/二阶带通滤波），覆盖：

- UI 交互 27 个（按钮 / 面板 / 卡牌 / 获得 / 解锁 / 存档…）
- 战斗流程与指令 14 个
- 命中与状态反馈 23 个
- **五行战记 5 个 + 终结技 5 个 + 觉醒技 + 连携技**（11 个）
- 状态异常 6 个（灼烧 / 引燃 / 冰封 / 缠绕 / 瘴气 / 减速）
- 异兽与五行气场 10 个
- 地图旅途 12 个（含 4 种脚步）
- 演出转场 6 个

**权利状态：完全自有**，无任何第三方权利主张 —— 可自由商用、修改、闭源发行，**无需署名**。
重新生成：`python _synth_sfx.py`（改参数即可批量微调音色）

---

## 五、备选素材包（Kenney Music Jingles · CC0）

位置：`_归档_设计文档/万相_音频设计文档/cc0_素材/kenney_jingles/`，共 **86 个 ogg**（5 组：8-Bit / Hit / Pizzicato / Sax / Steel）
- 作者：Kenney Vleugels（kenney.nl）
- 许可：**CC0 1.0** —— 商用免署名（作者欢迎署名但非强制）
- 说明：**未装入工程**（风格偏 chiptune/爵士，与水墨基调不合），留作备选与灵市/小游戏等特殊场景。

---

## 六、注意事项

1. **`Resources/` 目录会被全量打进包** —— `Assets/Resources/Audio/` 下所有文件都会进 build。不用的素材记得删，否则白白占包体。
2. **Incompetech 那 14 首是 320kbps 级 mp3，单曲 5–8 MB（合计约 90 MB）** —— 导入 Unity 后务必改导入设置：
   Load Type = `Streaming`、Compression = `Vorbis`、Quality ≈ 70%，单曲可降到 1–2 MB。长曲（Water Prelude 5 分钟）尤其要压。
2. 这批免费库素材的定位是 **占位 / 临时**：能立刻让游戏有声音、可验证 AudioSystem 链路。风格与《万相》水墨五行基调**不完全贴合**，正式版建议用 Suno 生成的主题曲替换。
3. 新增任何第三方素材时，**必须**在本清单登记（文件名 / 作品 / 作者 / 许可 / 来源）—— 发行时 Steim 商店页的 AI 披露与版权承诺都依赖这份记录能对得上。
4. 本清单不含 Suno 生成内容。若日后使用 Suno 曲目，需另行记录：**订阅凭证截图 + 生成日期 + 官方下载记录**。
