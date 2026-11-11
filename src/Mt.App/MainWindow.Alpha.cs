using System.Globalization;
using Avalonia.Controls;

namespace Mt.App;

// 불투명도 값 칸(PLAN-parts.md P4) — "모션" 탭, 불투명도를 받을 수 있는 노드를 골랐을 때만. 값을 넣으면 그 시각에 알파 채널의 키.
public partial class MainWindow
{
    private readonly StackPanel _alphaPanel = new() { Spacing = 4, IsVisible = false };
    private readonly TextBox _alphaBox = new() { FontSize = 11 };
    private readonly TextBlock _alphaNode = new() { FontSize = 10, Opacity = 0.7 };

    /// <summary>테스트 손잡이 — 불투명도 칸과 보이는가.</summary>
    internal (TextBox Box, bool Visible) AlphaBox => (_alphaBox, _alphaPanel.IsVisible);

    private Control BuildAlphaPanel()
    {
        _alphaPanel.Children.Add(Header("불투명도 (지금 시각)"));
        _alphaPanel.Children.Add(_alphaNode);
        _alphaPanel.Children.Add(Labeled("alpha", _alphaBox));
        ToolTip.SetTip(_alphaBox, "고른 노드의 불투명도(0~1) — 값을 넣고 Enter = 그 시각에 알파 채널의 키(되돌리기 한 번). 커브는 그래프에서. 부모 노드의 불투명도와 곱해져 그려진다. 세트에서 불투명도를 받는다고 표시한 노드만 받는다.");
        OnCommit(_alphaBox, text =>
        {
            if (!_refreshing && _viewport.SelectedNode is { } node && TryFloat(text, out float value))
            {
                Guard("불투명도 키", () => _viewport.SetAlphaValue(node, value));
                RefreshAlphaPanel();
            }
        });
        return _alphaPanel;
    }

    private void RefreshAlphaPanel()
    {
        _refreshDepth++;

        try
        {
            float? alpha = _viewport.AlphaValue(_viewport.SelectedNode);
            _alphaPanel.IsVisible = alpha is not null;

            if (alpha is null)
            {
                return;
            }

            _alphaNode.Text = _viewport.SelectedNode;

            if (!_alphaBox.IsFocused)
            {
                _alphaBox.Text = alpha.Value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }
}
