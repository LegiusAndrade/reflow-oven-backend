# TODO — Backend (em aberto)

O histórico completo do que já foi entregue está em **`DONE.md`**. Tarefas do front vão para
`../reflow-oven-front/TODO.md`.

## Em aberto

- **🔁 Instabilidade do `:5248` (factory reset) — só a queda não reproduzida segue em aberto.** Num teste o
  backend **caiu** (`:5248` → 000) ao acionar `POST /api/maintenance/factory-reset`. Já endereçado o que dava
  pra endereçar: o `FactoryResetAsync` **não reinicia** o serviço (sem reboot/restart no fluxo → o "responder
  antes de reiniciar" é **moot**, o controller devolve 204 normal) e o **candidato** — os ~14 `ExecuteDeleteAsync`
  rodando em sequência **sem transação** — foi **corrigido** (factory-reset/cleanup agora rodam numa transação;
  ver `DONE.md`). O fio solto que resta é só a **queda do processo em si**, **não reproduzida** desde então —
  reabrir se voltar a ocorrer.

- **#9 — Log de Operação: tela do front.** O backend está pronto e Master-only; falta o front montar o
  visualizador contra o contrato (`GET /api/operation-log`). *(time do front)*

- **Ops / Lucas.** `git push` da `develop` (rotineiro, feito a cada commit); segredos reais de
  produção (`Jwt`/`Master`/`Admin`/`Regular`) via ambiente; aplicar as migrations no deploy.
