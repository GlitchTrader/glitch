//
// Accordion page layout: one outer page scroll; expanders with padded content (no nested section scroll).
//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private const double AccordionSectionGap = 10;
        private const double AccordionHeaderHeight = 40;
        private const double AccordionHeaderPaddingX = 8;
        private const double AccordionContentPaddingTop = 8;
        private const double AccordionContentPaddingBottom = 12;

        private ScrollViewer _dashboardPageScroll;
        private Expander _dashboardReplicationExpander;
        private Expander _dashboardConnectedAccountsExpander;

        private ScrollViewer _journalAccordionScroll;
        private Expander _journalPerformanceExpander;
        private Expander _journalCriticalWarningsExpander;
        private Expander _journalLiveFeedExpander;

        private Style _accordionExpanderStyle;

        private static Grid WrapTabBodyForScroll(UIElement body)
        {
            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (body is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Stretch;
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            Grid.SetRow(body, 0);
            host.Children.Add(body);
            return host;
        }

        private Expander CreateAccordionExpander(FrameworkElement context, string localizationKey, string fallback)
        {
            if (_accordionExpanderStyle == null)
                _accordionExpanderStyle = CreateAccordionExpanderStyle(context);

            var expander = new Expander
            {
                Style = _accordionExpanderStyle,
                Margin = new Thickness(0, 0, 0, AccordionSectionGap),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top
            };

            // Header must stay a localized string — NT default template ToString()-ifies visuals.
            expander.Header = L(localizationKey, fallback);
            BindLocalizedHeader(expander, localizationKey, fallback);
            return expander;
        }

        private Style CreateAccordionExpanderStyle(FrameworkElement context)
        {
            var style = new Style(typeof(Expander));
            ApplySkinSetter(style, Control.ForegroundProperty, context, "FontControlBrush", "FontHeaderLevel4Brush", "FontTableBrush");
            ApplySkinSetter(style, Control.BackgroundProperty, context, "BackgroundTextInput", "BackgroundMainWindow", "GridEntireBackground");
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateAccordionExpanderTemplate()));
            return style;
        }

        private static ControlTemplate CreateAccordionExpanderTemplate()
        {
            var template = new ControlTemplate(typeof(Expander));

            var rootFactory = new FrameworkElementFactory(typeof(DockPanel));

            var toggleFactory = new FrameworkElementFactory(typeof(ToggleButton));
            toggleFactory.Name = "HeaderToggle";
            toggleFactory.SetValue(DockPanel.DockProperty, Dock.Top);
            toggleFactory.SetValue(FrameworkElement.MinHeightProperty, AccordionHeaderHeight);
            toggleFactory.SetValue(FrameworkElement.HeightProperty, AccordionHeaderHeight);
            toggleFactory.SetValue(Control.PaddingProperty, new Thickness(AccordionHeaderPaddingX, 0, AccordionHeaderPaddingX, 0));
            toggleFactory.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggleFactory.SetValue(Control.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            toggleFactory.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
            toggleFactory.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            toggleFactory.SetValue(Control.CursorProperty, Cursors.Hand);
            toggleFactory.SetValue(Control.FontWeightProperty, FontWeights.Medium);
            toggleFactory.SetValue(Control.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            toggleFactory.SetValue(Control.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            var isExpandedBinding = new Binding
            {
                Path = new PropertyPath(Expander.IsExpandedProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Mode = BindingMode.TwoWay
            };
            toggleFactory.SetValue(ToggleButton.IsCheckedProperty, isExpandedBinding);
            toggleFactory.SetValue(Control.TemplateProperty, CreateAccordionHeaderToggleTemplate());

            var headerGridFactory = new FrameworkElementFactory(typeof(Grid));
            headerGridFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

            var arrowColumnFactory = new FrameworkElementFactory(typeof(ColumnDefinition));
            arrowColumnFactory.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            headerGridFactory.AppendChild(arrowColumnFactory);

            var titleColumnFactory = new FrameworkElementFactory(typeof(ColumnDefinition));
            titleColumnFactory.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            headerGridFactory.AppendChild(titleColumnFactory);

            var arrowFactory = new FrameworkElementFactory(typeof(TextBlock));
            arrowFactory.Name = "AccordionArrow";
            arrowFactory.SetValue(Grid.ColumnProperty, 0);
            arrowFactory.SetValue(TextBlock.TextProperty, "\u25B6");
            arrowFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            arrowFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrowFactory.SetValue(TextBlock.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            headerGridFactory.AppendChild(arrowFactory);

            var headerPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            headerPresenterFactory.SetValue(Grid.ColumnProperty, 1);
            headerPresenterFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            headerPresenterFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            headerPresenterFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            headerGridFactory.AppendChild(headerPresenterFactory);

            toggleFactory.AppendChild(headerGridFactory);
            rootFactory.AppendChild(toggleFactory);

            var expandSiteFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            expandSiteFactory.Name = "ExpandSite";
            expandSiteFactory.SetValue(ContentPresenter.ContentSourceProperty, "Content");
            expandSiteFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            expandSiteFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            rootFactory.AppendChild(expandSiteFactory);

            template.VisualTree = rootFactory;

            var expandedTrigger = new Trigger { Property = Expander.IsExpandedProperty, Value = true };
            expandedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "ExpandSite"));
            expandedTrigger.Setters.Add(new Setter(TextBlock.TextProperty, "\u25BC", "AccordionArrow"));
            template.Triggers.Add(expandedTrigger);

            return template;
        }

        private static ControlTemplate CreateAccordionHeaderToggleTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));

            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;
            return template;
        }

        private static Border WrapAccordionSectionContent(UIElement content)
        {
            return new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(0, AccordionContentPaddingTop, 0, AccordionContentPaddingBottom),
                Child = content
            };
        }

        private ScrollViewer CreateAccordionPageScrollHost(StackPanel accordionStack)
        {
            var pageScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CanContentScroll = false,
                PanningMode = PanningMode.None
            };
            pageScroll.Content = accordionStack;
            pageScroll.PreviewMouseWheel += OnAccordionPageScrollPreviewMouseWheel;
            return pageScroll;
        }

        private static void OnAccordionPageScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!(sender is ScrollViewer scroll) || scroll.ScrollableHeight <= 0)
                return;

            double nextOffset = scroll.VerticalOffset - e.Delta;
            if (nextOffset < 0)
                nextOffset = 0;
            else if (nextOffset > scroll.ScrollableHeight)
                nextOffset = scroll.ScrollableHeight;

            scroll.ScrollToVerticalOffset(nextOffset);
            e.Handled = true;
        }

        /// <summary>
        /// DataGrids inside a page ScrollViewer must not host their own vertical scroll viewer —
        /// nested scroll is what breaks thumb travel and wheel routing in NT/WPF hosts.
        /// </summary>
        private static void ConfigureDataGridForPageScroll(
            DataGrid grid,
            bool allowHorizontalScroll = false,
            bool enableRowVirtualization = false)
        {
            if (grid == null)
                return;

            if (enableRowVirtualization)
            {
                grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
                grid.SetValue(
                    ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    allowHorizontalScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden);
                grid.SetValue(ScrollViewer.CanContentScrollProperty, true);
                grid.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
                grid.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            }
            else
            {
                grid.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
                grid.SetValue(
                    ScrollViewer.HorizontalScrollBarVisibilityProperty,
                    allowHorizontalScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden);
                grid.SetValue(ScrollViewer.CanContentScrollProperty, false);
                grid.SetValue(VirtualizingPanel.IsVirtualizingProperty, false);
            }

            grid.VerticalAlignment = VerticalAlignment.Top;
            grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        private static void ClearGridAccordionHeightConstraints(DataGrid grid)
        {
            if (grid == null)
                return;

            grid.ClearValue(FrameworkElement.MinHeightProperty);
            grid.ClearValue(FrameworkElement.MaxHeightProperty);
            grid.VerticalAlignment = VerticalAlignment.Top;
        }

        private void ClearAccordionLayoutRefs()
        {
            if (_dashboardPageScroll != null)
                _dashboardPageScroll.PreviewMouseWheel -= OnAccordionPageScrollPreviewMouseWheel;
            if (_journalAccordionScroll != null)
                _journalAccordionScroll.PreviewMouseWheel -= OnAccordionPageScrollPreviewMouseWheel;

            _dashboardPageScroll = null;
            _dashboardReplicationExpander = null;
            _dashboardConnectedAccountsExpander = null;
            _journalAccordionScroll = null;
            _journalPerformanceExpander = null;
            _journalCriticalWarningsExpander = null;
            _journalLiveFeedExpander = null;
            _accordionExpanderStyle = null;
        }
    }
}
