namespace Ingressa.Domain.Comum;

/// <summary>Base das exceções esperadas. A API converte cada tipo em um status HTTP.</summary>
public abstract class IngressaException(string mensagem) : Exception(mensagem);

/// <summary>404 — o recurso não existe ou o usuário não pode saber que ele existe.</summary>
public sealed class NaoEncontradoException(string mensagem) : IngressaException(mensagem);

/// <summary>409 — a operação conflita com o estado atual.</summary>
public sealed class ConflitoException(string mensagem) : IngressaException(mensagem);

/// <summary>422 — a operação viola uma regra de negócio.</summary>
public sealed class RegraDeNegocioException(string mensagem) : IngressaException(mensagem);

/// <summary>403 — autenticado, mas sem permissão para este recurso.</summary>
public sealed class AcessoNegadoException(string mensagem) : IngressaException(mensagem);

/// <summary>401 — credenciais ou sessão inválidas. A mensagem é genérica de propósito.</summary>
public sealed class NaoAutenticadoException(string mensagem = "E-mail ou senha inválidos.") : IngressaException(mensagem);
