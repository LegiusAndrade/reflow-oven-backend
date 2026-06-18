# TODO — Backend (em aberto)

O histórico completo do que já foi entregue está em **`DONE.md`**. Tarefas do front vão para
`../reflow-oven-front/TODO.md`.

## Em aberto

- **🔁 Instabilidade do `:5248` (factory reset) — parado a pedido (2026-06-17).** Num teste o backend **caiu**
  (`:5248` → 000) ao acionar `POST /api/maintenance/factory-reset`. Conferido em 2026-06-17: o
  `FactoryResetAsync` **não reinicia** o serviço (sem reboot/restart no fluxo) → o "responder antes de
  reiniciar" virou **moot**, o controller devolve 204 normal. O fio solto é a **queda do processo** (não
  reproduzida): os ~14 `ExecuteDeleteAsync` rodam em sequência sem transação — candidato a envolver numa
  transação e checar. Investigação adiada.

- **Autotune: tela do front.** O backend está pronto (`/api/autotune/*` — histórico paginado + start/cancel/
  apply/dismiss, CalibrationOnly pra disparar); falta a **tela dedicada** (disparar/cancelar com alvo, progresso
  ao vivo, histórico, aplicar-com-confirmação). Registrada em `../reflow-oven-front/TODO.md`. *(time do front)*
- **#9 — Log de Operação: tela do front.** O backend está pronto e Master-only; falta o front montar o
  visualizador contra o contrato (`GET /api/operation-log`). *(time do front)*

- **Ops / Lucas.** `git push` da `develop` (o `origin/develop` já está em dia, exceto este commit de docs); segredos reais de
  produção (`Jwt`/`Master`/`Admin`/`Regular`) via ambiente; aplicar as migrations no deploy.
