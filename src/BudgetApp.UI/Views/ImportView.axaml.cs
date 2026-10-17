using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using BudgetApp.UI.Services;
using BudgetApp.UI.ViewModels;

namespace BudgetApp.UI.Views;

public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();

        DropZone.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (HasFile(e))
            DropZone.Classes.Add("dragging");
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = HasFile(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragLeave(object? sender, DragEventArgs e) => DropZone.Classes.Remove("dragging");

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("dragging");
        if (DataContext is not ImportViewModel vm || e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToList() is not { Count: > 0 } files)
            return;

        var statements = new List<BudgetApp.Core.Import.StatementFile>();
        foreach (var file in files)
            statements.Add(await PlatformDialogs.ReadStatementAsync(file));
        await vm.LoadFilesAsync(statements);
    }

    private static bool HasFile(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);
}
