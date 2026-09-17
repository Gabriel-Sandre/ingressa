"""Testes dos scripts Lua da fila virtual e do limitador contra um Redis de verdade.

Os scripts rodam dentro do Redis, então não dá para testá-los com dublês: este arquivo
os exercita como a aplicação faz (mesmas chaves e argumentos de FilaVirtualRedis.cs e
LimitadorRedis.cs), inclusive os cenários de concorrência que motivaram usar Lua.

    docker run -d --rm -p 6390:6379 --name redis-testes redis:7.4-alpine
    pip install redis
    python tests/lua/teste_scripts_lua.py --porta 6390
"""

import argparse
import sys
import threading
import time
from pathlib import Path

import redis

RAIZ = Path(__file__).resolve().parents[2] / "src" / "Ingressa.Infrastructure"
EVENTO = 7
P = f"fila:{{evento:{EVENTO}}}:"

falhas = 0


def verificar(condicao, mensagem):
    global falhas
    print(("OK      " if condicao else "FALHOU  ") + mensagem)
    if not condicao:
        falhas += 1


def main():
    argumentos = argparse.ArgumentParser()
    argumentos.add_argument("--host", default="localhost")
    argumentos.add_argument("--porta", type=int, default=6390)
    opcoes = argumentos.parse_args()

    r = redis.Redis(host=opcoes.host, port=opcoes.porta, decode_responses=True)
    r.flushall()

    fila = {
        nome: r.register_script((RAIZ / "FilaVirtual" / "Scripts" / f"{nome}.lua").read_text(encoding="utf-8"))
        for nome in ["entrar", "consultar", "admitir", "usar-passe", "devolver-passe", "concluir", "encerrar"]
    }
    janela_fixa = r.register_script((RAIZ / "Limites" / "Scripts" / "janela-fixa.lua").read_text(encoding="utf-8"))

    def entrar(u):
        return fila["entrar"](
            keys=[P + "espera", P + "seq", P + f"passe:{u}", "filas:ativas", P + f"passe:{u}:usado"],
            args=[u, EVENTO],
        )

    def consultar(u):
        return fila["consultar"](keys=[P + "espera", P + f"passe:{u}", P + f"passe:{u}:usado"], args=[u])

    def admitir(limite, validade_ms, agora_ms, passes=10):
        return fila["admitir"](
            keys=[P + "espera", P + "ativos"],
            args=[limite, validade_ms, agora_ms, P + "passe:"] + [f"tok{i}-{agora_ms}" for i in range(passes)],
        )

    def usar_passe(u, passe):
        return fila["usar-passe"](keys=[P + f"passe:{u}"], args=[passe])

    def devolver_passe(u, passe):
        return fila["devolver-passe"](keys=[P + f"passe:{u}"], args=[passe])

    def concluir(u):
        return fila["concluir"](keys=[P + "ativos", P + f"passe:{u}:usado"], args=[u])

    def encerrar(agora_ms):
        return fila["encerrar"](keys=[P + "espera", P + "ativos", "filas:ativas"], args=[agora_ms, EVENTO])

    # ---------- ordem de chegada ----------
    for u in [10, 11, 12, 13, 14]:
        entrar(u)
    verificar(entrar(12) == ["aguardando", "3"], "entrar de novo mantém a posição 3")
    verificar(consultar(99) == ["fora", "0"], "quem nunca entrou está fora da fila")
    verificar(r.smembers("filas:ativas") == {str(EVENTO)}, "o evento é registrado na lista de filas ativas")

    # ---------- admissão respeita o limite ----------
    agora = int(time.time() * 1000)
    verificar(admitir(2, 600_000, agora) == [2, 3], "libera 2 (o limite) e restam 3 esperando")
    verificar(consultar(10)[0] == "liberado" and consultar(11)[0] == "liberado", "os dois primeiros foram liberados")
    verificar(consultar(12) == ["aguardando", "1"], "o terceiro passa a ser o primeiro da fila")
    verificar(admitir(2, 600_000, agora) == [0, 3], "sem vagas livres, ninguém mais é liberado")

    # ---------- passe de uso único ----------
    passe = consultar(10)[1]
    verificar(usar_passe(10, "passe-errado") == 0, "passe errado é recusado")
    verificar(usar_passe(10, passe) == 1, "passe certo é aceito")
    verificar(usar_passe(10, passe) == 0, "o passe é de uso único")
    verificar(consultar(10) == ["comprando", "0"], "depois de usar o passe a situação é 'comprando'")
    verificar(entrar(10) == ["comprando", "0"], "recarregar a página não manda o comprador para o fim da fila")
    verificar(
        devolver_passe(10, passe) == 1 and consultar(10) == ["liberado", passe],
        "passe devolvido depois de uma reserva que falhou volta a valer",
    )

    # ---------- concluir libera a vaga ----------
    usar_passe(10, passe)
    concluir(10)
    verificar(admitir(2, 600_000, agora) == [1, 2], "reserva concluída libera uma vaga para o próximo")
    verificar(consultar(12)[0] == "liberado", "o próximo da fila recebe o passe")
    verificar(admitir(2, 600_000, agora + 600_001) == [2, 0], "passes vencidos devolvem as vagas")

    # ---------- encerramento da fila ----------
    r.delete(P + "espera", P + "seq", P + "ativos")
    r.sadd("filas:ativas", EVENTO)
    entrar(500)
    verificar(encerrar(agora) == 0 and r.sismember("filas:ativas", EVENTO), "fila com gente esperando continua ativa")
    r.delete(P + "espera")
    verificar(encerrar(agora) == 1 and not r.sismember("filas:ativas", EVENTO), "fila vazia sai da lista de ativas")

    # ---------- concorrência ----------
    r.delete(P + "espera", P + "seq", P + "ativos")
    threads = [threading.Thread(target=entrar, args=(1000 + i,)) for i in range(50)]
    [t.start() for t in threads]
    [t.join() for t in threads]
    posicoes = sorted(int(consultar(1000 + i)[1]) for i in range(50))
    verificar(posicoes == list(range(1, 51)), "50 entradas simultâneas recebem as posições 1..50, sem repetir")

    # list.append é atômico; somar num inteiro compartilhado perderia atualizações entre as threads.
    liberados = []

    def admitir_em_paralelo():
        liberados.append(admitir(20, 600_000, agora + 700_000, passes=20)[0])

    threads = [threading.Thread(target=admitir_em_paralelo) for _ in range(10)]
    [t.start() for t in threads]
    [t.join() for t in threads]
    verificar(
        sum(liberados) == 20 and r.zcard(P + "ativos") == 20,
        "10 Workers admitindo ao mesmo tempo respeitam o limite de 20 compradores",
    )

    # ---------- limitador distribuído ----------
    resultados = [janela_fixa(keys=["limite:teste:u1:1"], args=[3, 60_000]) for _ in range(5)]
    verificar(
        [x[0] for x in resultados] == [1, 1, 1, 0, 0] and resultados[4][1] == 5 and 0 < resultados[4][2] <= 60_000,
        "limite distribuído: 3 permitidas, as seguintes recusadas com o tempo restante",
    )

    r.flushall()
    print()
    if falhas:
        print(f"{falhas} verificação(ões) falharam")
        return 1
    print("Todos os cenários dos scripts Lua passaram.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
