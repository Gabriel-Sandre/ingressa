using Ingressa.Application.Abstracoes;
using Ingressa.Application.Auth;
using Ingressa.Domain.Comum;
using Microsoft.Extensions.Logging;

namespace Ingressa.Application.Admin;

public sealed class AdminService(IUsuarioRepositorio usuarios, IUnidadeDeTrabalho unidade, ILogger<AdminService> logger)
{
    public async Task<IReadOnlyList<UsuarioResponse>> ListarOrganizadoresPendentesAsync(CancellationToken ct) =>
        (await usuarios.ListarOrganizadoresPendentesAsync(ct)).Select(AuthService.ParaResponse).ToList();

    public async Task<UsuarioResponse> AprovarOrganizadorAsync(int adminId, int usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.ObterAsync(usuarioId, ct)
            ?? throw new NaoEncontradoException($"Usuário {usuarioId} não encontrado.");

        usuario.AprovarComoOrganizador();
        await unidade.SalvarAsync(ct);

        logger.LogInformation("Organizador {UsuarioId} aprovado pelo admin {AdminId}", usuarioId, adminId);
        return AuthService.ParaResponse(usuario);
    }
}
