using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecoVibes.Editor
{
    [CustomEditor(typeof(RecoVibesWidget))]
    class RecoVibesWidgetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var widget = (RecoVibesWidget)target;
            if (string.IsNullOrWhiteSpace(widget.dataId))
            {
                EditorGUILayout.HelpBox("Paste your app's Data Id from the RecoVibes dashboard (your app → Install tab).", MessageType.Info);
                if (GUILayout.Button("Open the RecoVibes dashboard")) Application.OpenURL("https://www.recovibes.com/dashboard");
            }
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Size this rectangle where you want recommendations - the widget fills it. Views in the editor never earn points.", MessageType.None);
        }
    }

    static class RecoVibesMenu
    {
        [MenuItem("GameObject/UI/RecoVibes Recommendations", false, 2100)]
        static void Create(MenuCommand command)
        {
            var parent = command.context as GameObject;
            var canvas = parent != null ? parent.GetComponentInParent<Canvas>() : FindCanvas();
            if (canvas == null) canvas = CreateCanvas();
            if (parent == null || parent.GetComponentInParent<Canvas>() == null) parent = canvas.gameObject;

            var go = new GameObject("RecoVibes", typeof(RectTransform), typeof(RecoVibesWidget));
            Undo.RegisterCreatedObjectUndo(go, "Create RecoVibes Recommendations");
            GameObjectUtility.SetParentAndAlign(go, parent);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900, 420);
            rt.anchoredPosition = Vector2.zero;
            EnsureEventSystem();
            Selection.activeGameObject = go;
        }

        static Canvas FindCanvas()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindAnyObjectByType<Canvas>();
#else
            return UnityEngine.Object.FindObjectOfType<Canvas>();
#endif
        }

        static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static void EnsureEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
#else
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
#endif
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var inputSystem = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            go.AddComponent(inputSystem ?? typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }
    }
}
