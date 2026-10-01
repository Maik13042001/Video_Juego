using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Vista de un slot. Puedes armar tu propio prefab y asignar las referencias,
    /// o usar CreateDefault() (lo hace InventoryUI automáticamente si no hay prefab).
    /// </summary>
    public class InventorySlotUI : MonoBehaviour
    {
        [SerializeField] Image background;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text quantityText;
        [SerializeField] TMP_Text hotkeyText;
        [SerializeField] TMP_Text nameText;   // se muestra solo si el item no tiene icono

        [SerializeField] Color baseColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] Color flashColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        Coroutine _flash;

        public void SetHotkey(int number)
        {
            if (hotkeyText != null) hotkeyText.text = number.ToString();
        }

        public void SetData(InventorySlot slot)
        {
            bool empty = slot == null || slot.IsEmpty;

            if (icon != null)
            {
                icon.enabled = !empty;
                if (!empty)
                {
                    icon.sprite = slot.item.icon;
                    icon.color = slot.item.icon != null ? Color.white : slot.item.fallbackColor;
                }
            }

            if (nameText != null)
                nameText.text = (!empty && slot.item.icon == null) ? slot.item.displayName : string.Empty;

            if (quantityText != null)
                quantityText.text = (!empty && slot.quantity > 1) ? slot.quantity.ToString() : string.Empty;
        }

        /// <summary>Destello breve para que el jugador note que recibió un objeto.</summary>
        public void Flash()
        {
            if (background == null || !isActiveAndEnabled) return;
            if (_flash != null) StopCoroutine(_flash);
            _flash = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            const float duration = 0.45f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                background.color = Color.Lerp(flashColor, baseColor, t / duration);
                yield return null;
            }
            background.color = baseColor;
            _flash = null;
        }

        void OnDisable()
        {
            _flash = null;
            if (background != null) background.color = baseColor;
        }

        // =====================================================================
        // Construcción por código (UI por defecto, sin prefab)
        // =====================================================================
        public static InventorySlotUI CreateDefault(Transform parent, Vector2 size)
        {
            var go = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            var view = go.AddComponent<InventorySlotUI>();
            view.background = go.GetComponent<Image>();
            view.background.color = view.baseColor;
            view.background.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)iconGo.transform, 10f);
            view.icon = iconGo.GetComponent<Image>();
            view.icon.preserveAspect = true;
            view.icon.raycastTarget = false;
            view.icon.enabled = false;

            view.nameText = CreateText("Name", go.transform, 12f, TextAlignmentOptions.Center);
            Stretch((RectTransform)view.nameText.transform, 6f);

            view.quantityText = CreateText("Qty", go.transform, 20f, TextAlignmentOptions.BottomRight);
            Stretch((RectTransform)view.quantityText.transform, 5f);

            view.hotkeyText = CreateText("Hotkey", go.transform, 14f, TextAlignmentOptions.TopLeft);
            view.hotkeyText.color = new Color(1f, 1f, 1f, 0.6f);
            Stretch((RectTransform)view.hotkeyText.transform, 5f);

            return view;
        }

        public static TextMeshProUGUI CreateText(string objectName, Transform parent, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        static void Stretch(RectTransform rt, float margin)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
        }
    }
}
