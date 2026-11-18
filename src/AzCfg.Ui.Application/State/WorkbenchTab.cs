namespace AzCfg.Ui.Application.State;

public sealed record WorkbenchTab(Guid Id, TabDescriptor Descriptor, string Title, bool IsDirty, bool IsLoaded);
