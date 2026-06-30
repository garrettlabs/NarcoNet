using NarcoNet.Utilities;
using UnityEngine;

namespace NarcoNet.UI;

public enum DiagnosticState
{
    Pending,
    InProgress,
    Done,
    Error
}

public class DiagnosticStep(string label, string status, DiagnosticState state)
{
    public string Label { get; set; } = label;
    public string Status { get; set; } = status;
    public DiagnosticState State { get; set; } = state;
}

public class DiagnosticWindow : Bordered
{
    private const float WindowWidth = 360f;
    private const float Padding = 12f;
    private const float LineHeight = 22f;
    private const float HeaderHeight = 28f;
    private const float CloseButtonSize = 20f;
    private const int CornerRadius = 8;
    private const int BorderThickness = 1;

    private readonly List<string> _stepOrder = [];
    private readonly Dictionary<string, DiagnosticStep> _steps = [];

    private const float ButtonHeight = 26f;
    private const float ButtonSpacing = 8f;

    public bool Active { get; private set; }
    public Action? OnShowFilesClicked { get; set; }

    public void Show()
    {
        Active = true;
    }

    public void Hide()
    {
        Active = false;
    }

    public void UpdateStep(string key, string status, DiagnosticState state)
    {
        if (_steps.TryGetValue(key, out DiagnosticStep? step))
        {
            step.Status = status;
            step.State = state;
        }
        else
        {
            _steps[key] = new DiagnosticStep(status, status, state);
            _stepOrder.Add(key);
        }
    }

    public void Clear()
    {
        _steps.Clear();
        _stepOrder.Clear();
    }

    public void Draw()
    {
        float buttonArea = OnShowFilesClicked != null ? ButtonHeight + ButtonSpacing : 0f;
        float windowHeight = HeaderHeight + Padding * 2 + _stepOrder.Count * LineHeight + buttonArea + Padding;
        float x = Screen.width - WindowWidth - 16f;
        float y = Screen.height - windowHeight - 16f;

        Rect windowRect = new(x, y, WindowWidth, windowHeight);

        // Semi-transparent background
        DrawRoundedBox(windowRect, Colors.Dark.SetAlpha(0.85f), CornerRadius);
        DrawBorder(windowRect, BorderThickness, Colors.PrimaryLight.SetAlpha(0.4f), CornerRadius);

        // Header
        Rect headerRect = new(windowRect.x + Padding, windowRect.y + Padding, windowRect.width - Padding * 2 - CloseButtonSize - 4f, HeaderHeight);
        GUI.Label(headerRect, "Sync Diagnostic", new GUIStyle
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Colors.PrimaryLight }
        });

        // Close button
        Rect closeBtnRect = new(windowRect.xMax - Padding - CloseButtonSize, windowRect.y + Padding, CloseButtonSize, CloseButtonSize);
        if (GUI.Button(closeBtnRect, "X", new GUIStyle
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Colors.Grey }
        }))
        {
            Hide();
        }

        // Step lines
        float stepY = windowRect.y + Padding + HeaderHeight;
        foreach (string key in _stepOrder)
        {
            if (!_steps.TryGetValue(key, out DiagnosticStep? step))
                continue;

            Rect lineRect = new(windowRect.x + Padding, stepY, windowRect.width - Padding * 2, LineHeight);

            Color stateColor = step.State switch
            {
                DiagnosticState.Pending => Colors.Grey,
                DiagnosticState.InProgress => Colors.Warning,
                DiagnosticState.Done => Colors.Success,
                DiagnosticState.Error => Colors.Error,
                _ => Colors.Grey
            };

            string prefix = step.State switch
            {
                DiagnosticState.Pending => "○",
                DiagnosticState.InProgress => "►",
                DiagnosticState.Done => "✓",
                DiagnosticState.Error => "✗",
                _ => "○"
            };

            GUI.Label(lineRect, $"{prefix} {step.Status}", new GUIStyle
            {
                fontSize = 13,
                normal = { textColor = stateColor }
            });

            stepY += LineHeight;
        }

        // "Show File Comparison" button
        if (OnShowFilesClicked != null)
        {
            float btnY = stepY + ButtonSpacing;
            Rect btnRect = new(windowRect.x + Padding, btnY, windowRect.width - Padding * 2, ButtonHeight);

            DrawRoundedBox(btnRect, Colors.PrimaryDark.SetAlpha(0.5f), 4);

            if (GUI.Button(btnRect, "Show File Comparison", new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Colors.PrimaryLight },
                hover = { textColor = Colors.White }
            }))
            {
                OnShowFilesClicked();
            }
        }
    }
}
