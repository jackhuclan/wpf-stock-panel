namespace Tsp.Desktop.ViewModels.Navigation;

/// <summary>
/// <see cref="INavigationService"/> 的默认实现。<see cref="Current"/> 恒为返回栈顶，
/// 唯一例外是空状态：未知路由键既不入栈也不清栈，只把 <see cref="Current"/> 置 <c>null</c>，
/// 于是返回栈仍保留此前的有效路由，<see cref="GoBack"/> 的行为与那次未知导航之前完全一致。
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly Stack<PageRoute> _backStack = new();

    /// <inheritdoc />
    public PageRoute? Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<PageRoute?>? CurrentChanged;

    /// <inheritdoc />
    public bool CanGoBack => _backStack.Count > 1;

    /// <inheritdoc />
    public void Navigate(string routeKey)
    {
        PageRoute? route = Routes.Find(routeKey);
        if (route is null)
        {
            // 未知路由是正常情况，不是异常：停在空状态页，且不动返回栈。
            Current = null;
            CurrentChanged?.Invoke(this, null);
            return;
        }

        _backStack.Push(route);
        Current = route;
        CurrentChanged?.Invoke(this, route);
    }

    /// <inheritdoc />
    public void GoBack()
    {
        if (_backStack.Count <= 1)
        {
            return;
        }

        _backStack.Pop();
        Current = _backStack.Peek();
        CurrentChanged?.Invoke(this, Current);
    }
}
