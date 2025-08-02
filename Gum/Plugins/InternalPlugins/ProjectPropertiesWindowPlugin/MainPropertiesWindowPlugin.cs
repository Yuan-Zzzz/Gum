using Gum.DataTypes;
using Gum.Gui.Controls;
using Gum.Managers;
using Gum.Plugins.BaseClasses;
using Gum.ToolStates;
using Gum.Wireframe;
using RenderingLibrary.Graphics.Fonts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Gum.Commands;
using ToolsUtilities;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;
using Matrix = System.Numerics.Matrix4x4;
using Gum.Services;
using Gum.Services.Dialogs;

namespace Gum.Plugins.PropertiesWindowPlugin;

/// <summary>
/// Plugin for displaying project properties
/// </summary>
[Export(typeof(PluginBase))]
class MainPropertiesWindowPlugin : InternalPlugin
{
    #region Fields/Properties

    ProjectPropertiesControl control;

    ProjectPropertiesViewModel viewModel;
    [Import("LocalizationManager")]
    public LocalizationManager LocalizationManager
    {
        get;
        set;
    }
    #endregion

    private readonly FontManager _fontManager;
    private readonly WireframeCommands _wireframeCommands;
    private readonly IDialogService _dialogService;

    public MainPropertiesWindowPlugin()
    {
        _fontManager = Locator.GetRequiredService<FontManager>();
        _wireframeCommands = Locator.GetRequiredService<WireframeCommands>();
        _dialogService = Locator.GetRequiredService<IDialogService>();
    }

    public override void StartUp()
    {
        this.AddMenuItem(new List<string> { "Edit", "Properties" }).Click += HandlePropertiesClicked;

        viewModel = new PropertiesWindowPlugin.ProjectPropertiesViewModel();
        viewModel.PropertyChanged += HandlePropertyChanged;

        // todo - handle loading new Gum project when this window is shown - re-call BindTo
        this.ProjectLoad += HandleProjectLoad;
    }

    private void HandleProjectLoad(GumProjectSave obj)
    {
        if (control != null && viewModel != null)
        {
            viewModel.SetFrom(ProjectManager.Self.GeneralSettingsFile, ProjectState.Self.GumProjectSave);
            control.ViewModel = null;
            control.ViewModel = viewModel;
        }
    }

    private void HandlePropertiesClicked(object sender, EventArgs e)
    {
        try
        {
            if (control == null)
            {
                control = new ProjectPropertiesControl();
                control.CloseClicked += HandleCloseClicked;
            }

            viewModel.SetFrom(ProjectManager.Self.GeneralSettingsFile, ProjectState.Self.GumProjectSave);
            var wasShown = 
                _guiCommands.ShowTabForControl(control);

            if(!wasShown)
            {
                var tab = _guiCommands.AddControl(control, "Project Properties");
                tab.CanClose = true;
                control.ViewModel = viewModel;
                _guiCommands.ShowTabForControl(control);
            }
        }
        catch (Exception ex)
        {
            _guiCommands.PrintOutput($"Error showing project properties:\n{ex.ToString()}");
        }
    }

    private async void HandlePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        ////////////////////Early Out//////////////////
        if (viewModel.IsUpdatingFromModel)
        {
            return;
        }
        ///////////////////End early Out////////////////
        viewModel.ApplyToModelObjects();

        var shouldSaveAndRefresh = true;
        var shouldReloadContent = false;
        switch (e.PropertyName)
        {
            case nameof(viewModel.LocalizationFile):


                if (!string.IsNullOrEmpty(viewModel.LocalizationFile) && FileManager.IsRelative(viewModel.LocalizationFile) == false)
                {
                    viewModel.LocalizationFile = FileManager.MakeRelative(viewModel.LocalizationFile,
                        GumState.Self.ProjectState.ProjectDirectory, preserveCase:true);
                    shouldSaveAndRefresh = false;
                }
                else
                {
                    _fileCommands.LoadLocalizationFile();

                    WireframeObjectManager.Self.RefreshAll(forceLayout: true, forceReloadTextures: false);
                }
                break;
            case nameof(viewModel.LanguageIndex):
                LocalizationManager.CurrentLanguage = viewModel.LanguageIndex;
                break;
            case nameof(viewModel.ShowLocalization):
                shouldSaveAndRefresh = true;
                break;
            case nameof(viewModel.FontCharacterFile):
                if(!string.IsNullOrEmpty(viewModel.FontCharacterFile) && FileManager.IsRelative(viewModel.FontCharacterFile) == false)
                {
                    viewModel.FontCharacterFile = FileManager.MakeRelative(viewModel.FontCharacterFile,
                        GumState.Self.ProjectState.ProjectDirectory, preserveCase:true);
                    shouldSaveAndRefresh = false;
                }
                else if(viewModel.UseFontCharacterFile && !string.IsNullOrEmpty(viewModel.FontCharacterFile))
                {
                    var absolute = FileManager.MakeAbsolute(viewModel.FontCharacterFile,
                        GumState.Self.ProjectState.ProjectDirectory);
                    if(File.Exists(absolute))
                    {
                        try
                        {
                            var ranges = GetFontRangesFromFile(absolute);
                            if(!string.IsNullOrEmpty(ranges))
                            {
                                viewModel.FontRanges = ranges;
                            }
                        }
                        catch(Exception ex)
                        {
                            _dialogService.ShowMessage($"Error reading font character file:\n{ex}");
                        }
                    }
                }
                break;
            case nameof(viewModel.UseFontCharacterFile):
                if(viewModel.UseFontCharacterFile)
                {
                    if(!string.IsNullOrEmpty(viewModel.FontCharacterFile))
                    {
                        var absolute = FileManager.MakeAbsolute(viewModel.FontCharacterFile,
                            GumState.Self.ProjectState.ProjectDirectory);
                        if(File.Exists(absolute))
                        {
                            try
                            {
                                var ranges = GetFontRangesFromFile(absolute);
                                if(!string.IsNullOrEmpty(ranges))
                                {
                                    viewModel.FontRanges = ranges;
                                }
                            }
                            catch(Exception ex)
                            {
                                _dialogService.ShowMessage($"Error reading font character file:\n{ex}");
                            }
                        }
                    }
                }
                break;
            case nameof(viewModel.FontRanges):
                var isValid = BmfcSave.GetIfIsValidRange(viewModel.FontRanges);
                var didFixChangeThings = false;
                if (!isValid)
                {
                    var fixedRange = BmfcSave.TryFixRange(viewModel.FontRanges);
                    if (fixedRange != viewModel.FontRanges)
                    {
                        // this will recursively call this property, so we'll use this bool to leave this method
                        didFixChangeThings = true;
                        viewModel.FontRanges = fixedRange;
                    }
                }

                if (!didFixChangeThings)
                {
                    if (isValid == false)
                    {
                        _dialogService.ShowMessage("The entered Font Range is not valid.");
                    }
                    else
                    {
                        if (GumState.Self.ProjectState.GumProjectSave != null)
                        {
                            var wasAbleToDelete = false;
                            try
                            {
                                _fontManager.DeleteFontCacheFolder();
                                wasAbleToDelete = true;
                            }
                            catch(System.IO.IOException exception)
                            {
                                wasAbleToDelete = false;

                                var message =
                                    "Attempted to delete font cache folder to re-create it with the new font range values " +
                                    $"but was unable to do so:\n\n{exception}";
                                _dialogService.ShowMessage(message);
                            }




                            if(wasAbleToDelete)
                            {
                                await _fontManager.CreateAllMissingFontFiles(
                                    ProjectState.Self.GumProjectSave);
                            }

                        }
                        shouldSaveAndRefresh = true;
                        shouldReloadContent = true;
                    }
                }
                break;
            case nameof(viewModel.GuideLineColor):
                _wireframeCommands.RefreshGuides();
                break;
            case nameof(viewModel.SinglePixelTextureFile):
            case nameof(viewModel.SinglePixelTextureTop):
            case nameof(viewModel.SinglePixelTextureLeft):
            case nameof(viewModel.SinglePixelTextureRight):
            case nameof(viewModel.SinglePixelTextureBottom):

                if(!string.IsNullOrEmpty(viewModel.SinglePixelTextureFile) && FileManager.IsRelative(viewModel.SinglePixelTextureFile) == false)
                {
                    // This will loop:
                    viewModel.SinglePixelTextureFile = FileManager.MakeRelative(viewModel.SinglePixelTextureFile,
                        GumState.Self.ProjectState.ProjectDirectory, preserveCase:true);
                    shouldSaveAndRefresh = false;
                }

                break;
        }

        PluginManager.Self.ProjectPropertySet(e.PropertyName);

        if (shouldSaveAndRefresh)
        {
            _wireframeCommands.Refresh(forceLayout: true, forceReloadContent: shouldReloadContent);

            _fileCommands.TryAutoSaveProject();
        }
    }


    private void HandleCloseClicked(object sender, EventArgs e)
    {
        _guiCommands.RemoveControl(control);
    }

    private static string GetFontRangesFromFile(string absoluteFile)
    {
        var text = File.ReadAllText(absoluteFile);
        var codepoints = new HashSet<int>();
        for(int i=0; i<text.Length; i++)
        {
            int codepoint = char.ConvertToUtf32(text, i);
            codepoints.Add(codepoint);
            if(char.IsHighSurrogate(text[i]))
            {
                i++;
            }
        }

        var ordered = codepoints.OrderBy(item => item).ToList();
        if(ordered.Count == 0)
        {
            return string.Empty;
        }

        var ranges = new List<string>();
        int start = ordered[0];
        int previous = ordered[0];
        for(int index = 1; index < ordered.Count; index++)
        {
            var current = ordered[index];
            if(current == previous + 1)
            {
                previous = current;
            }
            else
            {
                ranges.Add(start == previous ? start.ToString() : $"{start}-{previous}");
                start = previous = current;
            }
        }
        ranges.Add(start == previous ? start.ToString() : $"{start}-{previous}");
        return string.Join(",", ranges);
    }
}
