using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Usuarios;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ingressa.Application.Tests.Fakes;

public sealed class RelogioFixo : TimeProvider
{
    public static readonly DateTime Inicio = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private DateTimeOffset _agora = new(Inicio);

    public DateTime Agora => _agora.UtcDateTime;

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan tempo) => _agora = _agora.Add(tempo);
}

/// <summary>Hash reversível e barato — só para teste.</summary>
public sealed class SenhaHasherFalso : ISenhaHasher
{
    public string GerarHash(string senha) => "hash:" + senha;
    public bool Verificar(string senha, string hash) => hash == "hash:" + senha;
}

public sealed class GeradorDeAccessTokenFalso(TimeProvider relogio) : IGeradorDeAccessToken
{
    public (string Token, DateTime ExpiraEm) Gerar(Usuario usuario) =>
        ($"access-{usuario.Id}-{Guid.NewGuid():N}", relogio.GetUtcNow().UtcDateTime.AddMinutes(15));
}

public sealed class GeradorDeCodigosFalso : IGeradorDeCodigos
{
    private int _sequencia;
    public string GerarTokenSeguro(int bytes) => $"token-{Interlocked.Increment(ref _sequencia)}";
    public string Hash(string valor) => "sha:" + valor;
    public string GerarCodigoDeIngresso() => $"ING{Interlocked.Increment(ref _sequencia):D6}";
}

public sealed class GatewayFalso : IGatewayDePagamento
{
    public bool Aprovar { get; set; } = true;
    public Action? DuranteACobranca { get; set; }
    public List<string> Estornos { get; } = [];
    public int Cobrancas { get; private set; }

    public Task<ResultadoPagamento> CobrarAsync(int pedidoId, decimal valor, string tokenDePagamento, CancellationToken ct)
    {
        Cobrancas++;
        DuranteACobranca?.Invoke();
        return Task.FromResult(Aprovar
            ? new ResultadoPagamento(true, $"pay_{pedidoId}", null)
            : new ResultadoPagamento(false, null, "cartão recusado pelo emissor."));
    }

    public Task EstornarAsync(string codigoPagamento, CancellationToken ct)
    {
        Estornos.Add(codigoPagamento);
        return Task.CompletedTask;
    }
}

public sealed class EmailFalso : IEnviadorDeEmail
{
    public List<(string Para, string Assunto, string Corpo)> Enviados { get; } = [];

    public Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct)
    {
        Enviados.Add((para, assunto, corpoTexto));
        return Task.CompletedTask;
    }
}

public static class Log
{
    public static NullLogger<T> Nulo<T>() => NullLogger<T>.Instance;
}
