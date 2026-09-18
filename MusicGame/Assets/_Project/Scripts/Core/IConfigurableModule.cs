/// <summary>
/// Opt-in — only a module with REAL editable configuration implements this (see each module's own
/// config SO doc for why it needs one). AppBootstrap calls Configure() once, right after
/// constructing the module and before Initialize(AppContext), so the module has its config in hand
/// for any cross-module wiring Initialize() does.
/// </summary>
public interface IConfigurableModule<TConfig>
{
    void Configure(TConfig config);
}
