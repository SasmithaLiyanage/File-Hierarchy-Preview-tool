using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HierarchyTool
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private TextBox? _scriptTextBox;
        private StackPanel? _previewPanel;
        private TextBlock? _destinationTextBlock;
        public MainWindow()
        {
            InitializeComponent();
            // Attempt to locate the TextBox and preview panel after InitializeComponent
            _scriptTextBox = (this.Content as FrameworkElement)?.FindName("ScriptTextBox") as TextBox ?? FindChildByName<TextBox>(this.Content as DependencyObject, "ScriptTextBox");
            _previewPanel = (this.Content as FrameworkElement)?.FindName("PreviewPanel") as StackPanel ?? FindChildByName<StackPanel>(this.Content as DependencyObject, "PreviewPanel");
            _destinationTextBlock = (this.Content as FrameworkElement)?.FindName("DestinationTextBlock") as TextBlock ?? FindChildByName<TextBlock>(this.Content as DependencyObject, "DestinationTextBlock");

            if (_scriptTextBox != null)
                SetScriptText(DefaultHierarchyScript);

            UpdatePreviewFromScript();
        }

        private const string DefaultHierarchyScript = """
folder assets{}

folder components{

    folder login{
        file login.txt
    }

    folder dashboard{
        file dashboard.txt
    }

}

folder config{}

folder resources{}
""";

        private T? FindChildByName<T>(DependencyObject? parent, string name) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Name == name && child is T typed)
                    return typed;

                var result = FindChildByName<T>(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void ViteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scriptTextBox != null)
            {
                SetScriptText("folder my-vite-app {\nfolder src {\nfile App.jsx\nfile main.jsx\n}\nfile index.html\nfile package.json\n}");
            }
        }

        private void SpringButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scriptTextBox != null)
            {
                SetScriptText("folder my-spring-app {\nfolder src {\nfolder main {\nfile application.properties\n}\n}\nfile pom.xml\n}");
            }
        }

        private void BlankButton_Click(object sender, RoutedEventArgs e)
        {
            if (_scriptTextBox != null)
            {
                SetScriptText(string.Empty);
            }
        }

        private void ScriptTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdatePreviewFromScript();
        }

        private void SetScriptText(string text)
        {
            if (_scriptTextBox == null)
                return;

            _scriptTextBox.Text = text;
        }

        private void ScriptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Tab || _scriptTextBox == null)
                return;

            var selectionStart = _scriptTextBox.SelectionStart;
            var selectionLength = _scriptTextBox.SelectionLength;
            _scriptTextBox.Text = _scriptTextBox.Text.Remove(selectionStart, selectionLength).Insert(selectionStart, "    ");
            _scriptTextBox.SelectionStart = selectionStart + 4;
            _scriptTextBox.SelectionLength = 0;
            e.Handled = true;
        }

        private void AddFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_scriptTextBox == null)
                return;

            var insert = "folder NewFolder {\n}\n";
            var pos = _scriptTextBox.SelectionStart;
            _scriptTextBox.Text = _scriptTextBox.Text.Insert(pos, insert);
            _scriptTextBox.SelectionStart = pos + insert.Length;
            UpdatePreviewFromScript();
        }

        private void AddFileButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_scriptTextBox == null)
                return;

            var insert = "file new-file.txt\n";
            var pos = _scriptTextBox.SelectionStart;
            _scriptTextBox.Text = _scriptTextBox.Text.Insert(pos, insert);
            _scriptTextBox.SelectionStart = pos + insert.Length;
            UpdatePreviewFromScript();
        }

        private void UpdatePreviewFromScript()
        {
            if (_previewPanel == null)
                return;

            _previewPanel.Children.Clear();

            if (_scriptTextBox == null)
                return;

            var script = _scriptTextBox.Text.Replace("\r\n", "\n");
            var roots = script.Contains("folder ", StringComparison.OrdinalIgnoreCase)
                ? ParseFolderScript(script)
                : ParseLegacyScript(script);

            // mark last sibling flags
            void MarkLast(List<PreviewNode> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var n = list[i];
                    n.IsLastSibling = (i == list.Count - 1);
                    if (n.Children.Count > 0)
                        MarkLast(n.Children);
                }
            }

            MarkLast(roots);

            // render
            for (int i = 0; i < roots.Count; i++)
            {
                RenderNode(roots[i], new List<bool>());
            }
        }

        private List<PreviewNode> ParseFolderScript(string script)
        {
            var parts = Regex.Matches(script, @"folder\s+[^{}\r\n]+?(?=\s*\{)|file\s+[^\s{}]+|[{}]|[^\s{}]+", RegexOptions.IgnoreCase)
                .Select(match => match.Value.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
            var index = 0;
            return ParseBlock(parts, ref index, false);
        }

        private List<PreviewNode> ParseBlock(string[] parts, ref int index, bool stopAtBrace)
        {
            var nodes = new List<PreviewNode>();

            while (index < parts.Length)
            {
                var part = parts[index++].Trim();
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                if (part == "}")
                    break;

                if (part == "{")
                    continue;

                var isFolder = part.StartsWith("folder ", StringComparison.OrdinalIgnoreCase);
                var isFile = part.StartsWith("file ", StringComparison.OrdinalIgnoreCase);
                var name = isFolder ? part[7..].Trim() : isFile ? part[5..].Trim() : part;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var node = new PreviewNode
                {
                    Name = name.TrimEnd('/'),
                    IsFolder = isFolder || !isFile && !name.Contains('.')
                };

                if (isFolder && index < parts.Length && parts[index].Trim() == "{")
                {
                    index++;
                    node.Children.AddRange(ParseBlock(parts, ref index, true));
                }

                nodes.Add(node);
            }

            return nodes;
        }

        private List<PreviewNode> ParseLegacyScript(string script)
        {
            var roots = new List<PreviewNode>();
            var depthMap = new Dictionary<int, PreviewNode>();

            foreach (var raw in script.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int depth = 0;
                while (depth < line.Length && line[depth] == '>')
                    depth++;

                var name = line[depth..].Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                var node = new PreviewNode { Name = name.TrimEnd('/'), IsFolder = name.EndsWith("/") || !name.Contains('.') };
                if (depth == 0)
                    roots.Add(node);
                else if (depthMap.TryGetValue(depth - 1, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);

                depthMap[depth] = node;
                foreach (var key in depthMap.Keys.Where(key => key > depth).ToList())
                    depthMap.Remove(key);
            }

            return roots;
        }

        private UIElement BuildNodeContent(string name, bool isFolder)
        {
            var panel = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 8 };

            var icon = new FontIcon();
            icon.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets");
            icon.FontSize = 20;
            icon.Glyph = isFolder ? "\uE8B7" : "\uE8A5"; // folder / document glyphs

            var text = new TextBlock() { Text = name, VerticalAlignment = VerticalAlignment.Center };

            panel.Children.Add(icon);
            panel.Children.Add(text);

            return panel;
        }

        private class PreviewNode
        {
            public string Name { get; set; } = string.Empty;
            public bool IsFolder { get; set; }
            public List<PreviewNode> Children { get; } = new List<PreviewNode>();
            public bool IsLastSibling { get; set; }
        }

        private void RenderNode(PreviewNode node, List<bool> ancestorHasNext)
        {
            if (_previewPanel == null)
                return;

            // Row container
            var row = new StackPanel() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };

            // For each ancestor level, draw a vertical line segment if ancestorHasNext is true
            foreach (var hasNext in ancestorHasNext)
            {
                var slot = new Grid() { Width = 20, Height = 24 };
                if (hasNext)
                {
                    var vert = new Microsoft.UI.Xaml.Shapes.Rectangle() { Width = 2, Height = 24, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    slot.Children.Add(vert);
                }
                row.Children.Add(slot);
            }

            // Current connector: horizontal line that connects to icon
            var connectorSlot = new Grid() { Width = 20, Height = 24 };
            var horiz = new Microsoft.UI.Xaml.Shapes.Rectangle() { Height = 2, Width = 14, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4,0,0,0) };
            connectorSlot.Children.Add(horiz);
            row.Children.Add(connectorSlot);

            // Icon
            UIElement icon = node.IsFolder
                ? new BitmapIcon() { UriSource = new Uri("ms-appx:///Assets/open-folder.png"), ShowAsMonochrome = false, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center }
                : new FontIcon() { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"), Glyph = "\uE8A5", FontSize = 20, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SteelBlue), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);

            // Text
            var text = new TextBlock() { Text = node.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 14 };
            row.Children.Add(text);

            _previewPanel.Children.Add(row);

            // Prepare ancestor flags for children: ancestorHasNext plus whether this node has next sibling
            var childAncestor = new List<bool>(ancestorHasNext) { !node.IsLastSibling };
            for (int i = 0; i < node.Children.Count; i++)
            {
                RenderNode(node.Children[i], childAncestor);
            }
        }

        private async void ExportMarkdown_Click(object sender, RoutedEventArgs e)
        {
            var roots = GetPreviewRoots();
            if (roots.Count == 0)
            {
                var emptyDialog = new ContentDialog
                {
                    Title = "Nothing to export",
                    Content = "Add a folder or file to the codebase first.",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await emptyDialog.ShowAsync();
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "README.md"
            };
            picker.FileTypeChoices.Add("Markdown file", new List<string> { ".md" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            try
            {
                var markdown = BuildMarkdown(roots);
                await FileIO.WriteTextAsync(file, markdown);

                var successDialog = new ContentDialog
                {
                    Title = "Markdown saved",
                    Content = $"Saved to:\n{file.Path}",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            catch (Exception exception)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Could not save Markdown",
                    Content = exception.Message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private List<PreviewNode> GetPreviewRoots()
        {
            var script = _scriptTextBox?.Text.Replace("\r\n", "\n") ?? string.Empty;
            return script.Contains("folder ", StringComparison.OrdinalIgnoreCase)
                ? ParseFolderScript(script)
                : ParseLegacyScript(script);
        }

        private string BuildMarkdown(List<PreviewNode> roots)
        {
            var lines = new List<string> { "# File Hierarchy", string.Empty, "```text" };

            for (var index = 0; index < roots.Count; index++)
                AppendMarkdownNode(lines, roots[index], string.Empty, index == roots.Count - 1);

            lines.Add("```");
            lines.Add(string.Empty);
            return string.Join(Environment.NewLine, lines);
        }

        private void AppendMarkdownNode(List<string> lines, PreviewNode node, string prefix, bool isLast)
        {
            var connector = isLast ? "└── " : "├── ";
            var suffix = node.IsFolder ? "/" : string.Empty;
            lines.Add($"{prefix}{connector}{node.Name}{suffix}");

            var childPrefix = prefix + (isLast ? "    " : "│   ");
            for (var index = 0; index < node.Children.Count; index++)
                AppendMarkdownNode(lines, node.Children[index], childPrefix, index == node.Children.Count - 1);
        }

        private async void ExportImage_Click(object sender, RoutedEventArgs e)
        {
            if (_previewPanel == null || _previewPanel.Children.Count == 0)
            {
                var emptyDialog = new ContentDialog
                {
                    Title = "Nothing to export",
                    Content = "Add a folder or file to the codebase first.",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await emptyDialog.ShowAsync();
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = "hierarchy-preview.png"
            };
            picker.FileTypeChoices.Add("PNG image", new List<string> { ".png" });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSaveFileAsync();
            if (file == null)
                return;

            try
            {
                var renderTarget = new RenderTargetBitmap();
                await renderTarget.RenderAsync(_previewPanel);

                var pixels = (await renderTarget.GetPixelsAsync()).ToArray();
                using var outputStream = await file.OpenAsync(FileAccessMode.ReadWrite);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)renderTarget.PixelWidth,
                    (uint)renderTarget.PixelHeight,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();

                var successDialog = new ContentDialog
                {
                    Title = "Image saved",
                    Content = $"Saved to:\n{file.Path}",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            catch (Exception exception)
            {
                var errorDialog = new ContentDialog
                {
                    Title = "Could not save image",
                    Content = exception.Message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
        }

        private async void GenerateFiles_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var destination = await picker.PickSingleFolderAsync();
            if (destination == null)
                return;

            if (_destinationTextBlock != null)
                _destinationTextBlock.Text = $"Destination: {destination.Path}";

            try
            {
                var script = _scriptTextBox?.Text.Replace("\r\n", "\n") ?? string.Empty;
                var roots = script.Contains("folder ", StringComparison.OrdinalIgnoreCase)
                    ? ParseFolderScript(script)
                    : ParseLegacyScript(script);

                foreach (var node in roots)
                    CreateNode(destination.Path, node);

                var success = new ContentDialog
                {
                    Title = "Hierarchy created",
                    Content = $"Created in:\n{destination.Path}",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await success.ShowAsync();
            }
            catch (Exception exception)
            {
                var error = new ContentDialog
                {
                    Title = "Could not create hierarchy",
                    Content = exception.Message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await error.ShowAsync();
            }
        }

        private void CreateNode(string parentPath, PreviewNode node)
        {
            var nodePath = Path.Combine(parentPath, node.Name);

            if (node.IsFolder)
            {
                Directory.CreateDirectory(nodePath);
                foreach (var child in node.Children)
                    CreateNode(nodePath, child);
                return;
            }

            Directory.CreateDirectory(parentPath);
            if (!File.Exists(nodePath))
                File.WriteAllText(nodePath, string.Empty);
        }
    }
}
