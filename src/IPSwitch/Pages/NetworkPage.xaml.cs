using IPSwitch.Models;
using IPSwitch.Services;
using IPSwitch.Windows;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace IPSwitch.Pages
{
    /// <summary>
    /// Profile management page: search, sorting, add, edit, duplicate, delete, import/export,
    /// desktop shortcuts and applying profiles.
    /// </summary>
    /// <remarks>
    /// Works directly on <see cref="ProfileService.Profiles"/> through a view (<see cref="ICollectionView"/>)
    /// with its own filter and sort order. The page is cached by the navigation; each time it is shown the active
    /// profile is detected again in the background.
    /// </remarks>
    public partial class NetworkPage : Page
    {
        private const string FileFilter = "IP Switch (*.json)|*.json|All files (*.*)|*.*";

        /// <summary>Grid width below which the DNS columns are hidden.</summary>
        private const double HideDnsBelow = 900;

        /// <summary>Grid width below which the mask and gateway columns are hidden as well.</summary>
        private const double HideAddressingBelow = 700;

        /// <summary>Grid width below which the adapter column is hidden too (profiles are still grouped by adapter).</summary>
        private const double HideInterfaceBelow = 480;

        /// <summary>Grid width below which the IP column is hidden too, so the action buttons always fit.</summary>
        private const double HideIpBelow = 420;

        /// <summary>Grid width below which "Add profile" shows only its icon, leaving room for the search box.</summary>
        private const double CompactHeaderBelow = 560;

        /// <summary>Width of the actions column (three buttons).</summary>
        private const double ActionsColumnWidth = 136;

        private readonly ICollectionView _view;
        private string _query = string.Empty;

        private static MainWindow? Main => Application.Current.MainWindow as MainWindow;

        /// <summary>Creates the page.</summary>
        public NetworkPage()
        {
            InitializeComponent();

            _view = new CollectionViewSource { Source = ProfileService.Profiles }.View;
            _view.Filter = MatchesQuery;
            ApplyDefaultSort();
            ProfilesDataGrid.ItemsSource = _view;

            Loaded += async (_, _) =>
            {
                Loc.LanguageChanged += OnLanguageChanged;
                ProfileService.Profiles.CollectionChanged += OnProfilesChanged;
                ApplyColumnHeaders();
                ApplyColumnVisibility();
                UpdateEmptyState();
                await ProfileService.DetectActiveProfilesAsync();
            };
            Unloaded += (_, _) =>
            {
                Loc.LanguageChanged -= OnLanguageChanged;
                ProfileService.Profiles.CollectionChanged -= OnProfilesChanged;
            };
        }

        #region Columns

        // DataGridColumn is not part of the visual tree: DynamicResource does not work on Header
        private void ApplyColumnHeaders()
        {
            ColName.Header = Loc.Get("Col_Name");
            ColIp.Header = Loc.Get("Col_IP");
            ColMask.Header = Loc.Get("Col_Mask");
            ColGateway.Header = Loc.Get("Col_Gateway");
            ColDns1.Header = Loc.Get("Col_Dns1");
            ColDns2.Header = Loc.Get("Col_Dns2");
            ColInterface.Header = Loc.Get("Col_Interface");
            ColActions.Header = Loc.Get("Col_Actions");
        }

        /// <summary>
        /// Shows each column if it is enabled in Settings and fits in the current width (responsive layout:
        /// on narrow widths the DNS columns are hidden first, then the mask and gateway columns).
        /// </summary>
        private void ApplyColumnVisibility()
        {
            var settings = App.Settings;
            double width = ProfilesDataGrid.ActualWidth > 0 ? ProfilesDataGrid.ActualWidth : double.MaxValue;
            bool roomForDns = width >= HideDnsBelow;
            bool roomForAddressing = width >= HideAddressingBelow;

            ColMask.Visibility = ToVisibility(settings.ShowSubnetMask && roomForAddressing);
            ColGateway.Visibility = ToVisibility(settings.ShowGateway && roomForAddressing);
            ColDns1.Visibility = ToVisibility(settings.ShowPrimaryDns && roomForDns);
            ColDns2.Visibility = ToVisibility(settings.ShowSecondaryDns && roomForDns);
            ColInterface.Visibility = ToVisibility(settings.ShowInterface && width >= HideInterfaceBelow);
            ColIp.Visibility = ToVisibility(width >= HideIpBelow);
            RestoreColumnWidths();

            // The text comes from the resources; when hidden, the tooltip still names the button
            if (width < CompactHeaderBelow)
                AddProfileButton.Content = null;
            else
                AddProfileButton.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "Profiles_Add");
        }

        /// <summary>
        /// Puts the column widths back. When the window gets very narrow, the DataGrid shrinks fixed and auto columns
        /// to their minimum and never widens them again, which clipped the action buttons.
        /// </summary>
        private void RestoreColumnWidths()
        {
            ColName.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            ColActions.Width = new DataGridLength(ActionsColumnWidth);
            foreach (var column in new DataGridColumn[] { ColIp, ColMask, ColGateway, ColDns1, ColDns2, ColInterface })
                column.Width = DataGridLength.Auto;
        }

        private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) ApplyColumnVisibility();
        }

        private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            ApplyColumnHeaders();
            _view.Refresh(); // Recomputes the localized validation messages
        }

        #endregion

        #region Filtering and sorting

        private bool MatchesQuery(object item)
        {
            if (string.IsNullOrEmpty(_query)) return true;
            if (item is not IPProfile p) return false;

            return Contains(p.Name) || Contains(p.IPAddress) || Contains(p.Interface) || Contains(p.Notes);

            bool Contains(string? value) => value?.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            _query = SearchBox.Text?.Trim() ?? string.Empty;
            _view.Refresh();
            UpdateEmptyState();
        }

        private void ApplyDefaultSort()
        {
            _view.SortDescriptions.Clear();
            _view.SortDescriptions.Add(new SortDescription(nameof(IPProfile.Interface), ListSortDirection.Ascending));
            _view.SortDescriptions.Add(new SortDescription(nameof(IPProfile.Name), ListSortDirection.Ascending));
        }

        /// <summary>Per-column sort cycle: ascending → descending → default order.</summary>
        private void OnSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            var column = e.Column;
            string property = column.SortMemberPath;
            if (string.IsNullOrWhiteSpace(property)) return;

            ListSortDirection? next = column.SortDirection switch
            {
                null => ListSortDirection.Ascending,
                ListSortDirection.Ascending => ListSortDirection.Descending,
                _ => null
            };

            foreach (var col in ProfilesDataGrid.Columns)
                col.SortDirection = null;

            if (next.HasValue)
            {
                _view.SortDescriptions.Clear();
                _view.SortDescriptions.Add(new SortDescription(property, next.Value));
                column.SortDirection = next;
            }
            else
            {
                ApplyDefaultSort();
            }
        }

        private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

        private void UpdateEmptyState()
        {
            bool isEmpty = ProfileService.Profiles.Count == 0;
            EmptyText.Visibility = ToVisibility(isEmpty);
            ProfilesDataGrid.Visibility = ToVisibility(!isEmpty);
        }

        #endregion

        #region Add, edit, duplicate and delete

        private void OnAddProfileClick(object sender, RoutedEventArgs e) => OpenEditor(null, isNew: true);

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (GetProfile(sender) is { } profile) OpenEditor(profile, isNew: false);
        }

        private void OnDuplicateClick(object sender, RoutedEventArgs e)
        {
            if (GetProfile(sender) is not { } profile) return;

            var copy = profile.Clone(ProfileTransferService.GetUniqueName(
                ProfileService.Profiles, Loc.Format("Profiles_CopyName", profile.Name)));
            copy.Hotkey = string.Empty; // A hotkey cannot be repeated
            OpenEditor(copy, isNew: true);
        }

        /// <summary>Opens the editor; on save, adds or replaces the profile and detects the active one again.</summary>
        /// <param name="profile">Profile to edit, or template for a new one; <see langword="null"/> for an empty one.</param>
        /// <param name="isNew">Whether the result is a new profile (add, duplicate) or replaces <paramref name="profile"/>.</param>
        private async void OpenEditor(IPProfile? profile, bool isNew)
        {
            var window = new AddEditWindow(ProfileService.Profiles.ToList(), profile, isNew)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true || window.ResultProfile == null) return;

            if (isNew)
            {
                ProfileService.Profiles.Add(window.ResultProfile);
            }
            else
            {
                int index = ProfileService.Profiles.IndexOf(profile!);
                if (index < 0) return;
                ProfileService.Profiles[index] = window.ResultProfile;
            }

            ProfileService.Save();
            await ProfileService.DetectActiveProfilesAsync();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (GetProfile(sender) is not { } profile || Main == null) return;

            bool confirmed = await Main.ConfirmAsync(
                Loc.Get("Delete_Title"),
                Loc.Format("Delete_Message", profile.Name),
                Loc.Get("Common_Delete"));

            if (!confirmed) return;

            ProfileService.Profiles.Remove(profile);
            ProfileService.Save();
        }

        private void OnMoreClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement button) return;

            var menu = (ContextMenu)FindResource("RowMenu");
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OnShortcutClick(object sender, RoutedEventArgs e)
        {
            if (GetProfile(sender) is not { } profile) return;

            try
            {
                string path = ShortcutService.CreateDesktopShortcut(profile);
                Main?.ShowNotification(Loc.Get("Notify_Success"), Loc.Format("Profiles_ShortcutCreated", Path.GetFileName(path)), ControlAppearance.Success);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
            {
                Main?.ShowNotification(Loc.Get("Notify_Error"), ex.Message, ControlAppearance.Danger);
            }
        }

        #endregion

        #region Import and export

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = FileFilter,
                FileName = "ip-switch-profiles.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                ProfileTransferService.Export(dialog.FileName, ProfileService.Profiles);
                Main?.ShowNotification(Loc.Get("Notify_Success"),
                    Loc.Format("Profiles_Exported", ProfileService.Profiles.Count), ControlAppearance.Success);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Main?.ShowNotification(Loc.Get("Notify_Error"), ex.Message, ControlAppearance.Danger);
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = FileFilter };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            try
            {
                var imported = ProfileTransferService.Read(dialog.FileName);
                int added = ProfileTransferService.Merge(ProfileService.Profiles, imported);
                ProfileService.Save();
                Main?.ShowNotification(Loc.Get("Notify_Success"), Loc.Format("Profiles_Imported", added), ControlAppearance.Success);
                await ProfileService.DetectActiveProfilesAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Main?.ShowNotification(Loc.Get("Notify_Error"), Loc.Format("Profiles_ImportFailed", ex.Message), ControlAppearance.Danger);
            }
        }

        #endregion

        #region Apply

        private async void OnApplyClick(object sender, RoutedEventArgs e)
        {
            if (GetProfile(sender) is { } profile && Main != null)
                await Main.ApplyProfileAsync(profile);
        }

        private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Only when the double click is on a row (not on the header or on the buttons)
            var source = e.OriginalSource as DependencyObject;
            if (FindAncestor<DataGridRow>(source) is not { Item: IPProfile profile }) return;
            if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) != null) return;

            if (Main != null)
                await Main.ApplyProfileAsync(profile);
        }

        private async void OnRefreshStatusClick(object sender, RoutedEventArgs e)
        {
            Main?.ShowNotification(Loc.Get("Notify_Scanning"), Loc.Get("Notify_ScanningMsg"), ControlAppearance.Info);
            await ProfileService.DetectActiveProfilesAsync();
        }

        #endregion

        private static IPProfile? GetProfile(object sender) =>
            (sender as FrameworkElement)?.DataContext as IPProfile;

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null && current is not T)
                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            return current as T;
        }
    }
}
