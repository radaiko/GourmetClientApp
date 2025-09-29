using CommunityToolkit.Mvvm.ComponentModel;

namespace GourmetClientApp.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public abstract void Initialize();
}