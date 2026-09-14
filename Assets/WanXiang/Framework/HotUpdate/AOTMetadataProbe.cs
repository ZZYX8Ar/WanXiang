// ============================================================================
//  万相 · AOT 泛型元数据体检探针（AOT 侧）
//  ---------------------------------------------------------------------------
//  ⚠⚠ 先把最重要的话说在前面：**这不是玩具，也不是可有可无的示例代码。**
//
//  它存在的唯一理由是：让「AOT 泛型补元数据」这条链路**真的被跑到**。
//
//  ---------------------------------------------------------------------------
//  背景：HybridCLR 补元数据到底在补什么
//
//  IL2CPP 是**提前编译**的。一个泛型方法 `Foo<T>()` 在 AOT 侧只会为
//  「编译期真的被用过的那几个 T」各生成一份机器码：
//
//      Foo<int> / Foo<float>   →  打包时生成好了
//      Foo<MyStruct>           →  AOT 侧没人用过  →  **根本不存在**
//
//  热更代码是运行期才加载的，它写 `Foo<MyStruct>()` 时 AOT 侧没有这份实现。
//  这时 IL2CPP 有两种表现，取决于 T 是不是值类型：
//
//    · T 是**引用类型**（class / interface / 数组）
//        → 泛型共享（generic sharing）：一份 `Foo<object>` 的机器码能给所有
//          引用类型复用。**通常不需要补元数据。**
//
//    · T 是**值类型**（struct / enum / int / bool …）
//        → 每个 T 的**布局都不同**（大小、字段偏移、能不能拆箱），
//          没法共享。AOT 侧没有就直接崩：
//          `ExecutionEngineException: method body is null`
//          （或者更早一步 `Attempting to call method ... for which no
//            method body is available`）
//
//  HybridCLR 的解法：把 AOT 程序集**被裁剪前的完整 DLL** 也打进包，
//  运行期用 `RuntimeApi.LoadMetadataForAOTAssembly()` 把它的元数据喂给
//  IL2CPP 虚拟机，虚拟机拿到元数据就能**当场解释执行**那些没编译过的泛型实例化。
//
//  这需要两份东西配合：
//    ① 编辑器侧：`HybridCLR/Generate/AOTGenericReference` 分析热更代码，
//       算出「哪些 AOT 泛型 + 值类型实参」缺实现，写进
//       `Assets/HybridCLRGenerate/AOTGenericReferences.cs` 的
//       `PatchedAOTAssemblyList`（按程序集名去重）。
//    ② 运行期：拿这份名单去加载对应程序集的 DLL 字节，
//       逐个调 `LoadMetadataForAOTAssembly`。（见 AOTMetadataLoader.cs）
//
//  ---------------------------------------------------------------------------
//  ⭐ 为什么必须有这个探针文件
//
//  ⚠ 分析法是「分析热更代码里用到的 AOT 泛型」。如果热更代码**一个都没用**，
//    产出的 `PatchedAOTAssemblyList` 就是**空列表** —— 而空列表是**看不出问题**的：
//
//      · 空列表 ⇒ 不需要补元数据 ⇒ 不加载任何 DLL ⇒ 一切正常
//      · 真需要却没分析出来 ⇒ 也是空列表 ⇒ 运行到那行才崩，且崩得莫名其妙
//
//    两者的编辑器表现**完全一样**。这就是「死链路」——代码在，但从没被执行过，
//    等哪天热更代码真的用了 `List<SomeStruct>` 才在真机上炸，
//    而那时你根本不会往"补元数据没生效"上想。
//
//  所以本工程**故意**保留下面这几个探针，并让热更入口（HotUpdateEntry）调它们。
//  只要热更侧调了，`PatchedAOTAssemblyList` 里就**必然**出现
//  `WanXiang.Runtime.dll`，于是：
//
//      · 发布工具会真的去复制 AOT DLL（复制不到会报错，不会静默）
//      · 启动流程会真的去加载 1 个 AOT DLL（加载失败会报错）
//      · 体检报告会显示「补元数据：1/1 成功」，而不是「0/0（无从判断）」
//
//  ⇒ 一条**会被走到的**链路，胜过一个**看起来对**的空壳。
//
//  ---------------------------------------------------------------------------
//  ⚠⚠⚠ 铁律：探针必须由**热更代码直接调用**，中间不许再包一层
//
//  这一条是实测打出来的 —— 第一版把三个探针调用集中进了一个 AOT 侧的
//  `RunAll()` 里，让热更代码只调 `RunAll()`。结果 `PatchedAOTAssemblyList`
//  依然是**空的 0 项**，和"确实不需要补元数据"长得一模一样。
//
//  真因在 HybridCLR 分析器的实现里（`Editor/AOT/Analyzer.cs`）：
//
//    · 对**根程序集（=热更程序集）**，`Prepare()` 是**直接扫元数据表**的：
//          TypeSpec 表   → TryAddAndWalkGenericType(...)
//          MethodSpec 表 → TryAddMethodNotWalkType(...)
//                          内部是 NeedWalk(null, ...)，而 callFrom==null 时恒为 true
//      ⇒ 只要泛型实例化出现在**热更程序集自己的元数据**里，就一定被记录。
//        它**不需要**去 walk 方法体。
//
//    · 而"walk 方法体"那条路（MethodReferenceAnalyzer → Analyzer.OnNewMethod）
//      有一道门：
//          NeedWalk(callFrom, type) =
//              热更程序集.Contains(type.Module.Name) || callFrom == null
//                                                 || callFrom.HasGenericParameters
//      注意 `callFrom` 是**调用方**（MethodReferenceAnalyzer.WalkMethod 传进去的
//      第一个参数就是"当前正在被遍历的那个方法体"），不是被调用的那个。
//
//    于是 "热更代码 → AOT 侧非泛型辅助方法 → 泛型方法" 这条链上：
//      辅助方法 ∈ 热更程序集？否。
//      辅助方法 callFrom==null？否。
//      辅助方法有泛型参数？否。
//      ⇒ 门是关的 ⇒ 泛型方法不被记录；辅助方法体也不会被继续 walk。
//
//    ⇒ **中间隔一层，`Describe<int>` 的 MethodSpec 就落在 AOT 程序集的元数据里，
//      而分析器只看热更程序集的元数据表 ⇒ 名单恒为空。**
//
//  所以：**不要**再给这几个探针套 AOT 侧包装方法（也正因如此，
//  第一版那个 `RunAll()` 已经被删掉了 —— 留着它只会诱导下一个人再犯一次）。
//
//  ---------------------------------------------------------------------------
//  ⚠ 为什么不让热更代码直接用现成的 `List<int>` 当探针
//
//  分析法只记录**AOT 侧没有实例化过**的泛型…… 更准确地说，它记录的是
//  「泛型**定义**的泛型参数里存在非引用类型约束的」那些（见
//  `Analyzer.IsNotShareableAOTGenericType`）。`List<T>` 其实符合这个条件。
//
//  但用现成泛型当探针有两个问题：
//    · 结论不稳定 —— 它取决于 QFramework / UniTask / UnityEngine 有没有
//      在热更可达的路径上实例化过它，会随第三方库版本变化而变；
//    · 报错信息空洞 —— 名单里出现 `mscorlib` 时，你无法从名字判断
//      "是我们自己引入的，还是某个库带的"。
//
//  用**自研的**泛型定义当探针，名单里就必然出现 `WanXiang.Runtime`，
//  一眼就能看出"这是我们自己的探针在起作用"。可控、可解释、可追责。
//
//  ---------------------------------------------------------------------------
//  ⚠ 为什么给这些方法标 [Obsolete]
//
//  同一条铁律的另一半：**AOT 侧不许调它们**。
//  一旦 AOT 侧也调了，泛型实例化就同时出现在 AOT 程序集的元数据里 ——
//  那本身不影响分析结果（分析器只看热更程序集），
//  但它会让"这些探针是热更侧专用的"这个约定变得含糊，
//  下一个人就可能顺手拿去做个工具方法，链条又被拆掉。
//
//  所以用 `[Obsolete]` 把它标成"看着就别碰"。这是**警告**不是错误，
//  热更侧调用时用 `#pragma warning disable CS0618` 局部压掉即可
//  （见 HotUpdateEntry.Initialize）。
//
//  ---------------------------------------------------------------------------
//  ⚠ 探针的开销 / 打包负担
//
//    · 运行期开销：`Describe` 只是字符串拼接，`ProbeBox` 只是装一个字段。
//      热更入口在 Initialize 里调一次，可以忽略。
//    · 打包负担：只是让 `PatchedAOTAssemblyList` 多出 `WanXiang.Runtime.dll`
//      一项，于是包体里多一份 WanXiang.Runtime 的裁剪前 DLL（几十 KB）。
//      这是**必需的代价** —— 真机上补元数据缺的正是这一份。
// ============================================================================

using System;

namespace WanXiang.Framework.HotUpdate
{
    /// <summary>
    /// 故意让热更代码去实例化的 AOT 泛型 —— 用来把「补元数据」这条链路走活。
    /// </summary>
    /// <remarks>
    /// 详见文件头的长说明。两条必须遵守的：
    /// <list type="number">
    /// <item>必须由**热更代码直接调用**，中间不许包 AOT 侧辅助方法；</item>
    /// <item>AOT 侧**不要**调用它们。</item>
    /// </list>
    /// </remarks>
    public static class AOTMetadataProbe
    {
        /// <summary>
        /// 泛型类型探针 —— 对应 <c>Analyzer.IsNotShareableAOTGenericType</c>。
        /// </summary>
        /// <remarks>
        /// 用值类型实参实例化它（例如 <c>ProbeBox&lt;int&gt;</c>），
        /// 就会在热更程序集的 TypeSpec 表里留下一条记录，
        /// 于是本程序集被记进 <c>PatchedAOTAssemblyList</c>。
        /// </remarks>
        /// <typeparam name="T">值类型时才有探针意义；引用类型走泛型共享，不会入名单。</typeparam>
        public sealed class ProbeBox<T>
        {
            /// <summary>装一个值。有字段才会真的产生"布局"需求，空壳类型可能被优化掉。</summary>
            public T Value;
        }

        /// <summary>
        /// 泛型方法探针 —— 对应 <c>Analyzer.IsNotShareableAOTGenericMethod</c>。
        /// </summary>
        /// <typeparam name="T">值类型时才有探针意义。</typeparam>
        /// <param name="value">任意值，只用来产生一次真实调用。</param>
        /// <returns>形如 <c>Int32=42</c> 的可读串。</returns>
        [Obsolete("体检探针：只允许**直接**从热更侧（WanXiang.HotUpdate）调用。" +
                  "① 不要包 AOT 侧辅助方法 —— 那样泛型的 MethodSpec 会落在 AOT 程序集里，" +
                  "分析法只看热更程序集的元数据表，名单会恒为空；" +
                  "② AOT 侧自己也不要调。详见 AOTMetadataProbe.cs 文件头。")]
        public static string Describe<T>(T value)
        {
            return typeof(T).Name + "=" + value;
        }

        /// <summary>
        /// 泛型方法探针（双类型参数 + 值类型实参），覆盖「多个泛型参数」的形态。
        /// </summary>
        /// <typeparam name="TKey">键类型。</typeparam>
        /// <typeparam name="TValue">值类型。</typeparam>
        [Obsolete("体检探针：只允许**直接**从热更侧调用。理由同 Describe<T>。")]
        public static string DescribePair<TKey, TValue>(TKey key, TValue value)
        {
            return typeof(TKey).Name + "->" + typeof(TValue).Name + ":" + key + "=" + value;
        }

        /// <summary>
        /// 泛型类型探针的便捷实例化入口，避免热更侧写一长串 new。
        /// </summary>
        /// <remarks>
        /// 返回 <see cref="ProbeBox{T}"/> 本身，所以调用点同时留下
        /// 一条 MethodSpec（本方法）与一条 TypeSpec（ProbeBox&lt;T&gt;）。
        /// </remarks>
        /// <typeparam name="T">值类型时才有探针意义。</typeparam>
        /// <param name="value">初始值。</param>
        [Obsolete("体检探针：只允许**直接**从热更侧调用。理由同 Describe<T>。")]
        public static ProbeBox<T> Box<T>(T value)
        {
            return new ProbeBox<T> { Value = value };
        }
    }
}
