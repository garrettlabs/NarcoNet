using NarcoNet.Utilities;

using UnityEngine;

namespace NarcoNet.UI;

public class ProgressWindow(string title, string message)
{
    private readonly CancelButton _cancelButton = new();
    private readonly InfoBox _infoBox = new(title, message);
    private readonly ProgressBar _progressBar = new();
    private readonly ByteProgressBar _byteProgressBar = new();
    public bool Active { get; private set; }

    public void Show()
    {
        Active = true;
    }

    public void Hide()
    {
        Active = false;
    }

    public void Draw(int progressValue, int progressMax, long bytesDownloaded, long bytesTotal, Action? cancelAction)
    {
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        const float windowWidth = 640f;
        const float windowHeight = 640f;

        GUILayout.BeginArea(new Rect((screenWidth - windowWidth) / 2f, (screenHeight - windowHeight) / 2f, windowWidth,
            windowHeight));
        GUILayout.BeginVertical();
        GUILayout.FlexibleSpace();

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _infoBox.Draw(new Vector2(480f, 240f));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(64f);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        _progressBar.Draw(new Vector2(windowWidth, 32f), progressValue, progressMax);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (bytesTotal > 0)
        {
            GUILayout.Space(16f);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            _byteProgressBar.Draw(new Vector2(windowWidth, 24f), bytesDownloaded, bytesTotal);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(64f);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (cancelAction != null)
        {
            if (_cancelButton.Draw(new Vector2(196f, 48f)))
            {
                cancelAction();
            }
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    internal static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824L => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576L => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024L => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    private class ProgressBar : Bordered
    {
        private const int BorderThickness = 2;
        private const int CornerRadius = 6;
        private float _animatedProgress;

        public void Draw(Vector2 size, int currentValue, int maxValue)
        {
            Rect borderRect = GUILayoutUtility.GetRect(size.x, size.y);

            // Draw subtle shadow
            Utility.DrawShadow(borderRect, 0, 2, 4);

            // Draw rounded border
            DrawBorder(borderRect, BorderThickness, Colors.PrimaryLight, CornerRadius);

            Rect progressRect =
                new(
                    borderRect.x + BorderThickness,
                    borderRect.y + BorderThickness,
                    borderRect.width - 2 * BorderThickness,
                    borderRect.height - 2 * BorderThickness
                );

            // Draw dark background with gradient
            DrawRoundedBox(progressRect, Colors.Dark.SetAlpha(0.5f), CornerRadius - BorderThickness);

            float targetRatio = maxValue > 0 ? (float)currentValue / maxValue : 0f;
            // Smooth animation
            _animatedProgress = Mathf.Lerp(_animatedProgress, targetRatio, Time.deltaTime * 5f);

            if (_animatedProgress > 0.01f)
            {
                Rect fillRect = new(progressRect.x, progressRect.y, progressRect.width * _animatedProgress,
                    progressRect.height);

                // Draw gradient fill with modern look
                DrawGradientBox(fillRect, Colors.Primary, Colors.PrimaryLight, true, CornerRadius - BorderThickness);

                // Add shine effect on top
                Rect shineRect = new(fillRect.x, fillRect.y, fillRect.width, fillRect.height * 0.5f);
                GUI.DrawTexture(shineRect, Utility.GetTexture(Colors.Highlight), ScaleMode.StretchToFill, true);
            }

            // Text with better contrast
            GUIStyle style = new()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Colors.White }
            };

            // Add text shadow for better readability
            GUIStyle shadowStyle = new()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Colors.Dark }
            };

            string progressText = maxValue > 0
                ? $"{currentValue} / {maxValue} ({(float)currentValue / maxValue:P1})"
                : $"{currentValue} / {maxValue}";
            Rect shadowRect = new(progressRect.x + 1, progressRect.y + 1, progressRect.width, progressRect.height);
            GUI.Label(shadowRect, progressText, shadowStyle);
            GUI.Label(progressRect, progressText, style);
        }
    }

    private class ByteProgressBar : Bordered
    {
        private const int BorderThickness = 1;
        private const int CornerRadius = 4;
        private float _animatedProgress;

        public void Draw(Vector2 size, long currentBytes, long totalBytes)
        {
            Rect borderRect = GUILayoutUtility.GetRect(size.x, size.y);

            // Draw rounded border
            DrawBorder(borderRect, BorderThickness, Colors.PrimaryLight.SetAlpha(0.6f), CornerRadius);

            Rect progressRect =
                new(
                    borderRect.x + BorderThickness,
                    borderRect.y + BorderThickness,
                    borderRect.width - 2 * BorderThickness,
                    borderRect.height - 2 * BorderThickness
                );

            // Draw dark background
            DrawRoundedBox(progressRect, Colors.Dark.SetAlpha(0.4f), CornerRadius - BorderThickness);

            float targetRatio = totalBytes > 0 ? (float)currentBytes / totalBytes : 0f;
            _animatedProgress = Mathf.Lerp(_animatedProgress, targetRatio, Time.deltaTime * 5f);

            if (_animatedProgress > 0.01f)
            {
                Rect fillRect = new(progressRect.x, progressRect.y, progressRect.width * _animatedProgress,
                    progressRect.height);

                DrawGradientBox(fillRect, Colors.Primary.SetAlpha(0.7f), Colors.PrimaryLight.SetAlpha(0.7f), true,
                    CornerRadius - BorderThickness);
            }

            // Text
            GUIStyle style = new()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Colors.White }
            };

            GUIStyle shadowStyle = new()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Colors.Dark }
            };

            string progressText = totalBytes > 0
                ? $"{FormatBytes(currentBytes)} / {FormatBytes(totalBytes)} ({targetRatio:P1})"
                : "";
            Rect shadowRect = new(progressRect.x + 1, progressRect.y + 1, progressRect.width, progressRect.height);
            GUI.Label(shadowRect, progressText, shadowStyle);
            GUI.Label(progressRect, progressText, style);
        }
    }
}

internal class CancelButton : Bordered
{
    private const int BorderThickness = 2;
    private const int CornerRadius = 8;

    private bool _active;
    private float _hoverTransition;

    public bool Draw(Vector2 size)
    {
        Rect borderRect = GUILayoutUtility.GetRect(size.x, size.y);

        Rect buttonRect =
            new(
                borderRect.x + BorderThickness,
                borderRect.y + BorderThickness,
                borderRect.width - 2 * BorderThickness,
                borderRect.height - 2 * BorderThickness
            );

        bool hovered = buttonRect.Contains(Event.current.mousePosition);

        // Smooth hover transition
        float targetTransition = hovered ? 1f : 0f;
        _hoverTransition = Mathf.Lerp(_hoverTransition, targetTransition, Time.deltaTime * 10f);

        if (hovered && Event.current.type == EventType.MouseDown)
        {
            _active = true;
        }

        if (_active && Event.current.type == EventType.MouseUp)
        {
            _active = false;
        }

        // Modern color selection
        Color buttonColor;
        if (_active)
        {
            buttonColor = Colors.SecondaryDark;
        }
        else
        {
            buttonColor = Color.Lerp(Colors.SecondaryDark, Colors.SecondaryLight, _hoverTransition);
        }

        Color textColor = Colors.White;

        // Draw shadow for depth
        if (!_active)
        {
            Utility.DrawShadow(borderRect, 0, _active ? 1 : 3, _active ? 4 : 6);
        }

        // Draw rounded border
        DrawBorder(borderRect, BorderThickness, Colors.SecondaryDark, CornerRadius);

        // Draw gradient button background
        DrawGradientBox(buttonRect, buttonColor, buttonColor.SetAlpha(buttonColor.a * 0.85f), false,
            CornerRadius - BorderThickness);

        // Add highlight effect at top
        if (!_active)
        {
            Rect highlightRect = new(buttonRect.x, buttonRect.y, buttonRect.width, buttonRect.height * 0.4f);
            GUI.DrawTexture(highlightRect, Utility.GetTexture(Colors.Highlight), ScaleMode.StretchToFill, true);
        }

        return GUI.Button(
            buttonRect,
            new GUIContent("CANCEL UPDATE"),
            new GUIStyle
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = textColor }
            }
        );
    }
}
