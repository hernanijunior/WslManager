namespace WslManager.Core;

/// <summary>Uma distro disponível no catálogo (<c>wsl --list --online</c>).</summary>
public sealed record OnlineDistro(string Name, string FriendlyName);
