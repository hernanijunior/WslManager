using Wpf.Ui.Abstractions;

namespace WslManager.Views;

/// <summary>
/// Fornece ao <c>NavigationView</c> instâncias já construídas das páginas, para
/// que elas compartilhem o mesmo <see cref="ViewModels.MainViewModel"/> (uma só
/// lista, um só timer). Páginas não registradas caem no <see cref="Activator"/>.
/// </summary>
public sealed class PageProvider : INavigationViewPageProvider
{
    private readonly Dictionary<Type, object> _pages;

    public PageProvider(params object[] pages)
        => _pages = pages.ToDictionary(p => p.GetType());

    public object? GetPage(Type pageType)
        => _pages.TryGetValue(pageType, out var page)
            ? page
            : Activator.CreateInstance(pageType);
}
