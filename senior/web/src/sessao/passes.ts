// Passes da fila virtual, guardados só em memória (somem ao recarregar a página;
// nesse caso a própria fila devolve o passe de novo enquanto ele valer).
const passes = new Map<number, string>()

export const guardarPasse = (eventoId: number, passe: string) => passes.set(eventoId, passe)
export const obterPasse = (eventoId: number) => passes.get(eventoId)
export const esquecerPasse = (eventoId: number) => passes.delete(eventoId)
