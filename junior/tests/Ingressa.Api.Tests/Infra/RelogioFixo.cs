namespace Ingressa.Api.Tests.Infra;

/// <summary>Relógio controlado pelo teste: "agora" é sempre o mesmo instante.</summary>
public sealed class RelogioFixo(DateTimeOffset? agora = null) : TimeProvider
{
    public static readonly DateTime Agora = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private DateTimeOffset _agora = agora ?? new DateTimeOffset(Agora);

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan tempo) => _agora = _agora.Add(tempo);
}
