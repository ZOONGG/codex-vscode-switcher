namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ManagedRuntimeStateResetService(IManagedInstanceStore instanceStore)
{
    public void Reset()
        => instanceStore.Clear();
}
