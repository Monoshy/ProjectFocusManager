using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusManager.App.Mvvm;

/// <summary>
/// 最简的「属性变了要通知界面」基类。
/// 界面绑定的值一改，窗口上的文字就自动跟着变，不用手动刷新。
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>赋值并在值确实变化时发出通知。返回值表示是否真的变了。</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
