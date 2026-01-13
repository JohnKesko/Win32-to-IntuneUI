using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Win32_to_IntuneUI.Models;

namespace Win32_to_IntuneUI.Views;

public partial class DetectionRulesEditorDialog : Window
{
    private readonly ObservableCollection<DetectionRuleViewModel> _rules = [];
    private DetectionRuleViewModel? _selectedRule;
    private readonly string _appName;
    private bool _isInitialized;

    public List<DetectionRule>? Result { get; private set; }

    public DetectionRulesEditorDialog()
    {
        InitializeComponent();
        _appName = string.Empty;
        RulesListBox.ItemsSource = _rules;
        RegistryHiveCombo.SelectedIndex = 0;
        RegistryDetectionMethodCombo.SelectedIndex = 0;
        RegistryOperatorCombo.SelectedIndex = 0;
        FileDetectionMethodCombo.SelectedIndex = 0;
        MsiVersionOperatorCombo.SelectedIndex = 0;
        _isInitialized = true;
    }

    public DetectionRulesEditorDialog(string appName, List<DetectionRule>? existingRules) : this()
    {
        _appName = appName;
        AppNameText.Text = appName;

        if (existingRules != null)
        {
            foreach (var rule in existingRules)
            {
                _rules.Add(new DetectionRuleViewModel(rule));
            }
        }

        UpdateEditorVisibility();
    }

    private void RulesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRule = RulesListBox.SelectedItem as DetectionRuleViewModel;
        if (_selectedRule != null)
        {
            LoadRuleIntoEditor(_selectedRule);
        }
    }

    private void LoadRuleIntoEditor(DetectionRuleViewModel rule)
    {
        // Set rule type
        var typeIndex = rule.Type switch
        {
            "registry" => 0,
            "file" => 1,
            "msi" => 2,
            "script" => 3,
            _ => 0
        };
        RuleTypeCombo.SelectedIndex = typeIndex;
        UpdateEditorVisibility();

        // Load type-specific fields
        switch (rule.Type)
        {
            case "registry":
                SelectComboItemByTag(RegistryHiveCombo, rule.Rule.Hive ?? "localMachine");
                RegistryKeyPathBox.Text = rule.Rule.KeyPath ?? "";
                RegistryValueNameBox.Text = rule.Rule.ValueName ?? "";
                SelectComboItemByTag(RegistryDetectionMethodCombo, rule.Rule.DetectionMethod ?? "exists");
                SelectComboItemByTag(RegistryOperatorCombo, rule.Rule.Operator ?? "equal");
                RegistryExpectedValueBox.Text = rule.Rule.DetectionValue ?? "";
                Registry32BitCheckBox.IsChecked = rule.Rule.Check32BitOn64System;
                UpdateRegistryValuePanelVisibility();
                break;

            case "file":
                FilePathBox.Text = rule.Rule.Path ?? "";
                FileNameBox.Text = rule.Rule.FileOrFolderName ?? "";
                SelectComboItemByTag(FileDetectionMethodCombo, rule.Rule.DetectionMethod ?? "exists");
                File32BitCheckBox.IsChecked = rule.Rule.Check32BitOn64System;
                break;

            case "msi":
                MsiProductCodeBox.Text = rule.Rule.ProductCode ?? "";
                SelectComboItemByTag(MsiVersionOperatorCombo, rule.Rule.ProductVersionOperator ?? "notConfigured");
                MsiVersionBox.Text = rule.Rule.ProductVersion ?? "";
                break;

            case "script":
                ScriptContentBox.Text = rule.Rule.ScriptContent ?? "";
                ScriptRunAs32BitCheckBox.IsChecked = rule.Rule.RunAs32Bit;
                ScriptEnforceSignatureCheckBox.IsChecked = rule.Rule.EnforceSignatureCheck;
                break;
        }
    }

    private void SelectComboItemByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == tag)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private string? GetSelectedComboTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }

    private void RuleType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitialized)
        {
            UpdateEditorVisibility();
        }
    }

    private void UpdateEditorVisibility()
    {
        // Guard against null panels during initialization
        if (RegistryPanel == null || FilePanel == null || MsiPanel == null || ScriptPanel == null)
            return;

        var selectedType = GetSelectedComboTag(RuleTypeCombo) ?? "registry";

        RegistryPanel.IsVisible = selectedType == "registry";
        FilePanel.IsVisible = selectedType == "file";
        MsiPanel.IsVisible = selectedType == "msi";
        ScriptPanel.IsVisible = selectedType == "script";
    }

    private void UpdateRegistryValuePanelVisibility()
    {
        var method = GetSelectedComboTag(RegistryDetectionMethodCombo);
        RegistryValuePanel.IsVisible = method is "string" or "integer" or "version";
    }

    private void AddRule_Click(object? sender, RoutedEventArgs e)
    {
        var newRule = new DetectionRule { Type = "registry" };
        var viewModel = new DetectionRuleViewModel(newRule);
        _rules.Add(viewModel);
        RulesListBox.SelectedItem = viewModel;
    }

    private void RemoveRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedRule != null)
        {
            var index = _rules.IndexOf(_selectedRule);
            _rules.Remove(_selectedRule);

            if (_rules.Count > 0)
            {
                RulesListBox.SelectedIndex = System.Math.Min(index, _rules.Count - 1);
            }
            else
            {
                _selectedRule = null;
            }
        }
    }

    private void ApplyRule_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedRule == null) return;

        var rule = _selectedRule.Rule;
        rule.Type = GetSelectedComboTag(RuleTypeCombo) ?? "registry";

        switch (rule.Type)
        {
            case "registry":
                rule.Hive = GetSelectedComboTag(RegistryHiveCombo);
                rule.KeyPath = RegistryKeyPathBox.Text;
                rule.ValueName = string.IsNullOrWhiteSpace(RegistryValueNameBox.Text) ? null : RegistryValueNameBox.Text;
                rule.DetectionMethod = GetSelectedComboTag(RegistryDetectionMethodCombo);
                rule.Operator = GetSelectedComboTag(RegistryOperatorCombo);
                rule.DetectionValue = RegistryExpectedValueBox.Text;
                rule.Check32BitOn64System = Registry32BitCheckBox.IsChecked ?? false;
                // Clear other type fields
                rule.Path = null;
                rule.FileOrFolderName = null;
                rule.ProductCode = null;
                rule.ScriptContent = null;
                break;

            case "file":
                rule.Path = FilePathBox.Text;
                rule.FileOrFolderName = FileNameBox.Text;
                rule.DetectionMethod = GetSelectedComboTag(FileDetectionMethodCombo);
                rule.Check32BitOn64System = File32BitCheckBox.IsChecked ?? false;
                // Clear other type fields
                rule.Hive = null;
                rule.KeyPath = null;
                rule.ValueName = null;
                rule.ProductCode = null;
                rule.ScriptContent = null;
                break;

            case "msi":
                rule.ProductCode = MsiProductCodeBox.Text;
                rule.ProductVersionOperator = GetSelectedComboTag(MsiVersionOperatorCombo);
                rule.ProductVersion = string.IsNullOrWhiteSpace(MsiVersionBox.Text) ? null : MsiVersionBox.Text;
                // Clear other type fields
                rule.Hive = null;
                rule.KeyPath = null;
                rule.Path = null;
                rule.FileOrFolderName = null;
                rule.ScriptContent = null;
                break;

            case "script":
                rule.ScriptContent = ScriptContentBox.Text;
                rule.RunAs32Bit = ScriptRunAs32BitCheckBox.IsChecked ?? false;
                rule.EnforceSignatureCheck = ScriptEnforceSignatureCheckBox.IsChecked ?? false;
                // Clear other type fields
                rule.Hive = null;
                rule.KeyPath = null;
                rule.Path = null;
                rule.FileOrFolderName = null;
                rule.ProductCode = null;
                break;
        }

        _selectedRule.UpdateSummary();

        // Refresh the ListBox to show updated summary
        var index = RulesListBox.SelectedIndex;
        RulesListBox.ItemsSource = null;
        RulesListBox.ItemsSource = _rules;
        RulesListBox.SelectedIndex = index;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        // Apply any pending changes to current rule
        if (_selectedRule != null)
        {
            ApplyRule_Click(sender, e);
        }

        Result = _rules.Select(r => r.Rule).ToList();
        Close();
    }
}

/// <summary>
/// ViewModel wrapper for DetectionRule to provide display summary
/// </summary>
public class DetectionRuleViewModel
{
    public DetectionRule Rule { get; }
    public string Type => Rule.Type;
    public string Summary { get; private set; }

    public DetectionRuleViewModel(DetectionRule rule)
    {
        Rule = rule;
        Summary = GenerateSummary();
    }

    public void UpdateSummary()
    {
        Summary = GenerateSummary();
    }

    private string GenerateSummary()
    {
        return Rule.Type switch
        {
            "registry" => TruncatePath(Rule.KeyPath, 25),
            "file" => TruncatePath($"{Rule.Path}\\{Rule.FileOrFolderName}", 25),
            "msi" => TruncatePath(Rule.ProductCode, 25),
            "script" => "Custom script",
            _ => ""
        };
    }

    private static string TruncatePath(string? path, int maxLength)
    {
        if (string.IsNullOrEmpty(path)) return "(not set)";
        if (path.Length <= maxLength) return path;
        return "..." + path[^(maxLength - 3)..];
    }
}
