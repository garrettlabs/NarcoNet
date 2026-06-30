using NarcoNet.Utilities;
using UnityEngine;

namespace NarcoNet.UI;

using SyncPathModFiles = Dictionary<string, Dictionary<string, ModFile>>;

public enum FileComparisonStatus
{
    Match,
    HashMismatch,
    ServerOnly,
    LocalOnly,
    DirectoryEntry
}

public record FileComparisonEntry(
    string FilePath,
    string? LocalHash,
    string? ServerHash,
    bool IsDirectory,
    FileComparisonStatus Status);

public class FileComparisonWindow : Bordered
{
    private const float WindowWidth = 1000f;
    private const float WindowHeight = 600f;
    private const float Padding = 12f;
    private const float HeaderHeight = 32f;
    private const float TabHeight = 28f;
    private const float RowHeight = 20f;
    private const float ColumnHeaderHeight = 24f;
    private const float FooterHeight = 28f;
    private const float CloseButtonSize = 20f;
    private const int CornerRadius = 8;
    private const int BorderThickness = 1;

    // Column widths
    private const float StatusColWidth = 80f;
    private const float HashColWidth = 70f;
    private const float PathColStart = Padding;

    private List<FileComparisonEntry> _allEntries = [];
    private List<FileComparisonEntry> _filteredEntries = [];
    private List<string> _syncPaths = [];
    private int _selectedTab; // 0 = All, 1+ = sync paths
    private Vector2 _scrollPosition;

    // Stats
    private int _matchCount;
    private int _mismatchCount;
    private int _serverOnlyCount;
    private int _localOnlyCount;

    public bool Active { get; private set; }

    public void Show()
    {
        _scrollPosition = Vector2.zero;
        Active = true;
    }

    public void Hide()
    {
        Active = false;
    }

    public void SetData(SyncPathModFiles local, SyncPathModFiles remote)
    {
        var merged = new SortedDictionary<string, FileComparisonEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var syncPathKvp in local)
        {
            foreach (var fileKvp in syncPathKvp.Value)
            {
                string key = $"{syncPathKvp.Key}/{fileKvp.Key}";
                ModFile modFile = fileKvp.Value;
                merged[key] = new FileComparisonEntry(
                    key,
                    modFile.Directory ? null : modFile.Hash,
                    null,
                    modFile.Directory,
                    modFile.Directory ? FileComparisonStatus.DirectoryEntry : FileComparisonStatus.LocalOnly);
            }
        }

        foreach (var syncPathKvp in remote)
        {
            foreach (var fileKvp in syncPathKvp.Value)
            {
                string key = $"{syncPathKvp.Key}/{fileKvp.Key}";
                ModFile modFile = fileKvp.Value;
                if (merged.TryGetValue(key, out var existing))
                {
                    if (modFile.Directory)
                    {
                        merged[key] = new FileComparisonEntry(
                            existing.FilePath, existing.LocalHash, null,
                            true, FileComparisonStatus.DirectoryEntry);
                    }
                    else
                    {
                        bool hashMatch = string.Equals(existing.LocalHash, modFile.Hash, StringComparison.OrdinalIgnoreCase);
                        merged[key] = new FileComparisonEntry(
                            existing.FilePath, existing.LocalHash, modFile.Hash,
                            existing.IsDirectory,
                            hashMatch ? FileComparisonStatus.Match : FileComparisonStatus.HashMismatch);
                    }
                }
                else
                {
                    merged[key] = new FileComparisonEntry(
                        key,
                        null,
                        modFile.Directory ? null : modFile.Hash,
                        modFile.Directory,
                        modFile.Directory ? FileComparisonStatus.DirectoryEntry : FileComparisonStatus.ServerOnly);
                }
            }
        }

        _allEntries = [.. merged.Values];

        // Extract unique sync paths for tabs
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in local.Keys) paths.Add(key);
        foreach (string key in remote.Keys) paths.Add(key);
        _syncPaths = [.. paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];

        // Compute stats
        _matchCount = _allEntries.Count(e => e.Status == FileComparisonStatus.Match);
        _mismatchCount = _allEntries.Count(e => e.Status == FileComparisonStatus.HashMismatch);
        _serverOnlyCount = _allEntries.Count(e => e.Status == FileComparisonStatus.ServerOnly);
        _localOnlyCount = _allEntries.Count(e => e.Status == FileComparisonStatus.LocalOnly);

        _selectedTab = 0;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_selectedTab == 0)
        {
            _filteredEntries = _allEntries;
        }
        else
        {
            string prefix = _syncPaths[_selectedTab - 1] + "/";
            _filteredEntries = _allEntries
                .Where(e => e.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _scrollPosition = Vector2.zero;
    }

    public void Draw()
    {
        float x = (Screen.width - WindowWidth) / 2f;
        float y = (Screen.height - WindowHeight) / 2f;
        Rect windowRect = new(x, y, WindowWidth, WindowHeight);

        // Background
        DrawRoundedBox(windowRect, Colors.Dark.SetAlpha(0.95f), CornerRadius);
        DrawBorder(windowRect, BorderThickness, Colors.PrimaryLight.SetAlpha(0.4f), CornerRadius);

        // Header
        Rect headerRect = new(windowRect.x + Padding, windowRect.y + Padding,
            windowRect.width - Padding * 2 - CloseButtonSize - 4f, HeaderHeight);
        GUI.Label(headerRect, "File Comparison", new GUIStyle
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Colors.PrimaryLight }
        });

        // Close button
        Rect closeBtnRect = new(windowRect.xMax - Padding - CloseButtonSize,
            windowRect.y + Padding, CloseButtonSize, CloseButtonSize);
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

        // Tab bar
        float tabY = windowRect.y + Padding + HeaderHeight + 4f;
        DrawTabs(windowRect.x + Padding, tabY, windowRect.width - Padding * 2);

        // Column headers
        float contentTop = tabY + TabHeight + 4f;
        float contentWidth = windowRect.width - Padding * 2;
        float pathColWidth = contentWidth - HashColWidth * 2 - StatusColWidth;

        Rect colHeaderRect = new(windowRect.x + Padding, contentTop, contentWidth, ColumnHeaderHeight);
        DrawRoundedBox(colHeaderRect, Colors.DarkMedium.SetAlpha(0.8f), 4);

        GUIStyle colHeaderStyle = new()
        {
            fontSize = 11,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Colors.Grey },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 0, 0)
        };

        GUI.Label(new Rect(colHeaderRect.x, colHeaderRect.y, pathColWidth, ColumnHeaderHeight),
            "File Path", colHeaderStyle);
        GUI.Label(new Rect(colHeaderRect.x + pathColWidth, colHeaderRect.y, HashColWidth, ColumnHeaderHeight),
            "Local", colHeaderStyle);
        GUI.Label(new Rect(colHeaderRect.x + pathColWidth + HashColWidth, colHeaderRect.y, HashColWidth, ColumnHeaderHeight),
            "Server", colHeaderStyle);
        GUI.Label(new Rect(colHeaderRect.x + pathColWidth + HashColWidth * 2, colHeaderRect.y, StatusColWidth, ColumnHeaderHeight),
            "Status", colHeaderStyle);

        // Scroll area
        float scrollTop = contentTop + ColumnHeaderHeight + 2f;
        float scrollBottom = windowRect.yMax - Padding - FooterHeight - 4f;
        float scrollAreaHeight = scrollBottom - scrollTop;
        Rect scrollRect = new(windowRect.x + Padding, scrollTop, contentWidth, scrollAreaHeight);

        GUI.DrawTexture(scrollRect, Utility.GetTexture(Color.black.SetAlpha(0.4f)), ScaleMode.StretchToFill, true, 0);

        float totalContentHeight = _filteredEntries.Count * RowHeight;

        GUIStyle scrollbarStyle = new(GUI.skin.verticalScrollbar)
        {
            normal = { background = Utility.GetTexture(Colors.GreyDark.SetAlpha(0.3f)) },
            active = { background = Utility.GetTexture(Colors.GreyDark.SetAlpha(0.3f)) },
            hover = { background = Utility.GetTexture(Colors.GreyDark.SetAlpha(0.3f)) },
            focused = { background = Utility.GetTexture(Colors.GreyDark.SetAlpha(0.3f)) }
        };
        GUIStyle scrollbarThumbStyle = new(GUI.skin.verticalScrollbarThumb)
        {
            normal = { background = Utility.GetTexture(Colors.Primary.SetAlpha(0.75f)) },
            active = { background = Utility.GetTexture(Colors.PrimaryDark.SetAlpha(0.85f)) },
            hover = { background = Utility.GetTexture(Colors.PrimaryLight.SetAlpha(0.85f)) },
            focused = { background = Utility.GetTexture(Colors.PrimaryDark.SetAlpha(0.85f)) }
        };

        GUISkin oldSkin = GUI.skin;
        GUI.skin.verticalScrollbarThumb = scrollbarThumbStyle;
        _scrollPosition = GUI.BeginScrollView(
            scrollRect,
            _scrollPosition,
            new Rect(0f, 0f, contentWidth - 16f, totalContentHeight),
            false,
            true,
            GUIStyle.none,
            scrollbarStyle);
        GUI.skin = oldSkin;

        // Draw visible rows only
        int firstVisible = Mathf.Max(0, (int)(_scrollPosition.y / RowHeight) - 1);
        int lastVisible = Mathf.Min(_filteredEntries.Count - 1,
            firstVisible + (int)(scrollAreaHeight / RowHeight) + 2);

        for (int i = firstVisible; i <= lastVisible; i++)
        {
            DrawRow(i, contentWidth - 16f, pathColWidth);
        }

        GUI.EndScrollView();

        // Footer stats
        Rect footerRect = new(windowRect.x + Padding, scrollBottom + 4f,
            contentWidth, FooterHeight);
        int totalFiles = _filteredEntries.Count(e => !e.IsDirectory);
        int filteredMatch = _filteredEntries.Count(e => e.Status == FileComparisonStatus.Match);
        int filteredMismatch = _filteredEntries.Count(e => e.Status == FileComparisonStatus.HashMismatch);
        int filteredServerOnly = _filteredEntries.Count(e => e.Status == FileComparisonStatus.ServerOnly);
        int filteredLocalOnly = _filteredEntries.Count(e => e.Status == FileComparisonStatus.LocalOnly);

        string statsText = _selectedTab == 0
            ? $"{_allEntries.Count} entries  |  {_matchCount} match  |  {_mismatchCount} modified  |  {_serverOnlyCount} server-only  |  {_localOnlyCount} local-only"
            : $"{totalFiles} files  |  {filteredMatch} match  |  {filteredMismatch} modified  |  {filteredServerOnly} server-only  |  {filteredLocalOnly} local-only";

        GUI.Label(footerRect, statsText, new GUIStyle
        {
            fontSize = 11,
            normal = { textColor = Colors.Grey },
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 0, 0, 0)
        });
    }

    private void DrawTabs(float x, float y, float width)
    {
        float tabX = x;
        float maxTabWidth = width;
        float usedWidth = 0f;

        // "All" tab
        usedWidth += DrawTab("All", tabX + usedWidth, y, _selectedTab == 0, () =>
        {
            _selectedTab = 0;
            ApplyFilter();
        });

        for (int i = 0; i < _syncPaths.Count; i++)
        {
            if (usedWidth > maxTabWidth - 80f) break;

            int tabIndex = i + 1;
            string label = ShortenPath(_syncPaths[i]);
            usedWidth += DrawTab(label, tabX + usedWidth, y, _selectedTab == tabIndex, () =>
            {
                _selectedTab = tabIndex;
                ApplyFilter();
            });
        }
    }

    private static float DrawTab(string label, float x, float y, bool selected, Action onClick)
    {
        GUIStyle style = new()
        {
            fontSize = 11,
            fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
            normal = { textColor = selected ? Colors.PrimaryLight : Colors.Grey },
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 4, 4)
        };

        float tabWidth = style.CalcSize(new GUIContent(label)).x + 16f;
        Rect tabRect = new(x, y, tabWidth, TabHeight);

        if (selected)
        {
            DrawRoundedBox(tabRect, Colors.PrimaryDark.SetAlpha(0.3f), 4);
        }

        if (GUI.Button(tabRect, label, style))
        {
            onClick();
        }

        return tabWidth + 2f;
    }

    private void DrawRow(int index, float rowWidth, float pathColWidth)
    {
        FileComparisonEntry entry = _filteredEntries[index];
        float rowY = index * RowHeight;

        // Alternating row background
        if (index % 2 == 0)
        {
            GUI.DrawTexture(new Rect(0f, rowY, rowWidth, RowHeight),
                Utility.GetTexture(Colors.DarkMedium.SetAlpha(0.3f)));
        }

        Color rowColor = entry.Status switch
        {
            FileComparisonStatus.Match => Colors.Success,
            FileComparisonStatus.HashMismatch => Colors.Warning,
            FileComparisonStatus.ServerOnly => Colors.Info,
            FileComparisonStatus.LocalOnly => Colors.Error,
            FileComparisonStatus.DirectoryEntry => Colors.GreyDark,
            _ => Colors.Grey
        };

        GUIStyle cellStyle = new()
        {
            fontSize = 11,
            normal = { textColor = rowColor },
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            padding = new RectOffset(4, 4, 0, 0)
        };

        // File path
        string displayPath = entry.FilePath.Replace('/', '\\');
        GUI.Label(new Rect(0f, rowY, pathColWidth, RowHeight), displayPath, cellStyle);

        // Local hash
        string localHash = entry.IsDirectory ? "dir" : (entry.LocalHash != null ? entry.LocalHash.Substring(0, Math.Min(7, entry.LocalHash.Length)) : "--");
        GUI.Label(new Rect(pathColWidth, rowY, HashColWidth, RowHeight), localHash, cellStyle);

        // Server hash
        string serverHash = entry.IsDirectory ? "dir" : (entry.ServerHash != null ? entry.ServerHash.Substring(0, Math.Min(7, entry.ServerHash.Length)) : "--");
        GUI.Label(new Rect(pathColWidth + HashColWidth, rowY, HashColWidth, RowHeight), serverHash, cellStyle);

        // Status label
        string statusText = entry.Status switch
        {
            FileComparisonStatus.Match => "Match",
            FileComparisonStatus.HashMismatch => "Modified",
            FileComparisonStatus.ServerOnly => "Server Only",
            FileComparisonStatus.LocalOnly => "Local Only",
            FileComparisonStatus.DirectoryEntry => "Directory",
            _ => "?"
        };

        GUIStyle statusStyle = new()
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = rowColor },
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(4, 4, 0, 0)
        };
        GUI.Label(new Rect(pathColWidth + HashColWidth * 2, rowY, StatusColWidth, RowHeight), statusText, statusStyle);
    }

    private static string ShortenPath(string path)
    {
        // Show last two segments: e.g. "../BepInEx/plugins"
        string normalized = path.Replace('\\', '/').TrimEnd('/');
        string[] parts = normalized.Split('/');
        return parts.Length <= 2
            ? normalized
            : "../" + parts[parts.Length - 2] + "/" + parts[parts.Length - 1];
    }
}
