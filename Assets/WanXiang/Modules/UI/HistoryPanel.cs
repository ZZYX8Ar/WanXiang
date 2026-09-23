// ============================================================================
//  Panel_History —— 历程（最近 10 局）
//  ---------------------------------------------------------------------------
//  记录三项（用户定案）：最远幕数 / 上场异兽 / 综合战力；外加那一局的编队码
//  （ShareCode），可复制发给朋友做 AI 对战（PvpMatch）。
//
//  列表由代码按 History 填充（模板 Item_Record 来自 prefab）。
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WanXiang.Framework.UI;

namespace WanXiang.Modules.UI
{
    [UIPanel("Panel_History", Layer = UILayer.Normal, CachePolicy = UICachePolicy.Cached,
             CloseOnMaskClick = false)]
    public sealed class HistoryPanel : UIPanelBase
    {
        [SerializeField] private TMP_Text _tmpTitle;          // Tmp_Title
        [SerializeField] private TMP_Text _tmpEmpty;          // Tmp_Empty（无记录时提示）
        [SerializeField] private RectTransform _listContent;  // Scroll_List/Viewport/Content
        [SerializeField] private RectTransform _itemTemplate; // Item_Record（模板，默认隐藏）
        [SerializeField] private TMP_Text _tmpCode;           // Tmp_Code（选中的编队码）
        [SerializeField] private Button _btnBack;             // Btn_Back

        private readonly List<RectTransform> _items = new List<RectTransform>();

        protected override void OnCreate()
        {
            if (_btnBack != null) _btnBack.onClick.AddListener(CloseSelf);
            if (_itemTemplate != null) _itemTemplate.gameObject.SetActive(false);
        }

        protected override UniTask OnOpenAsync(object payload)
        {
            Render();
            return UniTask.CompletedTask;
        }

        private void Render()
        {
            var meta = WanXiang.Meta.MetaStore.Ensure();
            int n = meta != null ? meta.HistActs.Count : 0;

            if (_tmpTitle != null) _tmpTitle.text = "历程（最近 " + n + " 局）";
            if (_tmpEmpty != null) _tmpEmpty.gameObject.SetActive(n == 0);

            // 清旧条目
            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i] != null) { _items[i].gameObject.SetActive(false); Destroy(_items[i].gameObject); }
            _items.Clear();

            if (_listContent == null || _itemTemplate == null) return;

            // 倒序：最新的在最上
            for (int k = 0; k < n; k++)
            {
                int i = n - 1 - k;
                var rt = Instantiate(_itemTemplate, _listContent);
                rt.gameObject.SetActive(true);
                rt.name = "Rec_" + i;
                rt.anchoredPosition = new Vector2(8f, -8f - k * 132f);
                rt.sizeDelta = new Vector2(-16f, 124f);

                var info = rt.Find("Tmp_Info") != null ? rt.Find("Tmp_Info").GetComponent<TMP_Text>() : null;
                if (info != null)
                    info.text = "第 " + meta.HistActs[i] + " 幕　·　战力 " + meta.HistPowers[i] +
                                "　·　" + meta.HistTimes[i];

                var beasts = rt.Find("Tmp_Beasts") != null ? rt.Find("Tmp_Beasts").GetComponent<TMP_Text>() : null;
                if (beasts != null) beasts.text = meta.HistBeasts[i];

                var copy = rt.Find("Btn_Copy") != null ? rt.Find("Btn_Copy").GetComponent<Button>() : null;
                if (copy != null)
                {
                    string code = meta.HistCodes[i];
                    bool hasCode = !string.IsNullOrEmpty(code);
                    copy.interactable = hasCode;
                    copy.onClick.AddListener(() =>
                    {
                        GUIUtility.systemCopyBuffer = code;      // 复制到系统剪贴板
                        if (_tmpCode != null) _tmpCode.text = "已复制编队码（" + code.Length + " 字符）：\n" + code;
                        Debug.Log("[HistoryPanel] 已复制编队码：" + code);
                    });
                }

                _items.Add(rt);
            }

            if (_listContent.sizeDelta.y < _items.Count * 132f + 16f)
                _listContent.sizeDelta = new Vector2(_listContent.sizeDelta.x, _items.Count * 132f + 16f);
        }
    }
}
