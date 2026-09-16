namespace Ingressa.Application.Abstracoes;

public enum SituacaoNaFila
{
    /// <summary>O usuário não está na fila deste evento.</summary>
    Fora,

    /// <summary>Esperando a vez.</summary>
    Aguardando,

    /// <summary>Recebeu um passe e pode reservar.</summary>
    Liberado
}

public sealed record PosicaoNaFila(SituacaoNaFila Situacao, long Posicao, string? Passe);

public sealed record ResultadoDaAdmissao(int Liberados, long AindaNaFila);

/// <summary>
/// Sala de espera para eventos de alta demanda. Em vez de milhares de pessoas disputarem
/// o estoque ao mesmo tempo, elas entram numa fila por ordem de chegada e recebem,
/// aos poucos, um passe de uso único que permite reservar.
/// </summary>
public interface IFilaVirtual
{
    Task<PosicaoNaFila> EntrarAsync(int eventoId, int usuarioId, CancellationToken ct);

    Task<PosicaoNaFila> ConsultarAsync(int eventoId, int usuarioId, CancellationToken ct);

    /// <summary>Libera os próximos da fila até o limite de compradores simultâneos.</summary>
    Task<ResultadoDaAdmissao> AdmitirAsync(int eventoId, CancellationToken ct);

    /// <summary>Eventos que têm alguém na fila ou comprando.</summary>
    Task<IReadOnlyList<int>> ListarEventosComFilaAsync(CancellationToken ct);

    /// <summary>Tira o evento da lista de filas ativas quando ela esvazia.</summary>
    Task EncerrarSeVaziaAsync(int eventoId, CancellationToken ct);

    /// <summary>Consome o passe (uso único). Retorna falso se não for válido.</summary>
    Task<bool> UsarPasseAsync(int eventoId, int usuarioId, string passe, CancellationToken ct);

    /// <summary>Devolve um passe consumido quando a reserva falha, para o comprador tentar de novo.</summary>
    Task DevolverPasseAsync(int eventoId, int usuarioId, string passe, CancellationToken ct);

    /// <summary>Libera a vaga de comprador simultâneo depois de uma reserva concluída.</summary>
    Task ConcluirAsync(int eventoId, int usuarioId, CancellationToken ct);
}

/// <summary>Limite de requisições compartilhado por todas as instâncias da API.</summary>
public interface ILimitadorDistribuido
{
    Task<ResultadoDoLimite> VerificarAsync(string politica, string particao, int limite, TimeSpan janela, CancellationToken ct);
}

public sealed record ResultadoDoLimite(bool Permitido, long Contagem, TimeSpan TentarNovamenteEm);
