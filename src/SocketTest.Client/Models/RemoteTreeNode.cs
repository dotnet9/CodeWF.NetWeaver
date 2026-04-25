using ReactiveUI;
using System.Collections.ObjectModel;

namespace SocketTest.Client.Models;

public class RemoteTreeNode : ReactiveObject
{
    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; } = true;

    public bool IsVirtual { get; init; }

    public bool IsDrive { get; init; }

    public bool IsPlaceholder { get; init; }

    public bool IsExpanded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSelected
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsLoading
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool ChildrenLoaded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ObservableCollection<RemoteTreeNode> Children { get; } = [];

    public string IconText => IsPlaceholder
        ? "..."
        : IsVirtual
            ? "PC"
            : IsDrive
                ? "DRV"
                : IsDirectory
                    ? "DIR"
                    : "FILE";

    public override string ToString() => Name;
}
