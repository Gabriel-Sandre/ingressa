using System.ComponentModel.DataAnnotations;
using Ingressa.Api.Models;

namespace Ingressa.Api.Dtos;

public sealed record RegistrarRequest(
    [Required(ErrorMessage = "Informe o nome."), StringLength(100, MinimumLength = 3, ErrorMessage = "O nome deve ter entre 3 e 100 caracteres.")] string Nome,
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido."), StringLength(200)] string Email,
    [Required(ErrorMessage = "Informe a senha."), StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter entre 8 e 128 caracteres.")] string Senha,
    PerfilUsuario Perfil = PerfilUsuario.Cliente);

public sealed record LoginRequest(
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] string Email,
    [Required(ErrorMessage = "Informe a senha.")] string Senha);

public sealed record UsuarioResponse(int Id, string Nome, string Email, PerfilUsuario Perfil);

public sealed record TokenResponse(string AccessToken, DateTime ExpiraEm, UsuarioResponse Usuario);
