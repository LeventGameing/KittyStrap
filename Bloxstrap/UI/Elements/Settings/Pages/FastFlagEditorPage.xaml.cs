﻿using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Collections.ObjectModel;

using Wpf.Ui.Mvvm.Contracts;

using Bloxstrap.UI.Elements.Dialogs;
using Microsoft.Win32;
using System.Windows.Media.Animation;


namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagEditorPage.xaml
    /// </summary>
    public partial class FastFlagEditorPage
    {


        private readonly ObservableCollection<FastFlag> _fastFlagList = new();

        private bool _showPresets = true;
        private string _searchFilter = string.Empty;
        private string _lastSearch = string.Empty;
        private DateTime _lastSearchTime = DateTime.MinValue;
        private const int _debounceDelay = 70;



        public FastFlagEditorPage()
        {
            InitializeComponent();
            SetDefaultStates();
        }

        private void SetDefaultStates()
        {
            TogglePresetsButton.IsChecked = true;
        }


        private void ReloadList()
        {
            var selectedEntry = DataGrid.SelectedItem as FastFlag;

            _fastFlagList.Clear();

            var presetFlags = FastFlagManager.PresetFlags.Values;

            foreach (var pair in App.FastFlags.Prop.OrderBy(x => x.Key))
            {
                // Skip preset flags if not shown
                if (!_showPresets && presetFlags.Contains(pair.Key))
                    continue;

                // Skip if flag name doesn't match the search filter
                if (!pair.Key.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) &&
                    !pair.Value.ToString()?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) == true)
                    continue;

                var entry = new FastFlag
                {
                    Name = pair.Key,
                    Value = pair.Value.ToString()!
                };

                _fastFlagList.Add(entry);
            }

            if (DataGrid.ItemsSource is null)
                DataGrid.ItemsSource = _fastFlagList;

            if (selectedEntry is null)
                return;

            var newSelectedEntry = _fastFlagList.FirstOrDefault(x => x.Name == selectedEntry.Name);
            if (newSelectedEntry is null)
                return;

            DataGrid.SelectedItem = newSelectedEntry;
            DataGrid.ScrollIntoView(newSelectedEntry);
        }

        private void ClearSearch(bool refresh = true)
        {
            SearchTextBox.Text = "";
            _searchFilter = "";

            if (refresh)
                ReloadList();
        }

        private void ShowAddDialog()
        {
            var dialog = new AddFastFlagDialog();
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            if (dialog.Tabs.SelectedIndex == 0)
                AddSingle(dialog.FlagNameTextBox.Text.Trim(), dialog.FlagValueTextBox.Text);
            else if (dialog.Tabs.SelectedIndex == 1)
                ImportJSON(dialog.JsonTextBox.Text);
        }

        private void ShowProfilesDialog()
        {
            var dialog = new FlagProfilesDialog();
            dialog.ShowDialog();

            if (dialog.Result != MessageBoxResult.OK)
                return;

            if (dialog.Tabs.SelectedIndex == 0)
                App.FastFlags.SaveProfile(dialog.SaveProfile.Text);
            else if (dialog.Tabs.SelectedIndex == 1)
            {
                if (dialog.LoadProfile.SelectedValue == null)
                    return;
                App.FastFlags.LoadProfile(dialog.LoadProfile.SelectedValue.ToString(), dialog.ClearFlags.IsChecked);
            }

            Thread.Sleep(1000);
            ReloadList();
        }

        private void AddSingle(string name, string value)
        {
            FastFlag? entry;

            if (App.FastFlags.GetValue(name) is null)
            {
                entry = new FastFlag
                {
                    // Enabled = true,
                    Name = name,
                    Value = value
                };

                if (!name.Contains(_searchFilter))
                    ClearSearch();

                _fastFlagList.Add(entry);

                App.FastFlags.SetValue(entry.Name, entry.Value);
            }
            else
            {
                Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);

                bool refresh = false;

                if (!_showPresets && FastFlagManager.PresetFlags.Values.Contains(name))
                {
                    TogglePresetsButton.IsChecked = true;
                    _showPresets = true;
                    refresh = true;
                }

                if (!name.Contains(_searchFilter))
                {
                    ClearSearch(false);
                    refresh = true;
                }

                if (refresh)
                    ReloadList();

                entry = _fastFlagList.Where(x => x.Name == name).FirstOrDefault();
            }

            DataGrid.SelectedItem = entry;
            DataGrid.ScrollIntoView(entry);
        }

        private void ImportJSON(string json)
        {
            Dictionary<string, object>? list = null;

            json = json.Trim();

            // autocorrect where possible
            if (!json.StartsWith('{'))
                json = '{' + json;

            if (!json.EndsWith('}'))
            {
                int lastIndex = json.LastIndexOf('}');

                if (lastIndex == -1)
                    json += '}';
                else
                    json = json.Substring(0, lastIndex + 1);
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                list = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);

                if (list is null)
                    throw new Exception("JSON deserialization returned null");
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(
                    String.Format(Strings.Menu_FastFlagEditor_InvalidJSON, ex.Message),
                    MessageBoxImage.Error
                );

                ShowAddDialog();

                return;
            }

            if (list.Count > 16)
            {
                var result = Frontend.ShowMessageBox(
                    Strings.Menu_FastFlagEditor_LargeConfig,
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNo
                );

                if (result != MessageBoxResult.Yes)
                    return;
            }

            var conflictingFlags = App.FastFlags.Prop.Where(x => list.ContainsKey(x.Key)).Select(x => x.Key);
            bool overwriteConflicting = false;

            if (conflictingFlags.Any())
            {
                int count = conflictingFlags.Count();

                string message = String.Format(
                    Strings.Menu_FastFlagEditor_ConflictingImport,
                    count,
                    String.Join(", ", conflictingFlags.Take(25))
                );

                if (count > 25)
                    message += "...";

                var result = Frontend.ShowMessageBox(message, MessageBoxImage.Question, MessageBoxButton.YesNo);

                overwriteConflicting = result == MessageBoxResult.Yes;
            }

            foreach (var pair in list)
            {
                if (App.FastFlags.Prop.ContainsKey(pair.Key) && !overwriteConflicting)
                    continue;

                if (pair.Value is null)
                    continue;

                var val = pair.Value.ToString();

                if (val is null)
                    continue;

                App.FastFlags.SetValue(pair.Key, pair.Value);
            }

            ClearSearch();
        }

        // refresh list on page load to synchronize with preset page
        private void Page_Loaded(object sender, RoutedEventArgs e) => ReloadList();

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row.DataContext is not FastFlag entry)
                return;

            if (e.EditingElement is not TextBox textbox)
                return;

            switch (e.Column.Header)
            {
                case "Name":
                    string oldName = entry.Name;
                    string newName = textbox.Text;

                    if (newName == oldName)
                        return;

                    if (App.FastFlags.GetValue(newName) is not null)
                    {
                        Frontend.ShowMessageBox(Strings.Menu_FastFlagEditor_AlreadyExists, MessageBoxImage.Information);
                        e.Cancel = true;
                        textbox.Text = oldName;
                        return;
                    }

                    App.FastFlags.SetValue(oldName, null);
                    App.FastFlags.SetValue(newName, entry.Value);

                    if (!newName.Contains(_searchFilter))
                        ClearSearch();

                    entry.Name = newName;

                    break;

                case "Value":
                    string oldValue = entry.Value;
                    string newValue = textbox.Text;

                    App.FastFlags.SetValue(entry.Name, newValue);

                    break;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
                window.Navigate(typeof(FastFlagsPage));
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => ShowAddDialog();

        private void FlagProfiles_Click(object sender, RoutedEventArgs e) => ShowProfilesDialog();

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var tempList = new List<FastFlag>();

            foreach (FastFlag entry in DataGrid.SelectedItems)
                tempList.Add(entry);

            foreach (FastFlag entry in tempList)
            {
                _fastFlagList.Remove(entry);
                App.FastFlags.SetValue(entry.Name, null);
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton button)
                return;

            _showPresets = button.IsChecked ?? true;
            ReloadList();
        }

        private void ExportJSONButton_Click(object sender, RoutedEventArgs e)
        {
            var flags = App.FastFlags.Prop;

            // Group by flag prefix using regex (e.g., DFFlag, DFInt, FFlag, etc.)
            var groupedFlags = flags
                .GroupBy(kvp =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(kvp.Key, @"^[A-Z]+[a-z]*");
                    return match.Success ? match.Value : "Other";
                })
                .OrderBy(g => g.Key); // Prefix group ordering (alphabetical)

            var formattedJson = new StringBuilder();
            formattedJson.AppendLine("{");

            int totalItems = flags.Count;
            int writtenItems = 0;
            int groupIndex = 0;

            foreach (var group in groupedFlags)
            {
                // Optional: blank line between groups
                if (groupIndex > 0)
                    formattedJson.AppendLine();

                // Sort entries in this group by the length of the full line string
                var sortedGroup = group
                    .OrderByDescending(kvp => kvp.Key.Length + (kvp.Value?.ToString()?.Length ?? 0));

                foreach (var kvp in sortedGroup)
                {
                    writtenItems++;
                    bool isLast = (writtenItems == totalItems);
                    string line = $"    \"{kvp.Key}\": \"{kvp.Value}\"";

                    if (!isLast)
                        line += ",";

                    formattedJson.AppendLine(line);
                }

                groupIndex++;
            }

            formattedJson.AppendLine("}");

            // Save the formatted JSON to a file
            SaveJSONToFile(formattedJson.ToString());
        }


        private void CopyJSONButton_Click1(object sender, RoutedEventArgs e)
        {
            string json = JsonSerializer.Serialize(App.FastFlags.Prop, new JsonSerializerOptions { WriteIndented = true });

            Clipboard.SetText(json);
        }

        private void CopyJSONButton_Click2(object sender, RoutedEventArgs e)
        {
            var flags = App.FastFlags.Prop;

            // Group by flag prefix using regex (e.g., DFFlag, DFInt, FFlag, etc.)
            var groupedFlags = flags
                .GroupBy(kvp =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(kvp.Key, @"^[A-Z]+[a-z]*");
                    return match.Success ? match.Value : "Other";
                })
                .OrderBy(g => g.Key); // Prefix group ordering (alphabetical)

            var formattedJson = new StringBuilder();
            formattedJson.AppendLine("{");

            int totalItems = flags.Count;
            int writtenItems = 0;
            int groupIndex = 0;

            foreach (var group in groupedFlags)
            {
                // Optional: blank line between groups
                if (groupIndex > 0)
                    formattedJson.AppendLine();

                // Sort entries in this group by the length of the full line string
                // Fix for the null reference issue
                var sortedGroup = group
                    .OrderByDescending(kvp => kvp.Key.Length + (kvp.Value?.ToString()?.Length ?? 0));


                foreach (var kvp in sortedGroup)
                {
                    writtenItems++;
                    bool isLast = (writtenItems == totalItems);
                    string line = $"    \"{kvp.Key}\": \"{kvp.Value}\"";

                    if (!isLast)
                        line += ",";

                    formattedJson.AppendLine(line);
                }

                groupIndex++;
            }

            formattedJson.AppendLine("}");

            Clipboard.SetText(formattedJson.ToString());
        }


        private void SaveJSONToFile(string json)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|Text files (*.txt)|*.txt",
                Title = "Save JSON or TXT File",
                FileName = "FroststrapExport.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {

                    var filePath = saveFileDialog.FileName;
                    if (string.IsNullOrEmpty(Path.GetExtension(filePath)))
                    {
                        filePath += ".json";
                    }

                    File.WriteAllText(filePath, json);
                    Frontend.ShowMessageBox("JSON file saved successfully!", MessageBoxImage.Information);
                }
                catch (IOException ioEx)
                {
                    Frontend.ShowMessageBox($"Error saving file: {ioEx.Message}", MessageBoxImage.Error);
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    Frontend.ShowMessageBox($"Permission error: {uaEx.Message}", MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    Frontend.ShowMessageBox($"Unexpected error: {ex.Message}", MessageBoxImage.Error);
                }
            }
        }

        private void ShowDeleteAllFlagsConfirmation()
        {
            // Show a confirmation message box to the user
            if (Frontend.ShowMessageBox(
                "Are you sure you want to delete all flags?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return; // Exit if the user cancels the action
            }

            // Exit if there are no flags to delete
            if (!HasFlagsToDelete())
            {
                ShowInfoMessage("There are no flags to delete.");
                return;
            }

            try
            {
                DeleteAllFlags();
                ReloadUI();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }

        private bool HasFlagsToDelete()
        {
            return _fastFlagList.Any() || App.FastFlags.Prop.Any();
        }

        private void DeleteAllFlags()
        {

            _fastFlagList.Clear();


            foreach (var key in App.FastFlags.Prop.Keys.ToList())
            {
                App.FastFlags.SetValue(key, null);
            }
        }

        private void ReloadUI()
        {
            ReloadList();
        }

        private void ShowInfoMessage(string message)
        {
            Frontend.ShowMessageBox(message, MessageBoxImage.Information, MessageBoxButton.OK);
        }

        private void HandleError(Exception ex)
        {
            // Display and log the error message
            Frontend.ShowMessageBox($"An error occurred while deleting flags:\n{ex.Message}", MessageBoxImage.Error, MessageBoxButton.OK);
            LogError(ex); // Logging error in a centralized method
        }

        private void LogError(Exception ex)
        {
            // Detailed logging for developers
            Console.WriteLine(ex.ToString());
        }


        private void DeleteAllButton_Click(object sender, RoutedEventArgs e) => ShowDeleteAllFlagsConfirmation();

        private CancellationTokenSource? _searchCancellationTokenSource;

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textbox) return;

            string newSearch = textbox.Text.Trim();

            // Return early if the search text hasn't changed or the debounce delay hasn't passed
            if (newSearch == _lastSearch && (DateTime.Now - _lastSearchTime).TotalMilliseconds < _debounceDelay)
                return;

            // Cancel the previous search operation if ongoing
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();

            _searchFilter = newSearch;
            _lastSearch = newSearch;
            _lastSearchTime = DateTime.Now;

            try
            {
                // Apply debounce delay using Task.Delay without blocking the UI
                await Task.Delay(_debounceDelay, _searchCancellationTokenSource.Token);

                // If the task was cancelled, exit early
                if (_searchCancellationTokenSource.Token.IsCancellationRequested)
                    return;

                // Reload the list and show search suggestion after debounce delay
                Dispatcher.Invoke(() =>
                {
                    ReloadList();
                    ShowSearchSuggestion(newSearch);
                });
            }
            catch (TaskCanceledException)
            {
                // Handle task cancellation gracefully
            }
        }


        private void ShowSearchSuggestion(string searchFilter)
        {
            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                AnimateSuggestionVisibility(0);
                return;
            }

            // Smarter search: Prioritize matches that start with the filter
            var bestMatch = App.FastFlags.Prop.Keys
                .Where(flag => flag.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(flag => !flag.StartsWith(searchFilter, StringComparison.OrdinalIgnoreCase))
                .ThenBy(flag => flag.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase))
                .ThenBy(flag => flag.Length)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(bestMatch))
            {
                SuggestionTextBlock.Text = $"Looking For ({bestMatch})?";
                AnimateSuggestionVisibility(1);
            }
            else
            {
                AnimateSuggestionVisibility(0);
            }
        }


        private void AnimateSuggestionVisibility(double targetOpacity)
        {
            var opacityAnimation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(95),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            opacityAnimation.Completed += (s, e) =>
            {
                if (targetOpacity == 0)
                {
                    SuggestionTextBlock.Visibility = Visibility.Collapsed;
                }
            };

            if (targetOpacity > 0)
            {
                SuggestionTextBlock.Visibility = Visibility.Visible;
            }

            SuggestionTextBlock.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }
    }
}