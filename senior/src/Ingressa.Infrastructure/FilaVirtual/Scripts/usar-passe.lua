-- Consome o passe de compra (uso único). Só apaga se o valor conferir.
-- KEYS[1] = fila:{evento}:passe:{usuario}
-- ARGV[1] = passe apresentado
-- Retorno: 1 se o passe era válido e foi consumido; 0 caso contrário.
-- A vaga em "ativos" continua ocupada até o passe original vencer: se a reserva falhar,
-- devolver-passe.lua restaura o passe e o comprador tenta de novo sem voltar ao fim da fila.

if redis.call('GET', KEYS[1]) == ARGV[1] then
  redis.call('SET', KEYS[1] .. ':usado', ARGV[1], 'PX', redis.call('PTTL', KEYS[1]))
  redis.call('DEL', KEYS[1])
  return 1
end

return 0
