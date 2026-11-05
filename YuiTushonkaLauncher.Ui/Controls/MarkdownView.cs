using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using YuiTushonkaLauncher.Core.Forge.Markdown;

namespace YuiTushonkaLauncher.Controls;

/// <summary>
/// Draws a parsed mod description: headings, paragraphs, both list kinds, code, quotes, rules and images, with links
/// that call <see cref="OpenUrlCommand" />. It is a panel, not a document viewer — the launcher has no web view and no
/// intention of gaining one for a text field, so everything here is a handful of controls a text block can wrap.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    /// <summary>The parsed description to draw.</summary>
    public static readonly StyledProperty<MarkdownDocument?> DocumentProperty = AvaloniaProperty.Register<MarkdownView, MarkdownDocument?>(
        nameof(Document)
    );

    /// <summary>
    /// Invoked with a link's address when the user clicks it. Typed for a string on purpose: a command built for
    /// another parameter type cannot be bound here at all, which is what a bare <see cref="ICommand" /> allowed.
    /// </summary>
    public static readonly StyledProperty<IRelayCommand<string>?> OpenUrlCommandProperty = AvaloniaProperty.Register<
        MarkdownView,
        IRelayCommand<string>?
    >(nameof(OpenUrlCommand));

    /// <summary>A view with nothing in it.</summary>
    public MarkdownView()
    {
        Spacing = 6;
    }

    static MarkdownView()
    {
        DocumentProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Rebuild());
        OpenUrlCommandProperty.Changed.AddClassHandler<MarkdownView>((view, _) => view.Rebuild());
    }

    /// <summary>The parsed description to draw.</summary>
    public MarkdownDocument? Document
    {
        get { return GetValue(DocumentProperty); }
        set { SetValue(DocumentProperty, value); }
    }

    /// <summary>Invoked with a link's address when the user clicks it.</summary>
    public IRelayCommand<string>? OpenUrlCommand
    {
        get { return GetValue(OpenUrlCommandProperty); }
        set { SetValue(OpenUrlCommandProperty, value); }
    }

    private void Rebuild()
    {
        Children.Clear();
        if (Document is not { IsEmpty: false } document)
        {
            return;
        }

        foreach (var block in document.Blocks)
        {
            if (Build(block) is { } control)
            {
                Children.Add(control);
            }
        }
    }

    private Control? Build(MarkdownBlock block)
    {
        switch (block.Kind)
        {
            case MarkdownBlockKind.Heading:
                var heading = Fill(
                    new TextBlock
                    {
                        FontWeight = FontWeight.SemiBold,
                        FontSize = block.Level switch
                        {
                            1 => 16,
                            2 => 14,
                            3 => 13,
                            _ => 12,
                        },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    block.Inlines
                );
                return heading;

            case MarkdownBlockKind.Paragraph:
                return Fill(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 }, block.Inlines);

            case MarkdownBlockKind.Bullets:
            case MarkdownBlockKind.Numbers:
                return BuildList(block);

            case MarkdownBlockKind.Code:
                return new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#1B1D22")),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Child = new SelectableTextBlock
                    {
                        Text = string.Join("\n", block.Lines),
                        FontFamily = new FontFamily("monospace"),
                        FontSize = 11,
                        TextWrapping = TextWrapping.NoWrap,
                    },
                };

            case MarkdownBlockKind.Quote:
                return new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#4A4F5A")),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(8, 2, 0, 2),
                    Child = Fill(
                        new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 12,
                            FontStyle = FontStyle.Italic,
                        },
                        block.Inlines
                    ),
                };

            case MarkdownBlockKind.Rule:
                return new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    Margin = new Thickness(0, 4),
                };

            case MarkdownBlockKind.Image:
                var caption = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
                caption.Inlines!.Add(new Run("[image] "));
                if (!string.IsNullOrWhiteSpace(block.Url))
                {
                    caption.Inlines.Add(LinkInline(block.Inlines.FirstOrDefault()?.Text ?? block.Url, block.Url));
                }

                return caption;

            default:
                return null;
        }
    }

    private Control BuildList(MarkdownBlock block)
    {
        var panel = new StackPanel { Spacing = 3 };
        for (var index = 0; index < block.Items.Count; index++)
        {
            var marker = block.Kind == MarkdownBlockKind.Numbers ? $"{index + 1}." : "•";
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0),
            };

            text.Inlines!.Add(new Run(marker + "  "));
            foreach (var inline in block.Items[index])
            {
                Add(text, inline);
            }

            panel.Children.Add(text);
        }

        return panel;
    }

    private TextBlock Fill(TextBlock target, IReadOnlyList<MarkdownInline> inlines)
    {
        target.Inlines ??= [];
        foreach (var inline in inlines)
        {
            Add(target, inline);
        }

        return target;
    }

    private void Add(TextBlock target, MarkdownInline inline)
    {
        var inlines = target.Inlines!;
        switch (inline.Kind)
        {
            case MarkdownInlineKind.Bold:
                inlines.Add(new Bold { Inlines = { new Run(inline.Text) } });
                break;

            case MarkdownInlineKind.Italic:
                inlines.Add(new Italic { Inlines = { new Run(inline.Text) } });
                break;

            case MarkdownInlineKind.Strike:
                var struck = new Run(inline.Text) { TextDecorations = TextDecorations.Strikethrough };
                inlines.Add(struck);
                break;

            case MarkdownInlineKind.Code:
                inlines.Add(new Run(inline.Text) { FontFamily = new FontFamily("monospace") });
                break;

            case MarkdownInlineKind.Link:
                if (string.IsNullOrWhiteSpace(inline.Url))
                {
                    inlines.Add(new Run(inline.Text));
                }
                else
                {
                    inlines.Add(LinkInline(inline.Text, inline.Url));
                }

                break;

            default:
                inlines.Add(new Run(inline.Text));
                break;
        }
    }

    /// <summary>A link is a transparent button styled as text: it has to be clickable, and wrapping must still work.</summary>
    private InlineUIContainer LinkInline(string text, string url)
    {
        var button = new Button
        {
            Content = text,
            Classes = { "link" },
            Padding = new Thickness(0),
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };

        // A click handler rather than a bound command: a Button asks its command CanExecute while it attaches to the
        // tree, and a command of the wrong parameter type throws there, taking the window down with it.
        button.Click += (_, _) => InvokeLink(url);
        ToolTip.SetTip(button, url);
        return new InlineUIContainer(button);
    }

    private void InvokeLink(string url)
    {
        if (OpenUrlCommand is not { } command)
        {
            return;
        }

        try
        {
            if (command.CanExecute(url))
            {
                OpenUrlCommand.Execute(url);
            }
        }
        catch (ArgumentException)
        {
            // A command that cannot take an address simply does not open the link; that is not worth a crash.
        }
    }
}
