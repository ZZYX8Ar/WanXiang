// 双击查看用的数据（UI 层传递，纯数据）
namespace WanXiang.Modules.UI
{
    public sealed class UnitInspectData
    {
        public string Name;
        public string Stats;      // 阵营 / HP / 盾 / 元素
        public string Statuses;   // 状态列表
        public string Skills;     // 技能与效果（多行）
        public string Mechanic;   // 机制标签
        public UnityEngine.Color Accent = UnityEngine.Color.white;
    }
}
