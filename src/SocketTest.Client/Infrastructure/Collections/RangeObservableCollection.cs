using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SocketTest.Client.Infrastructure.Collections;

public class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool SuppressNotification { get; set; }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!SuppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    public new void Clear()
    {
        Items.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void AddRange(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        SuppressNotification = true;
        foreach (var item in collection)
        {
            Items.Add(item);
        }

        SuppressNotification = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
