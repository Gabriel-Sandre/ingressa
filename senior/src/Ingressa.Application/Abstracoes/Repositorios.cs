using Ingressa.Domain.Eventos;
using Ingressa.Domain.Pedidos;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Application.Abstracoes;

public interface IUsuarioRepositorio
{
    Task<Usuario?> ObterAsync(int id, CancellationToken ct);
    Task<Usuario?> ObterPorEmailAsync(string emailNormalizado, CancellationToken ct);
    Task<bool> EmailExisteAsync(string emailNormalizado, CancellationToken ct);
    Task<IReadOnlyList<Usuario>> ListarOrganizadoresPendentesAsync(CancellationToken ct);
    void Adicionar(Usuario usuario);
}

public interface IRefreshTokenRepositorio
{
    Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct);
    Task RevogarFamiliaAsync(Guid familia, DateTime agora, CancellationToken ct);
    void Adicionar(RefreshToken token);
}

public interface IEventoRepositorio
{
    /// <summary>Carrega o evento com os setores, para alteração.</summary>
    Task<Evento?> ObterAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<Evento>> ListarDoOrganizadorAsync(int organizadorId, CancellationToken ct);
    void Adicionar(Evento evento);
    void Remover(Evento evento);
}

public interface IPedidoRepositorio
{
    /// <summary>Carrega o pedido com itens e ingressos, para alteração.</summary>
    Task<Pedido?> ObterAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<int>> ListarIdsDeReservasVencidasAsync(DateTime agora, int limite, CancellationToken ct);
    void Adicionar(Pedido pedido);
}

/// <summary>
/// Controle de lugares ocupados, feito direto no banco com operações atômicas.
/// </summary>
public interface IEstoque
{
    /// <summary>
    /// Ocupa <paramref name="quantidade"/> lugares somente se houver disponibilidade
    /// (um único UPDATE condicional). Retorna falso quando não há lugares suficientes.
    /// </summary>
    Task<bool> TentarOcuparAsync(int setorId, int quantidade, CancellationToken ct);

    /// <summary>Devolve lugares ao setor.</summary>
    Task LiberarAsync(int setorId, int quantidade, CancellationToken ct);
}

public interface IUnidadeDeTrabalho
{
    Task SalvarAsync(CancellationToken ct);

    /// <summary>
    /// Executa a operação em uma transação. Se algo falhar, nada é gravado —
    /// inclusive as alterações feitas por <see cref="IEstoque"/>.
    /// </summary>
    Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct);
}
