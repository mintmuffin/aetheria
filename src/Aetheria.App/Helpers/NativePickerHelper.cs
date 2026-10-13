using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Aetheria.App.Helpers;

public static class NativePickerHelper
{
    public static Task<string?> PickOpenFileAsync(params string[] fileTypeFilters) =>
        PickOpenFileAsync(App.MainWindowInstance, fileTypeFilters);

    public static async Task<string?> PickOpenFileAsync(Window window, params string[] fileTypeFilters)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        foreach (var ext in fileTypeFilters)
        {
            picker.FileTypeFilter.Add(ext);
        }
        if (picker.FileTypeFilter.Count == 0)
        {
            picker.FileTypeFilter.Add("*");
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static Task<string?> PickSaveFileAsync(
        string suggestedFileName,
        string extension,
        string fileTypeDescription) =>
        PickSaveFileAsync(App.MainWindowInstance, suggestedFileName, fileTypeDescription, extension);

    public static async Task<string?> PickSaveFileAsync(
        Window window,
        string suggestedFileName,
        string fileTypeDescription,
        string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName
        };
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        picker.FileTypeChoices.Add(fileTypeDescription, new List<string> { ext });

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
