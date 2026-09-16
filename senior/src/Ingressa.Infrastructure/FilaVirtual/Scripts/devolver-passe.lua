-- Devolve um passe consumido quando a reserva não deu certo (ex.: setor esgotado),
-- com o tempo de validade que ainda restava.
-- KEYS[1] = fila:{evento}:passe:{usuario}
-- ARGV[1] = passe
-- Retorno: 1 se devolveu; 0 se o passe já tinha vencido.

local usado = KEYS[1] .. ':usado'
local ttl = redis.call('PTTL', usado)
if redis.call('GET', usado) == ARGV[1] and ttl > 0 then
  redis.call('SET', KEYS[1], ARGV[1], 'PX', ttl)
  redis.call('DEL', usado)
  return 1
end

return 0
