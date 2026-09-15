namespace Ingressa.Api.Erros;

/// <summary>
/// Base das exceções esperadas pela aplicação. Cada tipo vira um status HTTP
/// em <see cref="TratadorDeExcecoes"/>; os controllers não precisam de try/catch.
/// </summary>
public abstract class IngressaException(string mensagem) : Exception(mensagem);

/// <summary>404 — o recurso não existe ou o usuário não pode saber que ele existe.</summary>
public sealed class NaoEncontradoException(string mensagem) : IngressaException(mensagem);

/// <summary>409 — a operação conflita com o estado atual (e-mail em uso, setor esgotado...).</summary>
public sealed class ConflitoException(string mensagem) : IngressaException(mensagem);

/// <summary>422 — os dados são válidos em formato, mas violam uma regra de negócio.</summary>
public sealed class RegraDeNegocioException(string mensagem) : IngressaException(mensagem);

/// <summary>403 — o usuário está autenticado, mas o recurso pertence a outra pessoa.</summary>
public sealed class AcessoNegadoException(string mensagem) : IngressaException(mensagem);

/// <summary>401 — e-mail ou senha incorretos. A mensagem é genérica de propósito.</summary>
public sealed class CredenciaisInvalidasException() : IngressaException("E-mail ou senha inválidos.");
