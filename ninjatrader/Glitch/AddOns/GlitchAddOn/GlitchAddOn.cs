//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Glitch.UI;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Minimal Glitch add-on shell used as the baseline for incremental rebuilds.
    /// </summary>
    public partial class GlitchAddOn : AddOnBase
    {
        private const string GlitchMenuHeader = "Glitch";
        private NTMenuItem _menuItem;
        private NTMenuItem _newMenu;
        private GlitchMainWindow _mainWindow;
        private static GlitchAddOn _activeInstance;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Glitch";
                Description = "Glitch baseline add-on";
            }
            else if (State == State.Active)
            {
                GlitchAddOn previousInstance = _activeInstance;
                _activeInstance = this;
                RunOnUiThread(() =>
                {
                    if (previousInstance != null && !ReferenceEquals(previousInstance, this))
                        previousInstance.RetireShellForReplacement();

                    ActivateShell();
                });
            }
            else if (State == State.Terminated)
            {
                if (ReferenceEquals(_activeInstance, this))
                    _activeInstance = null;

                RunOnUiThread(RetireShellForTermination);
            }
        }

        protected override void OnWindowCreated(Window window)
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            ControlCenter cc = window as ControlCenter;
            if (cc != null)
            {
                AttachControlCenterMenus(cc);
                return;
            }

            TryAttachChartTraderWidget(window);
        }

        protected override void OnWindowDestroyed(Window window)
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            if (window is ControlCenter)
            {
                DetachControlCenterMenus(window as ControlCenter);
                return;
            }

            DetachChartTraderWidget(window);
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            RunOnUiThread(ShowWindow);
        }

        private void ShowWindow()
        {
            EnsureSingleWindow(restart: false);
        }

        private void ActivateShell()
        {
            if (!ReferenceEquals(_activeInstance, this))
                return;

            AttachMenusToOpenControlCenters();
            AttachWidgetsToOpenChartWindows();
            RestartSingleWindow();
        }

        private void RetireShellForReplacement()
        {
            RemoveMenusFromOpenControlCenters();
            DetachAllChartTraderHosts();
            CloseWindow();
        }

        private void RetireShellForTermination()
        {
            RemoveMenusFromOpenControlCenters();
            DetachAllChartTraderHosts();
            CloseWindow();
        }

        internal static void ShowMainWindowFromExternalSurface()
        {
            if (_activeInstance == null)
                return;

            RunOnUiThread(_activeInstance.ShowWindow);
        }

        internal static void RequestFlattenAll()
        {
            if (_activeInstance == null)
                return;

            _activeInstance.RequestFlattenAllCore();
        }

        private void RequestFlattenAllCore()
        {
            RunOnUiThreadSync(() =>
            {
                if (_mainWindow == null)
                    EnsureSingleWindow(restart: false);
                _mainWindow?.FlattenAllFromExternalSurface();
            });
        }

        private void RestartSingleWindow()
        {
            EnsureSingleWindow(restart: true);
        }

        private void EnsureSingleWindow(bool restart)
        {
            List<Window> glitchWindows = FindOpenGlitchWindows();

            if (restart)
            {
                CloseWindows(glitchWindows);
                _mainWindow = null;
            }
            else if (glitchWindows.Count > 1)
            {
                // Keep one current-type window if available; otherwise replace all.
                var keep = glitchWindows.OfType<GlitchMainWindow>().FirstOrDefault();
                if (keep != null)
                {
                    foreach (var duplicate in glitchWindows.Where(w => !ReferenceEquals(w, keep)))
                        SafeClose(duplicate);

                    _mainWindow = keep;
                }
                else
                {
                    CloseWindows(glitchWindows);
                    _mainWindow = null;
                }
            }
            else if (glitchWindows.Count == 1 && _mainWindow == null)
            {
                _mainWindow = glitchWindows[0] as GlitchMainWindow;
                if (_mainWindow == null)
                    SafeClose(glitchWindows[0]);
            }

            if (_mainWindow == null)
            {
                _mainWindow = new GlitchMainWindow();
                _mainWindow.Closed += OnMainWindowClosed;
            }
            else
            {
                // Ensure handler is attached exactly once.
                _mainWindow.Closed -= OnMainWindowClosed;
                _mainWindow.Closed += OnMainWindowClosed;
            }

            _mainWindow.Show();
            _mainWindow.Activate();
        }

        private void OnMainWindowClosed(object sender, EventArgs e)
        {
            if (_mainWindow == null)
                return;

            _mainWindow.Closed -= OnMainWindowClosed;
            _mainWindow = null;
        }

        private void CloseWindow()
        {
            RunOnUiThread(() =>
            {
                CloseWindows(FindOpenGlitchWindows());
                _mainWindow = null;
            });

        }

        private void AttachMenusToOpenControlCenters()
        {
            if (Application.Current == null)
                return;

            foreach (Window window in Application.Current.Windows)
            {
                var controlCenter = window as ControlCenter;
                if (controlCenter != null)
                    AttachControlCenterMenus(controlCenter);
            }
        }

        private void RemoveMenusFromOpenControlCenters()
        {
            if (Application.Current == null)
            {
                ReleaseMenuItemReferences();
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                var controlCenter = window as ControlCenter;
                if (controlCenter != null)
                    DetachControlCenterMenus(controlCenter);
            }

            ReleaseMenuItemReferences();
        }

        private void AttachWidgetsToOpenChartWindows()
        {
            if (Application.Current == null)
                return;

            foreach (Window window in Application.Current.Windows)
            {
                if (IsChartWindow(window))
                    TryAttachChartTraderWidget(window);
            }
        }

        private void AttachControlCenterMenus(ControlCenter controlCenter)
        {
            if (controlCenter == null)
                return;

            NTMenuItem newMenu = controlCenter.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (newMenu == null)
                return;

            RemoveGlitchMenuItems(newMenu);
            ReleaseMenuItemReferences();

            _newMenu = newMenu;
            _menuItem = CreateGlitchMenuItem();
            _menuItem.Click += OnMenuItemClick;
            _newMenu.Items.Add(_menuItem);
        }

        private void DetachControlCenterMenus(ControlCenter controlCenter)
        {
            if (controlCenter == null)
            {
                ReleaseMenuItemReferences();
                return;
            }

            NTMenuItem newMenu = controlCenter.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
            if (newMenu != null)
                RemoveGlitchMenuItems(newMenu);

            ReleaseMenuItemReferences();
        }

        private NTMenuItem CreateGlitchMenuItem()
        {
            return new NTMenuItem
            {
                Header = GlitchMenuHeader,
                Style = Application.Current.TryFindResource("MainMenuItem") as Style
            };
        }

        private void RemoveGlitchMenuItems(ItemsControl container)
        {
            if (container == null)
                return;

            foreach (NTMenuItem existing in container.Items.OfType<NTMenuItem>()
                .Where(IsGlitchMenuItem)
                .ToList())
            {
                existing.Click -= OnMenuItemClick;
                container.Items.Remove(existing);
            }
        }

        private static bool IsGlitchMenuItem(NTMenuItem item)
        {
            return item != null &&
                string.Equals(item.Header as string, GlitchMenuHeader, StringComparison.Ordinal);
        }

        private void ReleaseMenuItemReferences()
        {
            if (_menuItem != null)
                _menuItem.Click -= OnMenuItemClick;

            _menuItem = null;
            _newMenu = null;
        }

        private static void RunOnUiThread(Action action)
        {
            if (action == null)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.InvokeAsync(action);
        }

        private static void RunOnUiThreadSync(Action action)
        {
            if (action == null)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private static List<Window> FindOpenGlitchWindows()
        {
            var windows = new List<Window>();
            if (Application.Current == null)
                return windows;

            foreach (Window window in Application.Current.Windows)
            {
                if (IsGlitchWindow(window))
                    windows.Add(window);
            }

            return windows;
        }

        private static bool IsGlitchWindow(Window window)
        {
            if (window == null)
                return false;

            if (window is GlitchMainWindow)
                return true;

            var type = window.GetType();
            return string.Equals(type.FullName, "Glitch.UI.GlitchMainWindow", StringComparison.Ordinal) ||
                   string.Equals(type.Name, "GlitchMainWindow", StringComparison.Ordinal);
        }

        private static void CloseWindows(IEnumerable<Window> windows)
        {
            if (windows == null)
                return;

            foreach (var window in windows.ToList())
                SafeClose(window);
        }

        private static void SafeClose(Window window)
        {
            try
            {
                GlitchMainWindow glitchWindow = window as GlitchMainWindow;
                if (glitchWindow != null)
                    glitchWindow.ShutdownForAddOn();
                else
                    window?.Close();
            }
            catch
            {
            }
        }
    }
}
