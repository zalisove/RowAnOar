using Jotunn.Managers;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace RowAnOar
{
    public class RowingUIManager
    {
        private GameObject rowingUI;
        private RectTransform progressBarBg;
        private RectTransform progressBarCurrent;
        private RectTransform progressBarTarget;
        private Text instructionText;
        private Text streakText;

        // Cached values for UI optimization
        private float lastUICurrentPosition = -1f;
        private float lastUITargetPosition = -1f;
        private int lastUIStreak = -1;

        public bool IsRowingUIActive() => rowingUI != null && rowingUI.activeSelf;

        public void ShowRowingUI() 
        {
            if (rowingUI != null)
                rowingUI.SetActive(true);
        }

        public void HideRowingUI()
        {
            if (rowingUI != null)
                rowingUI.SetActive(false);
        }

        public void SetupRowingUI()
        {
            rowingUI = GUIManager.Instance.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0.5f, 0.1f),
                anchorMax: new Vector2(0.5f, 0.1f),
                position: new Vector2(0f, 0f),
                width: 400f,
                height: 120f,
                draggable: true
            );
            rowingUI.name = "RowingMinigameUI";
            rowingUI.SetActive(false);

            CreateUIElements();

            // Initialize cached values
            lastUICurrentPosition = 0f;
            lastUITargetPosition = 0f;
            lastUIStreak = 0;
        }

        private void CreateUIElements()
        {
            // Title
            GameObject titleGO = GUIManager.Instance.CreateText(
                text: "Rowing Minigame",
                parent: rowingUI.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(75f, 0f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 18,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 300f,
                height: 30f,
                addContentSizeFitter: false
            );

            // Progress bar background
            GameObject progressBarBgGO = new GameObject("ProgressBarBg", typeof(RectTransform), typeof(Image));
            progressBarBgGO.transform.SetParent(rowingUI.transform, false);

            progressBarBg = progressBarBgGO.GetComponent<RectTransform>();
            progressBarBg.anchorMin = new Vector2(0.5f, 0.5f);
            progressBarBg.anchorMax = new Vector2(0.5f, 0.5f);
            progressBarBg.sizeDelta = new Vector2(300f, 30f);
            progressBarBg.anchoredPosition = new Vector2(0f, 0f);
            progressBarBgGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Current marker
            GameObject currentMarkerGO = new GameObject("CurrentMarker", typeof(RectTransform), typeof(Image));
            currentMarkerGO.transform.SetParent(progressBarBg, false);
            progressBarCurrent = currentMarkerGO.GetComponent<RectTransform>();
            progressBarCurrent.anchorMin = new Vector2(0f, 0f);
            progressBarCurrent.anchorMax = new Vector2(0f, 1f);
            progressBarCurrent.sizeDelta = new Vector2(10f, 0f);
            progressBarCurrent.anchoredPosition = new Vector2(150f, 0f);
            currentMarkerGO.GetComponent<Image>().color = Color.yellow;

            // Target marker
            GameObject targetMarkerGO = new GameObject("TargetMarker", typeof(RectTransform), typeof(Image));
            targetMarkerGO.transform.SetParent(progressBarBg, false);
            progressBarTarget = targetMarkerGO.GetComponent<RectTransform>();
            progressBarTarget.anchorMin = new Vector2(0f, 0f);
            progressBarTarget.anchorMax = new Vector2(0f, 1f);
            progressBarTarget.sizeDelta = new Vector2(20f, 0f);
            progressBarTarget.anchoredPosition = new Vector2(75f, 0f);
            targetMarkerGO.GetComponent<Image>().color = new Color(0f, 1f, 0f, 0.5f);

            // Instruction text
            GameObject instructionTextGO = GUIManager.Instance.CreateText(
                text: $"Press {RowAnOar.Instance.RowingKey} when markers align!",
                parent: rowingUI.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(75f, 30f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 14,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 350f,
                height: 30f,
                addContentSizeFitter: false
            );
            instructionText = instructionTextGO.GetComponent<Text>();

            // Streak text
            GameObject streakTextGO = GUIManager.Instance.CreateText(
                text: "Streak: 0",
                parent: rowingUI.transform,
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                position: new Vector2(150f, 10f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 12,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 200f,
                height: 20f,
                addContentSizeFitter: false
            );
            streakText = streakTextGO.GetComponent<Text>();
        }

        public void UpdateRowingUI(float currentPosition, float targetPosition, int streak)
        {
            if (rowingUI == null || !rowingUI.activeSelf) return;

            // Check if we need to update the UI
            bool needsUpdate =
                Math.Abs(lastUICurrentPosition - currentPosition) > 0.01f ||
                Math.Abs(lastUITargetPosition - targetPosition) > 0.01f ||
                lastUIStreak != streak;

            if (!needsUpdate) return;

            // Update cached values
            lastUICurrentPosition = currentPosition;
            lastUITargetPosition = targetPosition;
            lastUIStreak = streak;

            float barWidth = progressBarBg.rect.width;
            progressBarCurrent.anchoredPosition = new Vector2((currentPosition * barWidth), 0);
            progressBarTarget.anchoredPosition = new Vector2((targetPosition * barWidth), 0);
            instructionText.text = $"Press {RowAnOar.Instance.RowingKey} when markers align!";
            streakText.text = $"Streak: {streak}";
        }
    }
}
