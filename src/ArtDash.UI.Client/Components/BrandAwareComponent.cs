using ArtDash.UI.Client.Theming;

namespace ArtDash.UI.Client.Components;

/// <summary>
/// Base for components that draw the tenant's brand (logo, colors). They re-render by themselves when the
/// brand changes — during a live preview in the branding editor, for instance — even when their parent
/// does not. Derived classes overriding <see cref="OnInitialized"/> must call the base.
/// </summary>
public abstract class BrandAwareComponent : ComponentBase, IDisposable
{
    [Inject] protected BrandingState Brand { get; set; } = default!;

    protected override void OnInitialized() => Brand.Changed += OnBrandChanged;

    private void OnBrandChanged() => _ = InvokeAsync(StateHasChanged);

    protected virtual void OnDisposing()
    {
    }

    public void Dispose()
    {
        Brand.Changed -= OnBrandChanged;
        OnDisposing();
        GC.SuppressFinalize(this);
    }
}
