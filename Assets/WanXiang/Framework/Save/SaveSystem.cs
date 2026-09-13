// ============================================================================
//  WanXiang · 存档系统
//  ---------------------------------------------------------------------------
//  替代参考代码 DataMgr 的 BinaryFormatter 方案。三处关键改进：
//
//    1. 不依赖 BinaryFormatter —— 该 API 自 Unity 2023.1 起已从运行时移除，
//       且存在反序列化 RCE 风险。存档格式一旦上线无法更换，必须现在规避。
//
//    2. 存档版本迁移链 —— v1 存档能被当前版本正确升级。没有它，你加一个
//       字段就意味着老玩家存档全部作废。
//
//    3. 原子写入 + CRC 校验 + 备份槽 —— 手机端玩家随时可能杀进程，
//       直接覆写会有概率留下半截文件。本实现保证任何时刻中断都能恢复。
//
//  文件布局：
//    ┌──────────────────────────────────────────┐
//    │ Magic        "WXSV"           4 bytes    │
//    │ SaveVersion  int              4 bytes    │  存档结构版本
//    │ GameVersion  string           4 + n      │  写入时的游戏版本（诊断用）
//    │ PayloadLen   int              4 bytes    │
//    │ Crc32        uint             4 bytes    │  Payload 的校验和
//    │ ──────────────────────────────────────── │
//    │ Payload      [业务自定义二进制]            │
//    └──────────────────────────────────────────┘
//
//  写入流程（原子性保证）：
//    write .tmp → flush → 旧档改名 .bak → .tmp 改名正式档
//    任一步中断，读取时都会回退到 .bak。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using WanXiang.Core.Serialization;

namespace WanXiang.Framework.Save
{
    /// <summary>存档读取结果。</summary>
    public struct SaveReadResult
    {
        public bool Success;
        /// <summary>迁移完成后的存档版本（等于当前版本）。</summary>
        public int SaveVersion;
        /// <summary>存档写入时的游戏版本。</summary>
        public string GameVersion;
        /// <summary>可直接反序列化的业务数据。</summary>
        public byte[] Payload;
        /// <summary>失败原因（Success 为 false 时有效）。</summary>
        public string Error;
        /// <summary>是否从 .bak 备份恢复而来（提示玩家"上次存档异常，已恢复"）。</summary>
        public bool RecoveredFromBackup;

        public static SaveReadResult Fail(string reason) =>
            new SaveReadResult { Success = false, Error = reason };

        public static SaveReadResult Ok(int ver, string gameVer, byte[] payload, bool fromBackup = false) =>
            new SaveReadResult
            {
                Success = true,
                SaveVersion = ver,
                GameVersion = gameVer,
                Payload = payload,
                RecoveredFromBackup = fromBackup,
            };
    }

    /// <summary>
    /// 存档版本迁移器。业务侧为每次存档结构变更实现一个，
    /// 示例见文件末尾的 SaveMigrationV1ToV2。
    /// </summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        /// <summary>把旧版本 payload 转成新版本 payload。</summary>
        byte[] Migrate(byte[] oldPayload);
    }

    /// <summary>
    /// 存档服务。负责文件读写、校验、备份回退与版本迁移。
    /// 与业务数据格式解耦 —— 业务只需提供/接收 byte[]，自己的序列化自己决定。
    /// </summary>
    public sealed class SaveService
    {
        public const uint Magic = 0x56535857;   // "WXSV"

        private readonly string _rootDir;
        private readonly int _currentSaveVersion;
        private readonly string _gameVersion;

        /// <summary>从版本号索引的迁移器：key = FromVersion</summary>
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        /// <summary>延迟写入缓冲：slot → payload。配合 FlushDirty 使用。</summary>
        private readonly Dictionary<int, byte[]> _dirtyBuffer = new Dictionary<int, byte[]>();

        /// <param name="rootDir">存档目录。移动端建议传 Application.persistentDataPath + "/Save"</param>
        /// <param name="currentSaveVersion">当前客户端支持的存档结构版本</param>
        /// <param name="gameVersion">当前游戏版本，如 "1.2.0"</param>
        public SaveService(string rootDir, int currentSaveVersion, string gameVersion)
        {
            if (string.IsNullOrEmpty(rootDir)) throw new ArgumentNullException(nameof(rootDir));
            if (currentSaveVersion < 1) throw new ArgumentOutOfRangeException(nameof(currentSaveVersion));

            _rootDir = rootDir;
            _currentSaveVersion = currentSaveVersion;
            _gameVersion = gameVersion ?? "0.0.0";
        }

        public void RegisterMigration(ISaveMigration migration)
        {
            if (migration == null) throw new ArgumentNullException(nameof(migration));
            _migrations[migration.FromVersion] = migration;
        }

        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        private string SlotPath(int slot) => Path.Combine(_rootDir, $"save_{slot}.wx");
        private string TempPath(int slot) => Path.Combine(_rootDir, $"save_{slot}.tmp");
        private string BackupPath(int slot) => Path.Combine(_rootDir, $"save_{slot}.bak");

        private void EnsureDir()
        {
            if (!Directory.Exists(_rootDir)) Directory.CreateDirectory(_rootDir);
        }

        // ------------------------------------------------------------------
        // 写入
        // ------------------------------------------------------------------

        /// <summary>标记待写入（延迟落盘）。适合高频变更的场景。</summary>
        public void MarkDirty(int slot, byte[] payload)
        {
            _dirtyBuffer[slot] = payload;
        }

        /// <summary>把标记过的槽位落盘。建议由外部定时器（如 30 秒）与关键节点调用。</summary>
        public void FlushDirty()
        {
            if (_dirtyBuffer.Count == 0) return;

            // 先复制再清空：万一写入抛异常，不至于丢失待写数据
            var pending = new Dictionary<int, byte[]>(_dirtyBuffer);
            foreach (var kv in pending)
            {
                Save(kv.Key, kv.Value);
                _dirtyBuffer.Remove(kv.Key);
            }
        }

        /// <summary>立即写入存档（原子操作）。</summary>
        public void Save(int slot, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            EnsureDir();
            byte[] fileBytes = SerializeFile(_currentSaveVersion, _gameVersion, payload);

            string finalPath = SlotPath(slot);
            string tmpPath = TempPath(slot);
            string bakPath = BackupPath(slot);

            // 1. 完整写入临时文件
            File.WriteAllBytes(tmpPath, fileBytes);

            // 2. 现有存档转为备份（保留上一版，作为回滚底线）
            if (File.Exists(finalPath))
            {
                if (File.Exists(bakPath))
                {
                    try { File.Delete(bakPath); }
                    catch (IOException) { /* 备份删除失败不阻断主流程 */ }
                }
                File.Move(finalPath, bakPath);
            }

            // 3. 临时文件转正
            File.Move(tmpPath, finalPath);
        }

        // ------------------------------------------------------------------
        // 读取
        // ------------------------------------------------------------------

        public bool HasSave(int slot)
            => File.Exists(SlotPath(slot)) || File.Exists(BackupPath(slot));

        /// <summary>
        /// 读取存档。流程：正式档 → 校验失败则备份档 → 校验失败才返回失败。
        /// 之后自动执行版本迁移链。
        /// </summary>
        public SaveReadResult Load(int slot)
        {
            // ---- 1. 尝试正式档 ----
            var result = TryReadFile(SlotPath(slot), out bool corrupted);
            if (result.Success)
            {
                result.RecoveredFromBackup = false;
                return ApplyMigrations(result);
            }

            // ---- 2. 正式档损坏或不存在，尝试备份 ----
            if (corrupted || !File.Exists(SlotPath(slot)))
            {
                var backup = TryReadFile(BackupPath(slot), out _);
                if (backup.Success)
                {
                    backup.RecoveredFromBackup = true;
                    return ApplyMigrations(backup);
                }
            }

            return result.Success ? result : SaveReadResult.Fail(
                File.Exists(SlotPath(slot))
                    ? $"存档损坏且无可用备份：{result.Error}"
                    : "存档不存在");
        }

        /// <summary>读取单个文件并做完整性校验。corrupted 表示"文件存在但内容不合法"。</summary>
        private SaveReadResult TryReadFile(string path, out bool corrupted)
        {
            corrupted = false;

            if (!File.Exists(path)) return SaveReadResult.Fail("文件不存在");

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                return SaveReadResult.Fail($"读取失败：{ex.Message}");
            }

            if (bytes.Length < 20)
            {
                corrupted = true;
                return SaveReadResult.Fail("文件过短，可能写入被中断");
            }

            try
            {
                var reader = new WXBinaryReader(bytes);

                uint magic = reader.ReadUInt32();
                if (magic != Magic)
                {
                    corrupted = true;
                    return SaveReadResult.Fail("文件标识错误");
                }

                int saveVersion = reader.ReadInt32();
                string gameVersion = reader.ReadString();
                int payloadLen = reader.ReadInt32();
                uint expectedCrc = reader.ReadUInt32();

                if (payloadLen < 0 || reader.Position + payloadLen > bytes.Length)
                {
                    corrupted = true;
                    return SaveReadResult.Fail(
                        $"数据长度异常：声明 {payloadLen} 字节，实际剩余 {bytes.Length - reader.Position}");
                }

                var payload = new byte[payloadLen];
                Buffer.BlockCopy(bytes, reader.Position, payload, 0, payloadLen);

                uint actualCrc = WXChecksum.Compute(payload);
                if (actualCrc != expectedCrc)
                {
                    corrupted = true;
                    return SaveReadResult.Fail(
                        $"校验和不匹配（期望 0x{expectedCrc:X8}，实际 0x{actualCrc:X8}），" +
                        $"文件可能在写入中被中断");
                }

                if (saveVersion < 1)
                {
                    corrupted = true;
                    return SaveReadResult.Fail($"存档版本非法：{saveVersion}");
                }

                return SaveReadResult.Ok(saveVersion, gameVersion, payload);
            }
            catch (Exception ex)
            {
                corrupted = true;
                return SaveReadResult.Fail($"解析异常：{ex.Message}");
            }
        }

        /// <summary>执行版本迁移链：v1 → v2 → ... → 当前版本。</summary>
        private SaveReadResult ApplyMigrations(SaveReadResult result)
        {
            if (result.SaveVersion > _currentSaveVersion)
            {
                return SaveReadResult.Fail(
                    $"存档版本（v{result.SaveVersion}）高于当前客户端（v{_currentSaveVersion}）。" +
                    $"请更新游戏后再读取该存档。");
            }

            byte[] payload = result.Payload;
            int version = result.SaveVersion;
            int guard = 0;

            while (version < _currentSaveVersion)
            {
                if (++guard > 64)
                {
                    return SaveReadResult.Fail("迁移链疑似成环，已中止。请检查各 Migration 的 FromVersion/ToVersion。");
                }

                if (!_migrations.TryGetValue(version, out var migration))
                {
                    return SaveReadResult.Fail(
                        $"缺少 v{version} → v{version + 1} 的迁移器。" +
                        $"请实现 ISaveMigration 并注册到 SaveService。");
                }

                try
                {
                    payload = migration.Migrate(payload);
                }
                catch (Exception ex)
                {
                    return SaveReadResult.Fail($"v{version} → v{version + 1} 迁移失败：{ex.Message}");
                }

                version = migration.ToVersion;
            }

            result.Payload = payload;
            result.SaveVersion = version;
            return result;
        }

        // ------------------------------------------------------------------
        // 删除 / 工具
        // ------------------------------------------------------------------

        public void Delete(int slot)
        {
            TryDelete(SlotPath(slot));
            TryDelete(BackupPath(slot));
            TryDelete(TempPath(slot));
            _dirtyBuffer.Remove(slot);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* 忽略：下次写入时会覆盖 */ }
        }

        /// <summary>组装存档文件字节流（含头与校验和）。</summary>
        public static byte[] SerializeFile(int saveVersion, string gameVersion, byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            uint crc = WXChecksum.Compute(payload);

            using (var ms = new MemoryStream(payload.Length + 64))
            {
                var writer = new WXBinaryWriter(ms);
                writer.Write(Magic);
                writer.Write(saveVersion);
                writer.Write(gameVersion);
                writer.Write(payload.Length);
                writer.Write(crc);
                writer.Flush();

                ms.Write(payload, 0, payload.Length);
                return ms.ToArray();
            }
        }
    }

    // ========================================================================
    //  迁移器示例
    //  ------------------------------------------------------------------------
    //  场景：v1 存档里有 gold 和 level；v2 新增了 intimacy（亲密度）字段。
    //
    //  关键点：迁移器只做"旧格式 → 新格式"的字节级转换，
    //         不需要知道业务类长什么样，也不需要反序列化成对象。
    //         这保证了迁移逻辑不随业务类的后续变更而失效
    //         （如果用业务类做迁移，第 10 版时你根本反序列化不出第 1 版的对象）。
    // ========================================================================
    public sealed class SaveMigrationV1ToV2 : ISaveMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;

        public byte[] Migrate(byte[] oldPayload)
        {
            var reader = new WXBinaryReader(oldPayload);

            // ---- 按 v1 的字段顺序读 ----
            int gold = reader.ReadInt32();
            int level = reader.ReadInt32();

            // ---- 按 v2 的字段顺序写（末尾追加新字段的默认值）----
            using (var ms = new MemoryStream(oldPayload.Length + 8))
            {
                var writer = new WXBinaryWriter(ms);
                writer.Write(gold);
                writer.Write(level);
                writer.Write(0);        // v2 新增：亲密度，老存档默认 0
                writer.Flush();
                return ms.ToArray();
            }
        }
    }
}
