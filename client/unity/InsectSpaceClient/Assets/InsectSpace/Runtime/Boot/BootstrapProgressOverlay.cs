using UnityEngine;
using UnityEngine.UI;

namespace InsectSpace.Client
{
    [DisallowMultipleComponent]
    internal sealed class BootstrapProgressOverlay : MonoBehaviour
    {
        private InsectSpaceBootstrap bootstrap;
        private GameObject canvasObject;
        private Text status;
        private Slider progress;

        private void Awake()
        {
            bootstrap = GetComponent<InsectSpaceBootstrap>();
            canvasObject = new GameObject("InsectSpace Bootstrap Canvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            canvasObject.AddComponent<GraphicRaycaster>();
            var panel = new GameObject("Bootstrap Status");
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(520f, 130f);
            panel.AddComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.96f);
            status = MakeText(panel.transform, new Vector2(24f, -22f), new Vector2(472f, 54f), 18);
            var bar = new GameObject("Download Progress", typeof(RectTransform));
            bar.transform.SetParent(panel.transform, false);
            bar.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.2f);
            progress = bar.AddComponent<Slider>();
            progress.interactable = false;
            progress.minValue = 0f;
            progress.maxValue = 1f;
            var progressRect = progress.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0f, 0f);
            progressRect.anchorMax = new Vector2(1f, 0f);
            progressRect.offsetMin = new Vector2(24f, 24f);
            progressRect.offsetMax = new Vector2(-24f, 42f);
            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(bar.transform, false);
            fill.AddComponent<Image>().color = new Color(0.2f, 0.7f, 0.9f);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            progress.fillRect = fillRect;
        }

        private void Update()
        {
            if (bootstrap == null || canvasObject == null) return;
            status.text = bootstrap.LastError == null
                ? "INSECT SPACE\n" + bootstrap.ProgressStage + "  " + Mathf.RoundToInt(bootstrap.Progress * 100f) + "%"
                : "启动失败，请检查配置和资源后重启。";
            progress.value = Mathf.Clamp01(bootstrap.Progress);
            if (bootstrap.Ready && canvasObject != null)
            {
                Destroy(canvasObject);
                canvasObject = null;
                enabled = false;
            }
        }

        private static Text MakeText(Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var go = new GameObject("Status");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }
    }
}
