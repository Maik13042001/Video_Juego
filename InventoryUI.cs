using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Muestra el inventario siempre visible (hotbar) y se actualiza en tiempo real
    /// suscribiéndose a los eventos de PlayerInventory.
    ///
    /// Uso rápido: añade este componente a un GameObject vacío y dale Play. Con
    /// "Build Default Ui If Missing" activo, crea solo el Canvas, la barra y el texto de aviso.
    /// Si prefieres tu propio diseño, asigna Slots Root (+ Slot Prefab y Prompt Text).
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("Referencias (se buscan solas si se dejan vacías)")]
        [SerializeField] PlayerInventory inventory;
        [SerializeField] PlayerCollector collector;

        [Header("UI propia (opcional)")]
        [SerializeField] InventorySlotUI slotPrefab;
        [SerializeField] RectTransform slotsRoot;
        [SerializeField] TMP_Text promptText;

        [Header("UI por defecto")]
        [SerializeField] bool buildDefaultUiIfMissing = true;
        [SerializeField] Vector2 slotSize = new Vector2(72f, 72f);
        [SerializeField] float spacing = 8f;

        readonly List<InventorySlotUI> _views = new List<InventorySlotUI>();

        void Start()
        {
            if (inventory == null) inventory = PlayerInventory.Instance;
            if (collector == null) collector = FindCollector();

            if (inventory == null)
            {
                Debug.LogError("[InventoryUI] No hay PlayerInventory en la escena.", this);
                enabled = false;
                return;
            }

            if (slotsRoot == null)
            {
                if (!buildDefaultUiIfMissing)
                {
                    Debug.LogError("[InventoryUI] Falta asignar Slots Root.", this);
                    enabled = false;
                    return;
                }
                BuildDefaultCanvas();
            }

            BuildSlots();

            inventory.OnInventoryChanged += Refresh;
            inventory.OnItemAdded += HandleItemAdded;
            if (collector != null) collector.OnTargetChanged += HandleTargetChanged;

            Refresh();
        }

        void OnDestroy()
        {
            if (inventory != null)
            {
                inventory.OnInventoryChanged -= Refresh;
                inventory.OnItemAdded -= HandleItemAdded;
            }
            if (collector != null) collector.OnTargetChanged -= HandleTargetChanged;
        }

        void BuildSlots()
        {
            for (int i = 0; i < inventory.Capacity; i++)
            {
                var view = slotPrefab != null
                    ? Instantiate(slotPrefab, slotsRoot)
                    : InventorySlotUI.CreateDefault(slotsRoot, slotSize);

                view.SetHotkey(i + 1);
                _views.Add(view);
            }
        }

        void Refresh()
        {
            var slots = inventory.Slots;
            for (int i = 0; i < _views.Count && i < slots.Count; i++)
                _views[i].SetData(slots[i]);

            UpdatePrompt();   // el aviso "Inventario lleno" depende del estado del inventario
        }

        void HandleItemAdded(ItemData item, int amount, int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < _views.Count) _views[slotIndex].Flash();
        }

        void HandleTargetChanged(Pickup target) => UpdatePrompt();

        void UpdatePrompt()
        {
            if (promptText == null) return;

            var target = collector != null ? collector.CurrentTarget : null;
            if (target == null || target.Item == null)
            {
                promptText.text = string.Empty;
                return;
            }

            promptText.text = inventory.CanAdd(target.Item, target.Quantity)
                ? $"[E] Recoger {target.Item.displayName}"
                : "Inventario lleno";
        }

        // =====================================================================
        // Canvas generado por código
        // =====================================================================
        void BuildDefaultCanvas()
        {
            var canvasGo = new GameObject("InventoryCanvas (auto)",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Barra inferior con los slots
            var bar = new GameObject("Hotbar",
                typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            bar.transform.SetParent(canvasGo.transform, false);

            var barRt = (RectTransform)bar.transform;
            barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0f);
            barRt.pivot = new Vector2(0.5f, 0f);
            barRt.anchoredPosition = new Vector2(0f, 30f);

            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = bar.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            slotsRoot = barRt;

            // Texto de aviso ("[E] Recoger ...")
            if (promptText == null)
            {
                promptText = InventorySlotUI.CreateText("Prompt", canvasGo.transform, 30f, TextAlignmentOptions.Center);
                var rt = (RectTransform)promptText.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(900f, 60f);
                rt.anchoredPosition = new Vector2(0f, -180f);
            }
        }

        static PlayerCollector FindCollector()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<PlayerCollector>();
#else
            return FindObjectOfType<PlayerCollector>();
#endif
        }
    }
}
