# TODO — Backend (em aberto)

O histórico completo do que já foi entregue está em **`DONE.md`**. Tarefas do front vão para
`../reflow-oven-front/TODO.md`.

## Em aberto

- **#3 — Notificações faltantes.** "Execução abortada" → `kind: warning` já feito; faltam **falha da placa**
  (E-1xx) e **OTA disponível** — gerar a entrada no feed do sino com o `kind` correto.
- **#7 — RS422 / hardware real + `LinuxSystemController`.** Grande, futuro: protocolo STM32 (RS422) e o SO do
  OrangePi (hoje `SimulatedPowerBoard` / `SimulatedSystemController`).
- **#9 — Log de Operação: tela do front.** O backend está pronto e Master-only; falta o front montar o
  visualizador contra o contrato (`GET /api/operation-log`). *(time do front)*
- **Ops / Lucas.** `git push` da `develop`; segredos reais de produção (`Jwt`/`Master`/`Admin`/`Regular`) via
  ambiente; aplicar as migrations no deploy.
