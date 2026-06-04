# TODO — bugs e melhorias

## ⏭️ Próximas tarefas (backend) — para a próxima sessão (registrado 2026-06-01)

Levantadas ao fim da sessão de 2026-06-01. A #1 original (**Comparativo do Perfil em runs ao vivo**) foi
**concluída em 2026-06-03** — ver Resolvidos.

> **✅ Status (fim da sessão 2026-06-04):** #1, #2, #3, #4, #5, #6, #8, #9 **FEITOS e verificados ao vivo**
> — seed limpo em prod (operadores demo-only) · hub de notificações em tempo real `/hubs/notifications` ·
> "Execução abortada" → `warning` · limpeza real confirmada · categorias `programas`/`usuarios`/
> `programas-deletados` no overview com bytes reais · kernel real (uname -r) no overview · 2 casas decimais ·
> Log de Operação completo. **Resta só #7** (RS422 / hardware real — futuro, precisa do STM32/OrangePi).

1. **Seed limpo em produção** — *segurança, escopo pequeno.* `operador1`/`operador2` (demo, senha
   `reflow1234`) ainda semeiam em **produção** (o seed de usuários roda no `SeedAsync`, não só no demo).
   Gatear os usuários/dados demo atrás de `Seed:Demo` (dev-only) → prod só ganha Admin/Regular/Master da
   config. Opcional: fail-fast se `Admin__Password`/`Regular__Password` ficarem no default fora de Development
   (igual ao Master/Jwt em `Program.cs`).

2. **Notificações em tempo real (SignalR)** — o feed do sininho é polling; adicionar um hub de push
   espelhando o `/hubs/systemlog` (ver `ISystemLogSink`/`SignalRSystemLogSink`/`SystemLogHub`). Escopo médio.

3. **Eventos → notificações + `kind`** — definir/implementar quais eventos geram entrada no feed e o `kind`
   (`info`/`error`/`warning`/`update`): execução **abortada** → warning, **falha da placa**, **OTA** disponível.
   Escopo pequeno. **Status (verificado 2026-06-03):** a notificação de **"Execução abortada" já é gerada**,
   mas vem com `kind: "error"` (vermelho) — deveria ser **`warning`** (o front já renderiza warning em âmbar).
   Faltam ainda as notificações de **falha da placa** e **OTA**.

4. **Verificar "Limpeza mock-stage"** — a nota de 2026-06-01 (abaixo) diz que a Limpeza da Manutenção é mock,
   mas o `MaintenanceService.CleanupAsync` já usa `ExecuteDelete` (deleção real). Confirmar e fechar a nota.

5. **Limpeza: categorias "Programas", "Usuários ativos" e "Programas deletados"** — *pedido do Lucas
   (2026-06-03; reforçado 2026-06-04).* O front já tem as categorias prontas na Limpeza (Diagnóstico →
   Manutenção); elas aparecem **"vazio"** (desabilitadas) até o backend mandar a contagem. **⚠️ Re-confirmado
   2026-06-04: o overview ainda retorna só `execucoes/falhas/logs/inativos`** — `programas`, `usuarios` e
   `programas_deletados` **não vêm**, então o Master não vê o tamanho deles (o Lucas reportou de novo). Faltam:
   - `GET /api/maintenance/overview` → incluir em `database.categories` os ids **`programas`** (nº de programas
     salvos + bytes), **`usuarios`** (nº de usuários **ativos, exceto o autenticado** + bytes) e
     **`programas_deletados`** (nº de programas **soft-deleted / na lixeira** + bytes).
   - `POST /api/maintenance/cleanup` → tratar **`programas`** (apaga todos os programas salvos — decidir se
     mantém 1 default, igual ao factory-reset), **`usuarios`** (apaga os usuários **ativos exceto quem
     chamou**) e **`programas_deletados`** (expurga de vez os programas já soft-deleted, igual ao "purge" da
     Lixeira #8). A regra "exceto o logado" **tem de ser server-side** (pelo JWT) — o front manda só o id
     `usuarios` e nunca decide quem poupar; ele apenas espelha a remoção no cache local.
   - **Permissão (decidido com o Lucas, 2026-06-04):** limpar `programas` e `usuarios` é **só do Admin** — o
     **Master NÃO pode** (exceção deliberada ao "Master ⊇ Admin"; em todo o resto o Master é superusuário).
     O Master mantém visão **read-only do tamanho**, então o overview deve devolver as contagens dessas duas
     **também para o Master** (ele vê, mas não limpa). O front já trava no cliente (cadeado + "Somente Admin",
     não-selecionável) e nunca envia esses ids quando o autor é Master, mas o `POST /api/maintenance/cleanup`
     **tem de rejeitar** `programas`/`usuarios` vindos de um Master (403/ignorar) — gating de front não é
     segurança. Histórico (`execucoes`/`falhas`/`logs`/`inativos`) segue limpável por Admin **e** Master.
     **`programas_deletados` (a confirmar):** é a lixeira (a Lixeira #8 é Master-only), então faz sentido ser
     **Master-allowed**; no front deixei limpável por **ambos** por ora — confirmar se vira Master-only.

6. **`osKernel` do overview vem como RID, não kernel** — *cosmético, escopo mínimo.*
   `GET /api/maintenance/overview` devolve `osKernel: "ubuntu.24.04-x64"` (um Runtime Identifier do .NET). O
   front mostra isso em **"Versão do Linux"** (Diagnóstico → Manutenção → Sistema). Devolver a versão real do
   kernel (estilo `uname -r`, ex. `6.8.0-31-generic`) para o rótulo fazer sentido.

7. **RS422 / hardware real + `LinuxSystemController`** — *grande, futuro.* Protocolo STM32 (RS422) e o SO do
   OrangePi (hoje `SimulatedPowerBoard` / `SimulatedSystemController`).

8. **Precisão dos valores de programa (≤ 2 casas decimais)** — *pedido do Lucas (2026-06-04), feito no front.*
   O front passou a arredondar `temp`/`tempo` dos segmentos/pontos para **no máx. 2 casas** antes de
   `POST/PUT /api/programs` (define único `PROGRAM_VALUE_MAX_DECIMALS` em `lib/limits.ts`, aplicado na
   fronteira `programStore.saveProgram`). Para defesa em profundidade, o backend deveria **validar/arredondar
   igual** ao persistir um programa (e ao derivar a curva amostrada). **PID, calibração e demais configs ficam
   de fora** — precisam de mais precisão.

9. **Auditoria completa — "Log de Operação" (trilha genérica)** — ✅ **FEITO no backend (2026-06-04)** — falta a tela do front. *pedido do Lucas (2026-06-04); referência:
   print accelero "LOG DE OPERAÇÃO" (em `~/Pictures/2026-06-04_11-00.png`). Decidido com ele: **especificar o
   backend primeiro**; o front (visualizador) vem depois, contra o contrato abaixo.* Visão: "**tudo fica
   registrado no BD**" — toda alteração, execução, erro, comunicação, login, calibração numa **única trilha de
   auditoria, campo a campo**.

   **Modelo (tabela única, append-only):**
   `OperationLog { id, at (UTC), operatorId?, operatorName, category, type, object, objectId?, data (jsonb) }`
   - `operatorName` = usuário; **"Sistema"** para eventos automáticos (comunicação, OTA, watchdog), com
     `operatorId` nulo.
   - `category` (a separação por **aba/filtro** do front — ver "Apresentação"): `Execucao | Alteracao | Usuario
     | Erro | Comunicacao | Falha` (extensível: `Calibracao | Manutencao`). É a dimensão **grossa**; o `type`
     abaixo é o detalhe fino dentro dela.
     - **Erro × Falha (decidido com o Lucas 2026-06-04):** `Falha` = **faltas de hardware da placa** (E-1xx:
       sobretemperatura, sobrecorrente, termopar, perda RS422…); `Erro` = **erros de software/operação**.
       → **mover a falha-de-placa de `Erro` p/ `Falha`** no `AuditService.CategoryFor` (a "1 linha" que o
       backend ofereceu). A perda de comunicação na partida segue em `Comunicacao`, como já está.
   - `type` (TIPO; enum acento-free no wire): `Criacao | Alteracao | Remocao | Execucao | Erro | Comunicacao |
     Login | Logout | Calibracao | Limpeza | ResetFabrica`.
   - `object` (OBJETO): `Programa | Execucao | Falha | Usuario | Configuracao | Controlador | Sessao | …`.
   - `objectId` (OBJETO ID): identificador do alvo (id do programa, nº da execução, username…).
   - `data` (DADOS): **lista de campos** `[{ field, before?, after }]` — `before` ausente em eventos sem estado
     anterior (execução, comunicação, login). Valores em JSON, escalar ou objeto, igual ao print
     (`ocoMetadata → {"description":{…},"meta":{…}}`). **Nunca logar o valor de senha** — só "senha alterada".

   **Cobertura (o que precisa gravar):**
   - **Programa/Configuração** (Criação/Alteração/Remoção). *Já existe no ChangeLog (Relatórios → Alterações,
     com diff antes/depois)* → **unificar**: o ChangeLog vira um subconjunto do OperationLog
     (`type ∈ {Criacao,Alteracao,Remocao}`, `object ∈ {Programa,Configuracao}`) **sem regredir** o diff pronto.
   - **Execução** (iniciada/concluída/abortada) e **Erro/Falha** da placa — podem ser entradas-resumo que
     **referenciam** o registro detalhado existente (Execuções/Erros) via `objectId`.
   - **Comunicação RS422 (NOVO)** — link sobe/cai, perda/retomada de comm com o STM32, "última comunicação"
     (igual ao `conLastOnline` do print). Hoje só aparece como status na TopBar; **não é auditado**.
   - **Usuário** (criar/editar/remover/ativar, troca de senha, reset), **Sessão** (login/logout e falhas),
     **Calibração** (offsets/ganhos/PWM — Master), **Manutenção** (Limpeza: quem limpou quais categorias; Reset
     de fábrica).

   **Endpoint:** `GET /api/operation-log` paginado + filtros espelhando `ReportQuery` (page/pageSize, from/to,
   search) **+** `category`, `operator`, `type`, `object`, `objectId`. (Futuro: push via SignalR p/ ao vivo.)

   **Retenção & limpeza:** a auditoria deve ser **protegida da Limpeza** (como o "Registro de alterações" já é)
   — política de retenção própria, server-side, nunca apagável pelo operador.

   **Permissão (decidido com o Lucas 2026-06-04): Master-only** — igual à aba Diagnóstico → Log atual que ele
   substitui (não `canAdminister`). O endpoint `GET /api/operation-log` deve exigir **Master** (403 p/
   Admin/Regular); o front reusa o mesmo `isMaster`/sub-tab Master.

   **Apresentação no front (refinado com o Lucas 2026-06-04):** o Log de Operação **substitui a aba
   Diagnóstico → Log** atual (mensagens de sistema free-text — ele achou "feia"). Separação **por categoria**
   (filtro/sub-abas): **Tudo** (histórico corrente, sem filtro) · **Execução** · **Alteração** · **Usuário** ·
   **Erro** · **Comunicação** · **Falha** — cada uma = `?category=…`. **Paginação é obrigatória** (volume alto):
   o endpoint já pagina e o front reusa o `Pagination` de Relatórios. O log de sistema antigo (INFO/Aviso/Erro
   free-text) é substituído — mensagens de sistema podem cair na categoria `Erro`/`Sistema`. Segue
   **Master-only** (decidido 2026-06-04 — ver Permissão).

   **Contrato p/ o front (a confirmar antes de montar a tela):**
   `OperationLogEntryDto { id; at; operatorName; operatorId?; category; type; object; objectId?; data: {field; before?; after}[] }`
   \+ `PagedResult<OperationLogEntryDto>`. A tela espelha o print — **DATA · OPERADOR · TIPO · OBJETO · OBJETO
   ID · DADOS** (campo em pill + valor JSON) — com os mesmos filtros/paginação de Relatórios (layout 1024×600).

   **✅ FEITO (backend) 2026-06-04 — falta só a tela do front.**
   - Tabela `OperationLog` (entidade + `OperationField` jsonb; enums `OperationCategory`/`OperationType`/
     `OperationObject`, PascalCase acento-free; índices At/Category/Type/Object). Migration `AddOperationLog` aplicada.
   - `AuditService.Record(...)` = chokepoint único; **deriva `category` de (type,object)** (zero churn nos call
     sites); operador default = usuário autenticado, "Sistema" p/ automáticos; **nunca grava senha**.
     `RecordProgram/ConfigChange` escrevem o ChangeLog (diff antes/depois **intacto**) **e** o OperationLog.
   - Seams: Auth (login/técnico/falha/logout/troca de senha), User (CRUD + restaurar/expurgar), Calibração,
     Manutenção (limpeza: quem limpou o quê / reset), SystemMonitor (servidor central + OTA = operador "Sistema"),
     RunManager (execução iniciada/finalizada com status/duração/pico; falha→Erro com código/motivo; falha ao
     iniciar na placa→Comunicação).
   - `GET /api/operation-log` **Master-only** (403 Admin/Regular; auditoria protegida da Limpeza), filtros
     search/from/to/before/page/pageSize **+** category/operator/type/object/objectId. 0 warnings, **50/50 testes**.
   - **Contrato entregue = o acima**, com `category` antes de `type`; `before`/`after` são **strings** (um objeto
     vem como JSON em string). O front já pode montar a tela contra isso.
   - Follow-ups (não-bloqueantes): retenção/poda server-side; comunicação RS422 real do STM32 (o seam
     Comunicação/Controlador existe, só não é exercido pelo `SimulatedPowerBoard`); demo-seed de linhas variadas.

10. **Programa recém-criado não aparece em 1º na ordenação "Padrão"** — *bug reportado pelo Lucas (2026-06-04).*
    Em `ProgramService.ListAsync`, o sort default (`_ =>`) é `OrderBy(IsSeed).ThenByDescending(LastUsed ?? MinValue)
    .ThenBy(Name)`. Um programa **recém-criado tem `LastUsed = null`** → cai no grupo "nunca usado", ordenado por
    nome — então **nunca vem em primeiro** (o esperado pelo usuário). `Id` é `Guid.NewGuid()` (aleatório) e a
    entidade **não tem `CreatedAt`**, então não há como ordenar por criação hoje. **Fix:**
    - Adicionar **`CreatedAt` (DateTimeOffset)** em `ReflowProgram`; setar em `CreateAsync` (`DateTimeOffset.UtcNow`).
    - **Migration** p/ a coluna (backfill das linhas existentes — ex. `LastUsed` ou uma data fixa; elas então
      desempatam por nome).
    - Sort default → `OrderBy(IsSeed).ThenByDescending(CreatedAt).ThenBy(Name)` (mais novo **primeiro**; "Usado
      recentemente" continua cobrindo `LastUsed`). Assim o recém-criado abre em 1º na galeria.

**Front (time do front):** tela **"Lixeira do Master"** (ver/restaurar/expurgar via `…/deleted` · `…/restore`
· `…/purge`, MasterOnly) + remover a categoria "Alterações" da Limpeza — **backend já pronto**.

**Ops / Lucas:** `git push` da `develop` (commits locais acumulados); senha de app do Gmail no `.env`;
segredos reais de prod (Jwt/Master/Admin/Regular) + aplicar migrations no deploy.

---

## ✅ Resolvidos

### ⭐ Comparativo do Perfil em runs ao vivo (2026-06-03)
A tabela "Comparativo do Perfil" do relatório ficava **vazia em toda execução real** — `RunManager.FinalizeAsync`
montava `Trace`/`Points`/`Events` mas nunca preenchia `Comparison` (só os relatórios demo-seed tinham linhas).

**Feito:**
- `ProfileBuilder.StageBoundaries(segments)` — deriva os **estágios lógicos** (um por leg editável; colapsa os
  12 sub-pontos da parábola no endpoint; `Fixo` segura a temperatura anterior).
- `ProfileBuilder.BuildComparison(stages, samples)` — uma linha por estágio: **alvo programado × temp. medida
  da grelha** no fim do estágio (interpolada das amostras) e **tempo programado × tempo real decorrido**. Um run
  interrompido (abort/falha) **trunca** o estágio em que parou e **descarta** os que nunca começaram. O front
  deriva a coluna "Desvio" daí (`tempReal − tempProg`, `timeReal − timeProg`).
- `RunManager`: captura os estágios no `StartAsync` (de `Segments`, ou dos vértices do `Profile` para programas
  de pontos como o catálogo seed) e preenche `Comparison = ProfileBuilder.BuildComparison(...)` no finalize.
- Testes: 4 novos em `ProfileBuilderTests` (collapse de parábola, run limpo com desvio-zero de tempo, run abortado
  truncado, sem amostras → vazio). Suíte: **45/45**.

### Correções de relatórios: gráfico de falha (demo) + curva de "Alterações" (2026-06-03)
Dois problemas achados ao revisar a tela de Relatórios:

**(1) Execuções de falha sem gráfico — dados de demo, não proposital.** `DbSeeder.Demo.BuildExecution`
preenchia `Points`/`Comparison`/`Events` mas **não** o `Trace` (snapshot multi-sinal) — só a aba Erros
(`BuildError`) e a única `BuildLinkedFailureExecution` tinham. Execuções **reais** já recebem o `Trace` do
`RunManager`; era lacuna só do gerador demo. **Feito:** `BuildExecution` agora gera `Trace` (curva limpa em
run OK, assinatura de falha em run com erro — via novo parâmetro `faulted` em `BuildSnapshotSeries`).
⚠️ **Requer re-seed** (limpar o BD) para os relatórios demo já existentes ganharem o gráfico.

**(2) Curva de "Alterações" não batia com o programa — bug de lógica.** No caminho de **segmentos** (o que o
editor gera), o snapshot do ChangeLog reconstruía a curva ignorando o **baseline (0,0)**, achatando
**parábolas** em retas e plotando o **`Temp` bruto do `Fixo`** (que a curva real ignora). **Feito:**
`ReportService` agora reconstrói a curva real via `ProfileBuilder.ToProfile` a partir dos vértices
snapshotados (`Temp`/`TimeSec`/`Ramp`, já persistidos) — sem migration. Programas de pontos passam sem
mudança (legs lineares). +1 teste novo + 1 ajustado; suíte 46/46.

### 1. Biblioteca de logger — "todo log possível no console"
Quero todo log possível no console: quando o usuário logou, saiu, fez modificação, erro no BD, tudo.

**Feito:** adicionado **Serilog** (console) no `Program.cs` (`UseSerilog` + `UseSerilogRequestLogging`
= uma linha por requisição HTTP). Eventos logados nos serviços:
- **Login** OK / falha (com motivo: usuário não encontrado, inativo, senha incorreta) / técnico — `AuthService`.
- **Logout** — novo endpoint `POST /api/auth/logout` (o front chama ao sair). `AuthController`.
- **Usuários** criados/alterados/removidos (com ator) — `UserService`.
- **Programas** e **Configuração** criados/editados/removidos (com ator) — `AuditService`.
- **Execução** iniciada/finalizada/erro na placa — `RunManager`.
- **Erros de BD**: EF Core com SQL + erros detalhados em Development; exceções não tratadas viram `Error`.
- **Recuperação de senha** solicitada — `AuthService`.

> Obs.: o console é separado da tabela `SystemLog` (tela *Log do Sistema*), que hoje só tem dados de
> demo. Se quiser que essa tela mostre os eventos reais também, é um passo a mais — só avisar.

### 2. RPM sem casa decimal
**Feito:** RPM (forno e dissipador) agora é `int` em todo o fluxo (`SensorReadings`, `TraceSample` e DTOs).

### 3. `ValidationAppException` / "Temperatura máxima fora da faixa" parando no debugger — "deve ter um try"
**Esclarecido + melhorado:** **já era tratado** — o `ExceptionMiddleware` captura e devolve `400`/`409`
(ProblemDetails) limpo ao front. O "break" no VSCode é só o aviso de *first-chance exception* (normal).
Agora essas exceções são logadas como **`WRN`** (visível, sem cara de crash). Para o debugger não parar
nelas: aba **Run and Debug → BREAKPOINTS** → desmarcar "All Exceptions"/"User-Unhandled". (Documentado no GUIA.)

### 4. "Já existe uma execução em andamento" ao iniciar no front (execução fantasma)
**Feito:** se um tick do loop de controle falhar com execução ativa, ela é **abortada** (não fica travada
como *Running* para sempre); `try/catch` + log ao iniciar o programa na placa. O `POST /api/runs/stop`
já recupera uma execução presa. (`RunControlLoopService`, `RunManager`.)

### 5. Limitar a quantidade de dados pedida ao BD ("se pedir 1k de relatório...")
**Já protegido + explícito:** o servidor **trava** o `pageSize` — relatórios/logs no máx. **200** linhas,
programas no máx. **100**, mesmo que o cliente peça mais. Extraído para `DomainConstants`
(`ReportPageSizeMax` / `ProgramPageSizeMax`). Usuários e catálogo de falhas são pequenos (admin/fixos).

### 6. Trocar Swagger por Scalar
**Feito:** removido o Swashbuckle; OpenAPI agora via `Microsoft.AspNetCore.OpenApi` (`/openapi/v1.json`,
com esquema Bearer JWT) e UI interativa **Scalar** em **`/scalar`** (dev). Docs atualizadas.

### 7. Serviço de e-mail (onboarding + lembrete de troca de senha)
**Feito:** ao criar um usuário, o sistema **gera a senha** e a envia por e-mail pedindo a troca em até
`PasswordChangeWithinDays` (7) dias; passado o prazo, cada login **reenvia** o lembrete (no máx. 1×/dia) e
ainda deixa entrar. `IEmailSender` com `SmtpEmailSender` (MailKit/Gmail) ou `StubEmailSender` por `Email:Mode`;
`PasswordGenerator` (RNG), claim `must_change_password`, `POST /api/auth/change-password`.

### 8. Serviço de sistema do OrangePi + feed de notificações
**Feito:** `ISystemController` (Simulated padrão / Linux por `System:Mode`) em `/api/system/*` (status,
métricas CPU/mem/disco, rede/Wi-Fi/IP, relógio/NTP, atualização OTA, conectividade do servidor central,
reboot/shutdown — leituras autenticadas, **mutações `AdminOnly`**). A rede agora **aplica no SO** (best-effort)
ao salvar Configurações. Feed de notificações (`/api/notifications`) alimentado pela execução e pelo
`SystemMonitorService`. Comandos do SO via `ProcessRunner`/`ArgumentList` (sem injeção). Tamanho **real** do
banco (`pg_database_size`).

---

## Pendentes — levantados pela integração do front-end (2026-05-30)

> Itens identificados ao migrar o front-end para o backend real. Reescritos no formato de checklist.
>
> **Legenda:** ⛔ falta implementar no backend · 🟡 parcial/opcional · ✅ pronto.
> A coluna `feBlocked` indica se o item **bloqueia** o front (o front não consegue a parte dele sem isso).
>
> **Resumo:** ✅ **A–H todos implementados** nesta rodada (A prefs por usuário, B ping-porta/interfaces/prioridade,
> C trace multi-sinal por execução, D alerta de disco, E logging HTTP com payload, F filtros server-side dos
> relatórios, G tamanho por categoria, H diff por ponto). Detalhe de cada um abaixo (com "Feito:").

### ✅ Já prontos no backend (o front só precisa consumir)
Itens que o usuário achava que "faltavam" mas **já estão implementados** (não entram na lista abaixo):
- **Wi-Fi × cabo** e **IP/rede** — `GET/PUT /api/system/network`, `GET /api/system/wifi`.
- **Servidor central (globo)** — `GET /api/system/connectivity` + `status.centralServerOnline`.
- **Atualização (OTA)** — `GET/POST /api/system/update`.
- **Sino/Notificações** — feed real em `/api/notifications` (alimentado por execução + monitor).
- **Tamanho real do banco/HD** — `pg_database_size` no `MaintenanceService`; HD via `DeviceService`.
- **Paginação no servidor** — `page/pageSize` com clamp (200/200/100); falta só o front paginar por tela.

### ✅ A. Preferências do usuário (tema + séries do gráfico) por usuário — **Feito**
**Feito:** owned jsonb `User.Preferences` (enum `Theme` light/dark/system + 7 flags espelhando `RunSeriesPreference`);
`GET/PUT /api/me/preferences` (escopado pelo JWT, bloqueia técnico); embutido no login e em `/api/auth/me` para
hidratar o tema no boot; migration `AddUserPreferences` com backfill dos defaults corretos.
Hoje o tema só vive na sessão do front e as séries do gráfico de execução são uma **config global**
(`Settings.RunSeries`), não por usuário. O usuário quer que o tema e as legendas/séries **sigam o
usuário** entre PCs/logins.
- Adicionar preferências por usuário: `Theme` (`light|dark|system`) e `ChartSeries` (espelhar
  `RunSeriesPreference`, ~7 flags) — em `User` ou numa tabela `user_preferences(user_id FK unique,
  theme, chart_series jsonb, updated_at)`.
- Endpoints escopados pelo JWT: `GET /api/me/preferences` e `PUT /api/me/preferences`.
- Incluir as preferências no payload de `GET /api/auth/me` e no `LoginResult` (para hidratar o tema
  já no boot, sem flash).
- Migration EF Core nova.

### ✅ B. Rede: ping com **porta**, listar interfaces e escolher a **prioritária** — **Feito**
**Feito:** `PingRequest.Port` opcional → probe TCP (latência do handshake) quando há porta, senão ICMP;
`GET /api/system/interfaces` (`ListInterfacesAsync`, enum `InterfaceKind` Ethernet/WiFi); `POST /api/system/interfaces/priority`
(`AdminOnly`, nmcli `autoconnect-priority`). Simulado + Linux implementados.
A aba Rede do front precisa de três coisas que o backend ainda não expõe (confirmado lendo as rotas de
`SystemController` — existem hoje: `GET/PUT /api/system/network`, `GET /api/system/wifi`,
`POST /api/system/wifi/connect`, `GET /api/system/connectivity`, time/ntp, update, reboot/shutdown):
- **Ping com porta:** hoje `PingRequest(string Host)` (`DiagnosticsDtos.cs`) só tem host e
  `DiagnosticsService.PingAsync(host)` faz só ICMP. Estender para `{ Host, int? Port }` e, quando vier
  `Port`, fazer **TCP connect** (mede a latência do handshake); sem porta, manter o ICMP atual.
- **Listar interfaces:** não há endpoint que liste as interfaces (cabo/wifi, up/down, IP). Adicionar
  algo como `GET /api/system/interfaces` → `[{ Name, Kind, Up, IpAddress }]`.
- **Interface prioritária:** não há endpoint para escolher a rede prioritária. Adicionar
  `POST /api/system/priority-interface` (ou um campo em `PUT /api/system/network`).
- Config da rede cabeada (IP fixo etc.) já dá para fazer via `PUT /api/system/network`.
> Obs.: o **medium** (wifi vs cabo) para o ícone da TopBar e o status do **servidor central**
> (`GET /api/system/connectivity`) para o ícone do globo **já existem** — falta só o front consumir.

### ✅ C. Trace **multi-sinal** persistido por execução — **Feito**
**Feito:** `ExecutionReport.Trace` (mesmo `FailureSnapshot` do erro, jsonb); o `RunManager` acumula as amostras
por tick e, ao finalizar, faz downsample (`SnapshotSamples`) em 7 séries (alvo/forno/dissipador/corrente/tensão/2 fans)
reusando a paleta do snapshot de erro; exposto em `ExecutionDetailDto.Trace`; migration `AddExecutionTrace`.
O relatório de execução só guarda **temperatura** (`ExecutionReport.Points` = T/Temp/Kind). O usuário
quer o gráfico do relatório com **corrente, tensão, RPM, temperatura do dissipador, etc.** ao longo do
tempo. O trace ao vivo (`TraceSampleDto`: alvo/forno/placa/corrente/tensão/fans) trafega no
`/hubs/telemetry` mas **não é gravado**.
- Reaproveitar **exatamente** o padrão que já existe no relatório de **erro**:
  `ErrorLogEntry.Snapshot` = `FailureSnapshot { DurationSec, List<SnapshotSeries{Name,Unit,Color,Values[]}> }`.
- Capturar os ~7 sinais durante a run (`RunManager`/`RunControlLoopService`) com **downsample/limite**
  de pontos, gravar em `ExecutionReport.Snapshot` e expor em `ExecutionDetailDto`
  (`GET /api/executions/{id}` ou `GET /api/executions/{id}/trace`).
- Migration EF Core nova.

### ✅ D. Alerta de **pouco espaço em disco**: e-mail + aviso na tela — **Feito**
**Feito:** `DomainConstants.DiskLowFreePercent` (10%); o `SystemMonitorService` checa o disco a cada poll com
detecção de **cruzamento** do limiar (sem spam, re-arma ao recuperar) e dispara notificação no sino +
`IEmailSender.SendDiskLowAsync` aos admins ativos (best-effort).
Há base mas falta tudo que junta: o disco real já está disponível (`DeviceService` /
`SystemMetrics.DiskFreeGB`/`DiskTotalGB`), e o e-mail (`SmtpEmailSender`/MailKit) + o **feed de
notificações** já funcionam. **Falta criar** o limiar (ex.: `DomainConstants.DiskLowFreePercent`, hoje
**inexistente**) e a lógica que dispara o alerta.
- Num `BackgroundService` (ex.: `SystemMonitorService`, que já monitora central/OTA), checar o disco
  contra `DiskLowFreePercent` com **estado/throttle** (não spammar) e, ao cruzar o limiar:
  - `NotificationService.RaiseAsync(...)` → aviso na tela (sino) — **já pronto**;
  - enviar e-mail via `IEmailSender` (adicionar algo como `SendDiskLowAsync`).
- Em produção, configurar `Email:Mode=Smtp` + credenciais (resolve também a **recuperação de senha
  real**, hoje em modo stub).
- Opcional: tornar o limiar configurável em `Settings`.

### ✅ E. Logging de **GET/SET com payload** (auditoria server-side) — **Feito**
**Feito:** `AddHttpLogging` (request/response com método/path/query/status/duração + corpo, limite 4 KB) ligado
em **dev**; `AuthRedactionInterceptor` descarta o corpo de `/api/auth/*` (sem vazar senha/token — verificado);
override do Serilog para a categoria `HttpLogging` em dev. (A auditoria de mutação `ChangeLogEntry` já existia.)
Já existe auditoria de mutação (`AuditService` grava `ChangeLogEntry` + log) e `ExceptionMiddleware`
(só erros). **Não há** logging de request/response (grep por `AddHttpLogging`/`UseHttpLogging` = nada);
GETs não são logados e o UPDATE só registra o *diff*, não o payload recebido.
- Adicionar `AddHttpLogging`/middleware (ou action filter) logando request/response das rotas
  relevantes **com redação** de senha/JWT.
- Estender a auditoria de UPDATE para gravar o payload **before/after** completo (reaproveitar
  `AuditService`/`ChangeLogEntry`). Definir **retenção** com limite.

### ✅ F. Filtros server-side dos Relatórios — **Feito**
**Feito:** `ReportQuery` ganhou `Status/Action/Severity/Level` (literais pt-BR, parseados por `EnumWire.TryFromWire`,
inválido ignorado), aplicados **antes** de `CountAsync`/`Skip`/`Take` em cada relatório; `To` tratado como **fim do dia**.
O `EnumWire` foi movido para `Domain.Common` (compartilhado entre as camadas).
Para mover a tela **Relatórios** para paginação no servidor sem quebrar os filtros, o `ReportQuery`
(`Search/From/To/Page/PageSize`) precisa ganhar o **filtro por categoria** de cada aba — hoje esse
dropdown ("Filtrar por…") só funciona no cliente, sobre a página já carregada.
- `GET /api/executions` → `?status=` (Concluído/Falha).
- `GET /api/changes` → `?action=` (Criado/Editado/Removido).
- `GET /api/errors` → `?severity=` (Crítico/Alerta/Aviso).
- `GET /api/system-log` → `?level=` (Info/Aviso/Erro) — hoje o `SystemLogModal` filtra nível no cliente.
- Aplicar o `.Where()` correspondente **antes** do `CountAsync`/`Skip`/`Take` no `ReportService`.
- (Opcional) `?sort=` nos relatórios (hoje a ordem é fixa, mais recente primeiro) e tratar `to` como
  **fim do dia** (hoje `At <= To` com `yyyy-mm-dd` vira meia-noite e exclui o dia).
> Sem isso, a paginação no servidor dos relatórios filtraria só a página atual (errado). A paginação
> de **Programas** não depende disso — já tem `search/filter/sort/page/pageSize` no backend.

### ✅ G. Tamanho do banco **por categoria** exato — **Feito**
`MaintenanceService` agora usa o tamanho **exato** de cada tabela via `pg_total_relation_size`
(`IAppDbContext.GetTableSizesBytesAsync`): Execuções→`Executions`, Alterações→`Changes`,
Falhas→`Errors`, Logs→`SystemLog`. "Usuários inativos" (subconjunto de linhas de `Users`) é rateado
proporcionalmente. O total continua sendo o `pg_database_size` real.

### ✅ H. Diff antes/depois em Alterações — **Feito (diff por ponto, antes×depois)**
`GET /api/changes/{id}` traz `ConfigBullets` + `Points`. Numa **edição**, o `ProgramService.UpdateAsync`
calcula um **diff por ponto** (posicional) entre a curva antiga e a nova: ponto igual → `unchanged` (1
linha); ponto que mudou → `changed-before` + `changed-after` no **mesmo índice**; ponto só na nova →
`added`; só na antiga → `removed`. O front remonta a curva *antes* de `removed`+`changed-before`+`unchanged`
e a *depois* de `added`+`changed-after`+`unchanged`, e rotula cada ponto pelo papel (tabelas
Adicionado/Alterado/Removido). Sem persistir `BeforeProfile`/`AfterProfile`: reusa `Points` (jsonb) + o
enum `ChangePointRole` (novo membro `unchanged`) — **sem migração**. História limitada a
`ChangeRetentionPerProgramMax` (10) por programa, e `GET /api/changes?programId=` lista a de um programa.

---

## Pendentes (novos — 2026-05-31)

### ✅ I. Subir o limite de pontos do perfil 30 → 100 — **Feito**
`DomainConstants.ProfileMaxPoints` agora é **100** e governa os **dois** caminhos: o cap de **segmentos**
(`req.Segments`) e o de **pontos do import direto** (`req.Profile`) — a `ProfilePointsMax` (adicionada antes,
não commitada) foi **unificada** nela. Trace ao vivo segue coerente: `RunMeasuredMaxPoints` (600) decima as
**amostras medidas**, não o perfil — a curva derivada de 100 segmentos pode chegar a ~1200 pontos, usada só na
interpolação (`TempAt`), sem inflar o armazenamento da execução. **Sem migração** (constante, não schema). O
front espelha `100` em `limits.ts` e mostra `x/100` + tempo total.
> Já OK no backend (o front só consome): histórico de edições por programa já limitado a
> `ChangeRetentionPerProgramMax = 10`, e `GET /api/changes` aceita `programId` (dá pra listar as últimas 10
> edições e montar o gráfico multi-curva). **Carga de CPU (%):** agora é a **utilização real** (delta de
> `/proc/stat`, `(total−idle)/total`, já a média de todos os núcleos) — não mais o `/proc/loadavg`. Exposta em
> `SystemMetricsDto.CpuLoadPercent` (`/api/system/metrics` e `/api/system/status`) **e** em
> `MaintenanceOverviewDto.CpuLoadPercent` (`/api/maintenance/overview`), ao lado do `DiskFreeGB` — falta só o
> front mostrar uma `InfoRow` "Carga da CPU" junto do "Espaço livre no HD".

---

## Notas (histórico)

- A migration `AddFaultTypes` foi criada manualmente com:
  ```
  dotnet ef migrations add AddFaultTypes -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations
  ```

## Pendentes (adicionados pelo usuário)

### ✅ J. Programa de teste com +10 edições (relatório de Alterações) — **Feito**
Criar no BD um programa **editado ~12×** para exercitar o relatório de Alterações e ver a retenção
(`ChangeRetentionPerProgramMax = 10`) cortando para as 10 mais recentes. Fazer via `DbSeeder.Demo`
(ou script pontual) gerando um `ReflowProgram` + uma sequência de `ChangeLogEntry` com diff por ponto.

### ✅ K. Técnico de calibração ao trocar tema → 403 com mensagem de autorização — **Feito**
**Já era tratado** (não é crash): `MeController.RequireUserId()` lança `ForbiddenAppException`,
mapeada pelo `ExceptionMiddleware` para **403 ProblemDetails**. Ajustada só a **mensagem** para deixar
a falta de autorização explícita: *"A sessão técnica não tem autorização para acessar ou alterar
preferências de usuário."* (cobre o GET e o PUT de `/api/me/preferences`). O "break" no VSCode é o aviso
de *first-chance exception* (mesma situação do item #3) — desmarcar "All Exceptions" em Run and Debug →
BREAKPOINTS. Opcional no front: não oferecer troca de tema na sessão técnica.


## Validação 2026-05-31 (pedidos do front)

> ## ✅ Implementado no backend (2026-05-31)
> Todos os itens 1–6 **e** o item J foram implementados nesta rodada (build + 78 testes verdes, **1 migração**: `AddExecutionFailureLink`). Decisões e contrato pro front:
> - **1. Regular inicia/para execução:** policy `OperatorOrAdmin` (= `RequireRole(Admin, Regular)`) em `POST /api/runs/start` e `/stop`. O técnico (role Admin) continua passando. Sem mudança de DTO — o 403 some pro Regular.
> - **2. Senha do cadastro = MODELO B + vencimento duro.** `CreateUserRequest` **perdeu** `Password` (o front deve parar de enviar/coletar senha). A senha é gerada e enviada por e-mail; o usuário troca via `change-password`. **Novo:** se logar com a senha provisória **vencida** (`PasswordIssuedAt` + `PasswordChangeWithinDays`=7 dias), o backend gera/`envia` uma nova (throttle 1×/dia, e-mail **antes** de salvar o hash pra não travar ninguém) e **rejeita** o login com a string exata: `"Sua senha provisória expirou. Enviamos uma nova senha para o seu e-mail."` → o front mostra isso e manda consultar o e-mail. (Sem coluna nova: reusa `PasswordIssuedAt`.)
> - **3. Status `Abortado`:** novo membro de `ExecutionStatus` (literal de wire e de filtro = `"Abortado"`, sem acento). Parada manual agora persiste como `Abortado` (não mais `Falha`). Front: tratar o 3º status (badge/filtro). Sem migração.
> - **4. Motivo da falha + link:** `ExecutionDetailDto` ganhou `failureReason`/`errorCode` (=`FaultTypeCode`)/`linkedErrorId` (nuláveis). Em falha com fault catalogado (E-1xx) o backend grava um `ErrorLogEntry` e liga via `linkedErrorId` → deep-link pra `GET /api/errors/{id}`. Abort/limpo ficam `null`. (Migração `AddExecutionFailureLink`.)
> - **5. Favoritar idempotente:** `POST /api/programs/{id}/favorite` aceita corpo opcional `{ "favorite": bool }` (set idempotente); sem corpo continua **toggle** (compatível). Resposta `{ "favorite": bool }` inalterada. Delete segue 204.
> - **6. Diff estruturado:** `GET /api/changes/{id}` ganhou `diff` (`summary` + `points[]` consolidados: 1 linha por índice, `status` ∈ `unchanged|changed|added|removed`, `changedFields` ∈ `temp|timeSec|ramp`) + `beforeCurve`/`afterCurve`; `Points` legado mantido (transição). `GET /api/changes?before=<ISO>` filtra estritamente anterior (exclusivo). Sem migração.
> - **J. Programa de teste:** `DbSeeder.Demo` semeia "Perfil Teste de Edições" com 12 edições, persistindo só as 10 mais recentes (a retenção é na escrita), pra exibir o corte do histórico.
>
> Detalhe de cada item (necessidade/proposta/consumo) permanece abaixo, como referência.


Lote de pedidos do front gerados a partir do feedback da Validação 2026-05-31. Cada seção segue o formato **Necessidade / Situação atual / Proposta (endpoint/DTO) / Como o front vai consumir / Dúvidas-decisões**, com citações dos arquivos reais de ambos os repos.

**Prioridade sugerida:**
1. **Bloqueiam o uso (resolver primeiro):** (1) autorização — usuário Regular tomando 403 em `runs/start`/`stop`; (2) bug de login — não consigo logar com a senha criada no cadastro de usuário.
2. **Em seguida:** (3) status "Abortado" distinto de "Falha"; (4) motivo da falha + link para o Relatório de Erros.
3. **Depois:** (5) endpoints dedicados de favoritar/deletar por id (corte do flicker).
4. **Por último (refinamento do item H):** (6) diff estruturado por ponto no detalhe da Alteração de programa.

---

### 1. Usuário Regular recebe 403 ao iniciar/parar execução (`POST /api/runs/start` e `/stop`)

**Necessidade:** Na validação, a Vanessa (papel **Regular**) tocou em **INICIAR** num programa e o app retornou 403 ("ao iniciar um programa com o usuário regular dá 403"). Hoje o front oferece a ação a um Regular, mas o backend a rejeita — ou o front mente (mostra um botão que não funciona), ou o backend é restritivo demais. O Regular é o **operador do dia a dia** (a tela Início + a galeria de programas existem justamente para ele rodar perfis); é esperado que ele consiga **iniciar e parar** execuções. A decisão é primariamente de **autorização do backend**.

**Situação atual:**
- Backend: `RunsController.Start` e `RunsController.Stop` estão ambos com `[Authorize(Policy = AuthPolicies.AdminOnly)]` (`src/ReflowOven.Api/Controllers/RunsController.cs:11` e `:16`). A policy `AdminOnly` é `p.RequireRole(nameof(UserType.Admin))` (`src/ReflowOven.Api/Program.cs:102`; nome em `src/ReflowOven.Api/Auth/AuthPolicies.cs:7`). O papel vem do claim `ClaimTypes.Role` no JWT (`src/ReflowOven.Infrastructure/Auth/JwtTokenService.cs:34`), e o enum só tem `Admin`/`Regular` (`src/ReflowOven.Domain/Enums/Enums.cs:11-15`). O `GET /api/runs/status` já é só autenticado (sem policy). DTO de entrada: `StartRunRequest(string ProgramId)` (`src/ReflowOven.Application/Dtos/RunDtos.cs:3`). Logo, um Regular autenticado **bate na policy e recebe 403** — comportamento esperado da config atual, não um bug.
- Front: `canAccess` (`src/lib/auth.ts:61-64`) deixa o Regular alcançar `/`, onde a galeria mostra `ProgramCard` com o botão **INICIAR** sempre visível (`src/components/ProgramCard/index.tsx:21`, sem checagem de papel). O clique abre `RunModal` (`src/components/ProgramGallery/index.tsx`), que chama `api.startRun(program.id)` → `POST /api/runs/start` (`src/lib/api.ts:429`). A falha cai no `.catch` do `RunModal` (`src/components/RunModal/index.tsx:99-103`) e vira o toast pt-BR da ProblemDetails (a mensagem genérica de 403). O **PARAR** (`api.stopRun`, `RunModal/index.tsx:313`) sofreria o mesmo 403.

**Proposta (endpoint/DTO) — recomendada (a): liberar o papel Regular em iniciar/parar.** Sem mudança de rota, DTO ou shape de resposta — só a **autorização**. As assinaturas/respostas permanecem `POST /api/runs/start` (body `{ "programId": "<guid>" }`) → `200 RunStatusDto`, e `POST /api/runs/stop` (sem body) → `200 RunStatusDto`, idênticas às de hoje (camelCase já em uso nos DTOs). A decisão é: **trocar a exigência de policy** desses dois endpoints de "somente Admin" para "qualquer usuário autenticado" (Admin **ou** Regular).
- Opção mínima: remover o `[Authorize(Policy = AuthPolicies.AdminOnly)]` de `Start`/`Stop`; o `SetFallbackPolicy(...RequireAuthenticatedUser())` já vigente (`Program.cs:101`) garante que continuam exigindo login (e o `RunsController.Status` segue idêntico).
- Opção mais explícita/auditável (preferível): criar uma policy nomeada, p. ex. `AuthPolicies.OperatorOrAdmin` = `RequireRole(nameof(UserType.Admin), nameof(UserType.Regular))`, e anotar `Start`/`Stop` com ela. Mantém o padrão de policies do projeto e evita depender só do fallback. A sessão **técnica de calibração** já recebe role `Admin` no token (`JwtTokenService.cs:22`), então continua podendo operar — sem regressão.
- Sem migração de banco (papéis já existentes). O log de execução iniciada/finalizada do `RunManager` passa a registrar também atores Regular — desejável para auditoria.

**Proposta alternativa (b): manter 403 e esconder a ação no front.** Se a decisão for que **só Admin** opera o forno, o backend está certo e o front deve parar de oferecer a ação ao Regular: condicionar o botão **INICIAR** ao papel (reusar `canManagePrograms(role)` ou criar `canRunPrograms(role)` em `src/lib/auth.ts`) em `ProgramCard`/`ProgramGallery`, ocultando ou desabilitando (com tooltip "Apenas administradores podem iniciar execuções"). Nesse caso, ajustar também a rota — se o Regular não inicia nada, talvez nem deva ver a galeria com botões de ação.

**Recomendação:** opção **(a)**, policy `OperatorOrAdmin` nos dois endpoints. O perfil Regular existe para operar, e a UI já o leva à galeria; restringir a operação ao Admin contradiz o propósito do papel.

**Como o front vai consumir:**
- Se **(a)**: nenhuma mudança de contrato em `src/lib/api.ts` (`startRun`/`stopRun` inalterados). O 403 deixa de ocorrer; o `RunModal` passa a abrir e transmitir a telemetria normalmente para o Regular. Opcional: reforçar a mensagem de erro de 403 já tratada no `.catch` do `RunModal/index.tsx:99-103` caso o backend ainda negue por outra razão (ex.: execução já em andamento — esse é 409, não 403).
- Se **(b)**: editar `src/lib/auth.ts` (novo `canRunPrograms`) e `src/components/ProgramCard/index.tsx` + `ProgramGallery/index.tsx` para ocultar/desabilitar **INICIAR**; nenhuma mudança em `api.ts`.

**Dúvidas/decisões (backend owner):**
1. **Regular deve poder iniciar E parar execuções?** (recomendação: sim, ambos — não faz sentido um operador iniciar e não conseguir parar com segurança).
2. Preferem a **policy nomeada** `OperatorOrAdmin` (explícita, mantém o padrão) ou só **remover** o atributo e confiar no `FallbackPolicy` (mínimo)?
3. Há alguma outra ação operacional hoje `AdminOnly` que **deveria** acompanhar essa liberação para o operador (p. ex. leituras de diagnóstico/`/api/diagnostics/readings`, hoje `AdminOnly` em `DiagnosticsController.cs`)? Fora do escopo deste item, mas convém alinhar a fronteira "operador × admin" de uma vez.

---

### 2. Criação de usuário — "já existe" antes de fechar o modal + não consigo logar com a senha criada

**Necessidade:** Dois sintomas relatados na Validação: (a) ao criar um usuário, "Esse usuário já existe." aparece no campo *Usuário* **antes** de o modal fechar, dando a impressão de que a resposta do servidor chega cedo demais; (b) depois **não dá para logar** com a senha digitada no modal. O fluxo de cadastro precisa ser previsível: a senha que o admin digita tem que ser a senha que loga (ou, se for o modelo de senha gerada do item 7, o front precisa parar de pedir senha e comunicar isso ao usuário). Hoje o front coleta `Senha`/`Confirmar senha` (obrigatórias, `UserCreateModal.tsx:96-111`), mas o backend a ignora — é o cerne do bug (b).

Diagnóstico por sub-item (causa real, já confirmada no código):
- **(a) é 100% front, não é o 409 chegando tarde.** É um **pré-check no cliente**: `usernameExists()` (`src/lib/users.ts:110-113`, comparando contra a lista em cache `usersStore`) é avaliado a cada tecla em `UserCreateModal.tsx:52` e renderiza o aviso inline em `UserCreateModal.tsx:90` na hora. O 409 do servidor (`ConflictException "Já existe um usuário com esse nome."`, `UserService.cs:29-30`) só ocorre no submit; o modal só fecha em sucesso (`onClose()` em `UserCreateModal.tsx:70`). Ou seja, o "antes de fechar" é o aviso reativo do cliente, **não** uma resposta adiantada do servidor. Nenhuma mudança de backend é necessária para (a).
- **(b) é 100% backend.** Em `UserService.CreateAsync` o backend **descarta `req.Password`** e grava o hash de uma senha **gerada** pelo sistema: `var tempPassword = PasswordGenerator.Generate();` → `PasswordHash = hasher.Hash(tempPassword)` (`UserService.cs:34,43`), com `MustChangePassword=true`. O login confere a senha digitada contra esse hash gerado (`AuthService.cs:48 hasher.Verify(password, user.PasswordHash)`), então a senha do modal **nunca** confere → "Senha incorreta." É exatamente o conflito apontado no item 7 do TODO.

**Situação atual:**
- Backend: `CreateUserRequest(Name, Email, Password, Type, Status)` (`UserDtos.cs:19-24`) carrega `Password`, mas `UserService.CreateAsync` o ignora e usa `PasswordGenerator.Generate()` + `MustChangePassword=true` + e-mail de onboarding (`UserService.cs:21-65`). `POST /api/users` é `AdminOnly` e devolve o `UserDto` em 201 `CreatedAtAction` (`UsersController.cs:14-19`). Conflito de nome → 409 (`UserService.cs:29-30`). O `SessionDto` já expõe `MustChangePassword` (`AuthDtos.cs:11-19`) e existe `POST /api/auth/change-password` (`ChangePasswordRequest`, `AuthService.ChangePasswordAsync`). `Validation.ValidatePassword` aceita 8–72 chars (`DomainConstants.PasswordMinLength=8`, `PasswordMaxLength=72`).
- Front consome: `src/lib/api.ts:417-418 createUser({name,email,password,type,status})` → `src/lib/users.ts:116-137 upsertUser` (envia `password`, e até exige `if (!user.password) throw` em `users.ts:127`) → `UserCreateModal.tsx:62-71 handleCreate` (coleta senha, mostra toast, fecha). O 409 vira `ApiError` (mapeado de ProblemDetails em `api.ts:90-92`) e é exibido como toast em `UserCreateModal.tsx:69`. O front **não** tem método para `change-password` nem trata `mustChangePassword` no login (`auth.ts:16-28` apenas guarda a sessão).

**Proposta (endpoint/DTO):** Decisão de modelo (o dono do backend escolhe **A** ou **B** — ver Dúvidas). A proposta principal é **A**, por ser a menor mudança que faz "a senha digitada é a que loga".

**Opção A — honrar a senha enviada pelo front (recomendada, menor mudança):** `POST /api/users` passa a **usar `req.Password`** quando vier preenchida; cai na geração automática só quando vier vazia.
- DTO: manter `CreateUserRequest`. Adicionar 1 campo opcional na resposta para o front saber o que aconteceu, sem quebrar o `UserDto` existente — ou reusar `MustChangePassword`.

Request (camelCase, inalterado):
```json
{ "name": "joao.silva", "email": "joao@dominio.com", "password": "umaSenhaForte1!", "type": "Regular", "status": "Ativo" }
```
Resposta 201 (acrescentar `mustChangePassword` ao `UserDto`, default `false`):
```json
{ "id": "…", "name": "joao.silva", "email": "joao@dominio.com", "type": "Regular", "status": "Ativo",
  "createdAt": "2026-05-31T12:00:00Z", "lastLogin": null, "events": [], "mustChangePassword": false }
```
Regra: se `password` presente → `Validation.ValidatePassword(req.Password)` + `hasher.Hash(req.Password)` + `MustChangePassword=false` (e-mail de boas-vindas sem senha, opcional); se ausente/null → comportamento atual (gera + envia por e-mail + `MustChangePassword=true`). 409 de nome inalterado. Convenção de enums via `[JsonStringEnumMemberName]` (Ativo/Inativo, Admin/Regular) preservada.

**Opção B — assumir o modelo "senha gerada" do item 7:** Backend mantém a geração; remove-se `Password` do contrato e o front para de coletar senha.
- DTO: `CreateUserRequest(Name, Email, Type, Status)` (remover `Password`).
- Resposta: `UserDto` + `mustChangePassword: true`. O usuário criado loga com a senha do e-mail e é forçado a trocar (`change-password` já existe).

**Como o front vai consumir:**
- **(a) FE — fix imediato (independe do backend):** em `UserCreateModal.tsx`, o pré-check `usernameExists` é UX legítima (avisa antes do submit), mas para alinhar com o feedback: (i) manter o aviso inline porém deixar claro que é validação local, e (ii) garantir que o 409 do servidor **também** seja tratado no `catch` de `handleCreate` (já é, via toast `ApiError`, `UserCreateModal.tsx:69`) — nenhuma reordenação de fechamento é necessária porque o modal só fecha em sucesso (`onClose()` em `UserCreateModal.tsx:70`, após `await upsertUser`). Não há "resposta chegando antes de fechar"; o sintoma é só o pré-check do cliente. Se quiser, suavizar para mostrar o aviso apenas no blur/submit em vez de a cada tecla.
- **(b) FE — depende da opção escolhida:**
  - Se **A**: nada muda no contrato do front (`api.ts:417`, `users.ts:128-134` já enviam `password`). Passa a funcionar assim que o backend honrar `req.Password`. Opcional: ler `mustChangePassword` no `UserDto`.
  - Se **B**: remover os campos `Senha`/`Confirmar senha` de `UserCreateModal.tsx` e a obrigatoriedade em `users.ts:127`; remover `password` de `api.createUser` (`api.ts:417-418`) e de `upsertUser` (`users.ts:128-134`); exibir aviso "a senha será enviada por e-mail". Em ambos os casos, para o `mustChangePassword` do login, adicionar `api.changePassword(...)` em `api.ts` (hoje inexistente) e tratar `session.mustChangePassword` em `auth.ts login` (hoje ignorado, `auth.ts:21-23`) abrindo um modal de troca.

**Dúvidas/decisões (backend):**
1. **A ou B?** A senha digitada deve valer (A) ou o cadastro deve adotar o modelo de senha gerada do item 7 e o front parar de pedir senha (B)? Esta é a decisão central — define se (b) é corrigido no backend (A) ou no front (B).
2. Em **A**, ao criar com senha definida pelo admin, `MustChangePassword` fica `false` (sem ciclo de troca) e o e-mail de onboarding ainda é enviado (sem expor a senha)? Confirmar.
3. Expor `mustChangePassword` no `UserDto` é aceitável, ou prefere o front ler isso só no `SessionDto` do login?
4. Em **A**, mantém-se o fallback (gerar+enviar) quando `password` vem vazio, ou o front sempre manda senha (tornando o fallback morto)?
5. Política de senha: o front valida 8–`PASSWORD_MAX_LENGTH`; o backend exige 8–72 (`DomainConstants`). Confirmar que `PASSWORD_MAX_LENGTH` no `limits.ts` casa com 72 para não divergir a mensagem de erro.

---

### 3. Status "Abortado" na execução (distinto de "Falha")

**Necessidade:** Na validação, parar a execução pelo botão "Parar" registra o relatório como **"Falha"** (ícone vermelho `cancel`), mas o evento no log diz **"Execução abortada"** — sinal contraditório para o operador. A parada manual não é um defeito; precisa de **status + ícone próprios "Abortado"** (cor/ícone neutros, ex. âmbar/`stop_circle`), distintos de "Concluído" e de "Falha". O front lista o status só pelo badge do resumo (a lista não traz `events`), então não há como derivar "abortado" do log no cliente — o backend tem que emitir o status distinto na **lista**, no **detalhe** e no **filtro**.

**Situação atual:**
- A run ao vivo já distingue parada manual: `RunManager.StopAsync` chama `FinalizeAsync(run, RunStatus.Aborted, ...)`; o enum `RunStatus` (`src/ReflowOven.Domain/Enums/Enums.cs:52-57`) tem `running`/`done`/**`aborted`**.
- Porém ao persistir, `RunManager.FinalizeAsync` (`src/ReflowOven.Infrastructure/Run/RunManager.cs:155`) **colapsa** tudo que não é `Done` em `Falha`: `Status = status == RunStatus.Done ? ExecutionStatus.Concluido : ExecutionStatus.Falha`. O único vestígio do abort é o `LogEvent` "Execução abortada" (linha 168) e a notificação (linhas 182-187).
- O enum persistido/serializado `ExecutionStatus` (`Enums.cs:59-63`) só tem `Concluído` e `Falha`.
- Esse status flui sem transformação por `ExecutionSummaryDto.Status` e `ExecutionDetailDto.Status` (`src/ReflowOven.Application/Dtos/ReportDtos.cs:16-43`) e pelo `ReportService.ExecutionsAsync`/`ExecutionAsync` (`src/ReflowOven.Application/Services/ReportService.cs:7-40`). O filtro de status é `ReportQuery.Status` parseado por `EnumWire.TryFromWire<ExecutionStatus>` (`ReportService.cs:17`).
- Consumo no front: `src/lib/api.ts:319` `ExecutionStatusWire = "Concluido" | "Falha"`; `src/lib/reports.ts:46` `ExecutionStatus = "Concluído" | "Falha"`; mapeamento direto em `src/lib/reportsClient.ts:36,63` (`status: e.status`); badge em `src/components/RelatoriosScreen/badges.tsx:8-15` (`ok = status === "Concluído"`, senão vermelho); filtro em `src/components/RelatoriosScreen/index.tsx:44-48`.

**Proposta (endpoint/DTO):** Nenhum endpoint novo. Adicionar um terceiro membro ao enum `ExecutionStatus` e mapear a run abortada para ele.

1. `ExecutionStatus` (`Enums.cs:59-63`) — novo membro com literal **acentuado** no JSON de resposta (convenção `Concluído`/`Crítico`):
   ```csharp
   public enum ExecutionStatus
   {
       [JsonStringEnumMemberName("Concluído")] Concluido,
       Falha,
       Abortado, // membro sem acento → wire de RESPOSTA e de FILTRO idênticos: "Abortado"
   }
   ```
   Observação: `Abortado` não leva acento, então o valor de resposta e o member-name (usado no filtro) coincidem em **`Abortado`** (igual a `Falha`, que também não usa `[JsonStringEnumMemberName]`).

2. `RunManager.FinalizeAsync` (`RunManager.cs:155`) — mapear os 3 casos em vez de 2:
   ```csharp
   Status = status switch
   {
       RunStatus.Done    => ExecutionStatus.Concluido,
       RunStatus.Aborted => ExecutionStatus.Abortado,
       _                 => ExecutionStatus.Falha,
   },
   ```
   Manter o `LogEvent`/notificação "Execução abortada" como estão (já coerentes). Decidir se o evento de abort deve virar `LogEventKind.Alerta` (atual) ou um kind neutro.

3. Resposta — o valor aparece no campo `status` (camelCase) tanto da lista quanto do detalhe, sem mudança estrutural de DTO:
   - `GET /api/executions` (item de `items[]`, `ExecutionSummaryDto`):
     ```json
     { "id": "…", "programId": "…", "programName": "Perfil SMD Padrão", "userName": "Lucas Silva",
       "startedAt": "2026-05-31T18:00:00-03:00", "durationSeconds": 73,
       "status": "Abortado", "peakTemp": 210, "peakCurrent": 8.4 }
     ```
   - `GET /api/executions/{id}` (`ExecutionDetailDto`): mesmo campo `"status": "Abortado"`.

4. Filtro — `GET /api/executions?status=Abortado`. Já funciona automaticamente: `EnumWire.TryFromWire<ExecutionStatus>(q.Status, …)` (`ReportService.cs:17`) resolve o novo membro; nada a mudar no serviço.

**Como o front vai consumir:**
- `src/lib/api.ts:319` — `ExecutionStatusWire = "Concluido" | "Falha" | "Abortado"` (member-names, usados como `?status=`).
- `src/lib/reports.ts:46` — `ExecutionStatus = "Concluído" | "Falha" | "Abortado"` (literais de resposta).
- `src/lib/reportsClient.ts:36` — atualizar o `status` do `ExecSummaryDto` inline (segue tipado por `ExecutionStatus`; o mapeamento `status: e.status` em :63 já passa por valor).
- `src/components/RelatoriosScreen/badges.tsx:8-15` — `StatusBadge` deixa de ser booleano (`ok`); virar um mapa por status (`Concluído`→`check_circle`/verde, `Falha`→`cancel`/vermelho, `Abortado`→ ex. `stop_circle`/âmbar `text-amber-700 dark:text-amber-400`), no padrão dos `ACTION_STYLE`/`SEVERITY_STYLE` do mesmo arquivo.
- `src/components/RelatoriosScreen/index.tsx:44-48` — acrescentar a opção `{ value: "Abortado", label: "Abortado", icon: "stop_circle" }` ao `FILTERS.execucoes`.

**Dúvidas/decisões:**
1. Confirmar o literal de wire **`Abortado`** (sem acento, igual em resposta e filtro) — ok pela convenção?
2. **Migração/dados legados:** runs paradas manualmente já gravadas estão como `Falha` e permanecerão assim (só novas runs ganham `Abortado`). Aceitável, ou querem um script de backfill (ex.: execuções com `LogEvent` "Execução abortada" → `Abortado`)?
3. O enum é persistido como TEXT via `PtBrEnumConverter`; adicionar um membro **não** exige migração de schema — confirmar que não há `CHECK`/constraint enumerando os valores na coluna `Status`.
4. Ícone/cor canônicos do "Abortado" (sugestão: `stop_circle`, âmbar) — decisão de UI, mas vale alinhar para a notificação/sino usarem o mesmo tom.
5. O `kind` do `LogEvent` de abort deve continuar `Alerta` ou passar a um kind neutro, agora que o status já comunica o abort?

---

### 4. Motivo da falha + link para o Relatório de Erros na execução

**Necessidade:** No feedback da Validação, uma execução com falha aparece só como **"Falha"**, sem explicar o **porquê** — o operador precisa abrir a aba Erros e adivinhar qual registro corresponde. O FE quer (1) exibir o **motivo da falha** direto no detalhe da execução e (2) um **link "Ver no Relatório de Erros"** que abra a aba Erros já filtrada/aberta no erro exato daquela execução. Hoje isso é impossível com precisão: o motivo só existe implícito (e quando muito) na 1ª mensagem de evento `kind:"falha"`, e execução e erro **não compartilham id**, então um deep-link confiável não dá pra construir no cliente.

**Situação atual:**
- `ExecutionDetailDto` (`src/ReflowOven.Application/Dtos/ReportDtos.cs:27-43`) só traz `Status` (`ExecutionStatus.Falha`), `FaultAtT`/`FaultAtTemp` (nuláveis), `Events` e `Trace` — **nenhum campo de motivo, código ou link para o erro**.
- A entidade `ExecutionReport` (`src/ReflowOven.Domain/Entities/ExecutionReport.cs`) tem `FaultAtT`/`FaultAtTemp` mas **o `RunManager.FinalizeAsync` nunca os preenche** (`src/ReflowOven.Infrastructure/Run/RunManager.cs:146-172`): uma run que termina mal vira `ExecutionStatus.Falha` (linha 155) com eventos genéricos (`"Execução abortada"`, `kind: Alerta`) — não há sequer um evento `kind: falha` real, nem criação de `ErrorLogEntry`.
- `ErrorLogEntry` (`src/ReflowOven.Domain/Entities/ErrorLogEntry.cs`) e `ExecutionReport` **não têm FK entre si**. `LogEvent.ErrorLogEntryId` (ExecutionReport.cs:80) liga um evento a um erro, não a execução ao erro. Erros só são gerados hoje pelo `DbSeeder.Demo` (`BuildError`), desacoplados de qualquer run.
- `ReportService.ExecutionAsync` (`src/ReflowOven.Application/Services/ReportService.cs:29-40`) projeta o DTO 1:1 da entidade. FE consome em `reportsClient.ts:74` (`fetchExecutionDetail` → `ExecDetailDto`).

**Proposta (endpoint/DTO):** Acrescentar **3 campos nuláveis** ao `ExecutionDetailDto` (sem novo endpoint — segue em `GET /api/executions/{id}`), preenchidos pelo `RunManager` no caminho de falha e, quando houver fault real, gravando também um `ErrorLogEntry` cujo `Id` vira o `linkedErrorId`:

```jsonc
// GET /api/executions/{id} — campos novos no fim do JSON (camelCase, demais campos inalterados)
{
  "id": "0f3c...", "programName": "SMD Sem Chumbo 245ºC", "status": "Falha",
  "faultAtT": 142, "faultAtTemp": 261,
  // --- novos ---
  "failureReason": "Corrente de saída excedeu o limite. Processo interrompido.", // string | null
  "errorCode": "E-112",          // string | null  (= FaultType.Code, quando há erro catalogado)
  "linkedErrorId": "9a1b..."     // string | null  (Guid do ErrorLogEntry correspondente; null se não houve)
}
```

- DTO: adicionar `string? FailureReason, string? ErrorCode, Guid? LinkedErrorId` ao final de `ExecutionDetailDto`.
- Entidade: adicionar a `ExecutionReport` `FailureReason` (string?), `FaultTypeCode` (string?) e `LinkedErrorId` (Guid? — FK opcional para `ErrorLogEntry`); migration EF nova.
- `RunManager.FinalizeAsync`: no ramo de falha, definir `FaultAtT/FaultAtTemp`, `FailureReason` (mesmo texto pt-BR do motivo) e criar/gravar um `ErrorLogEntry` com aquele `FaultTypeCode`, setando `report.LinkedErrorId = error.Id` antes do `SaveChanges`. Em abort manual (sem fault de placa) os três campos ficam `null`.
- `ReportService.ExecutionAsync`: incluir os 3 campos na projeção. `Status` continua sendo o enum pt-BR `[JsonStringEnumMemberName]` já existente.

**Como o front vai consumir:**
- `src/lib/reportsClient.ts`: estender `ExecDetailDto` com `failureReason?: string | null; errorCode?: string | null; linkedErrorId?: string | null` e repassá-los em `fetchExecutionDetail` para o `ExecutionReport`.
- `src/lib/reports.ts`: adicionar ao type `ExecutionReport` os campos `failureReason?`, `errorCode?`, `linkedErrorId?`.
- `ExecutionDetail.tsx`: mostrar `failureReason` (com `errorCode`) no cabeçalho da falha; quando `linkedErrorId` existir, renderizar um botão "Ver no Relatório de Erros".
- `RelatoriosScreen/index.tsx`: o botão chama um novo handler que faz `setTab("erros")` + `openError({ id: linkedErrorId })` (reusa `fetchErrorDetail`/`openError` já existentes em index.tsx:112), abrindo o overlay no erro exato.

**Dúvidas/decisões:**
- **Sempre criar `ErrorLogEntry` em toda falha** (mesmo abort manual sem fault de placa) ou só quando a placa reportar um fault catalogado? Sugiro só com fault real → `linkedErrorId` null em abort, e `failureReason` opcionalmente preenchido com texto genérico.
- `errorCode` deve apontar para um `FaultType.Code` existente (E-101..E-160) ou pode ser código livre quando não houver fault catalogado?
- A placa/loop de controle (`Rs422PowerBoard`/`RunControlLoopService`) hoje **não entrega motivo de fault** ao `RunManager` — isso depende do firmware expor o código/motivo do fault no protocolo RS422. Confirmar se já existe ou se entra como passo de hardware separado (sem ele, `failureReason` ficaria limitado a heurística como excesso de temperatura/corrente vs. limites).

---

### 5. Endpoints dedicados de favoritar/desfavoritar e deletar por id (sem recarregar o programa inteiro)

**Necessidade:** No feedback de validação o usuário relatou que **só favoritar/desfavoritar um programa faz a tela "piscar branco"** — a galeria some por um instante e volta. Ele pediu um POST de favoritar por id (e idem deletar por id) para que a ação seja pontual, sem reenviar/recarregar o programa inteiro. Investigando, os endpoints dedicados **já existem no backend** (item de favoritos por usuário já foi feito): a causa do flicker é o **front**, que após a chamada dispara um refetch completo da página. Portanto não é mais um "criar do zero", e sim **(a) confirmar/ajustar o contrato** desses endpoints e **(b) o front passar a atualizar só a flag localmente**. Convém apenas decidir se o favoritar deve continuar **toggle** ou virar **set explícito** (idempotente), o que torna o update otimista do front imune a duplo-clique/condição de corrida.

**Situação atual:**
- **Favoritar:** `POST /api/programs/{id}/favorite` já existe e é **dedicado/por-usuário** (escopado pelo JWT) — `ProgramsController.ToggleFavorite` (`src/ReflowOven.Api/Controllers/ProgramsController.cs:41-48`). É **toggle** (sem body), retorna **200** com `FavoriteResult(bool Favorite)` (`ProgramsController.cs:50`) → JSON `{ "favorite": true|false }`. A lógica está em `ProgramService.ToggleFavoriteAsync(id, userId, ct)` (`src/ReflowOven.Application/Services/ProgramService.cs:122-139`): valida existência (`NotFoundException`), insere/remove uma linha em `db.Favorites` (`FavoriteProgram { UserId, ProgramId }`, `src/ReflowOven.Domain/Entities/ReflowProgram.cs:55-62`) e retorna o novo estado. **Não** toca no `ReflowProgram` nem regrava o programa.
- **Deletar:** `DELETE /api/programs/{id}` já existe, `[AdminOnly]`, e retorna **204 NoContent** (`ProgramsController.cs:33-39`); é soft-delete + auditoria em `ProgramService.DeleteAsync` (`ProgramService.cs:109-120`).
- **Consumo no front:** `api.ts` já chama os dois endpoints corretos — `toggleFavorite: POST .../favorite` tipado `{ favorite: boolean }` e `deleteProgram: DELETE ...` tipado `void` (`src/lib/api.ts:411-412`; `request` já trata 204 → `undefined`, `api.ts:75`). **O problema** está em `src/lib/programStore.ts`: `toggleFavorite` (linhas 117-120) e `deleteProgram` (linhas 122-125) fazem `await loadPrograms(currentQuery)` depois da mutação; `loadPrograms` seta `loading=true`/`notify()` e, na primeira passada, zera `programs`/`favoriteIds` para arrays vazios — daí o **flash branco**. O toggle no card vem de `ProgramListCard/index.tsx:40` (botão estrela) e o delete do `ConfirmDialog` (linhas 98-104).

**Proposta (endpoint/DTO):** O contrato atual **já satisfaz** a necessidade; o ajuste é pequeno e opcional, para deixar o update otimista robusto:
- **Manter** `DELETE /api/programs/{id}` → **204** (sem corpo). Nada a fazer. (Confirmação solicitada: continua 204.)
- **Favoritar — adicionar a variante "set explícito" (idempotente)** aceitando body, mantendo o toggle como fallback quando o body vier ausente:
  - Request `POST /api/programs/{id}/favorite`:
    ```json
    { "favorite": true }
    ```
  - Response **200**:
    ```json
    { "favorite": true }
    ```
  - Backend: estender `FavoriteResult` continua igual; adicionar um DTO de entrada (ex.: `public sealed record SetFavoriteRequest(bool? Favorite);`) e, em `ToggleFavoriteAsync`, se `Favorite` vier preenchido fazer **upsert/delete idempotente** (garante o estado pedido, sem depender do estado anterior); se vier `null`, manter o toggle atual. Sem migração (reusa a tabela `Favorites`). Erros seguem o padrão ProblemDetails pt-BR já em uso (`NotFoundException` → 404; sessão sem usuário → 403 `ForbiddenAppException`). Enums não se aplicam aqui (campo `bool`), então `[JsonStringEnumMemberName]` não é necessário.

**Como o front vai consumir:** A mudança é em `src/lib/programStore.ts` (a `src/lib/api.ts` só precisa, opcionalmente, passar o body `{ favorite }` na chamada da linha 412):
- `toggleFavorite(id)`: **update otimista local** — alternar `favoriteIds` (adicionar/remover o id) e `notify()` **antes** da chamada; chamar `api.toggleFavorite(id, next)`; em erro, **reverter** a flag e mostrar toast (o `catch` já existe em `ProgramListCard/index.tsx:40`). **Remover** o `await loadPrograms(currentQuery)`. Resultado: a estrela troca na hora, sem refetch nem flash. Quando o filtro ativo é `favorites`, desfavoritar pode deixar um card "fantasma" até a próxima navegação — aceitável (ou removê-lo localmente da lista também).
- `deleteProgram(id)`: remover o programa da lista **localmente** (filtrar `programs` pelo id e decrementar `total`) e `notify()`, sem `loadPrograms`; em erro, reverter + toast. Evita o flash e mantém a página estável. (Opcional: refetch só quando a página esvaziar, para puxar o próximo item da paginação.)

**Dúvidas/decisões (para o backend):**
1. Favoritar continua **toggle puro** ou aceita **`{ favorite: bool }`** (idempotente)? Recomendo aceitar o body (toggle como fallback) para o front fazer set explícito otimista, imune a duplo-clique/corrida. Em ambos os casos a resposta `{ "favorite": bool }` resolve o front.
2. Confirmar que **`DELETE /api/programs/{id}` permanece 204** (o front trata 204 → void).
3. O favoritar hoje **não exige AdminOnly** (qualquer sessão com usuário pode favoritar, é por-usuário) — confirmar que isso é o desejado (técnico/calibração tem `userId`? se a sessão técnica não tiver usuário, o 403 atual está correto).

---

### 6. Diff estruturado por ponto no detalhe da Alteração de programa (refinamento do item H)

**Necessidade:** O detalhe de uma Alteração de programa (`Relatórios → Alterações`) precisa de um diff por ponto mais limpo de consumir e renderizar, e de um `at` ISO ordenável. Isso atende à validação do usuário (FE `TODO.md` bug 7 + "Pendências de backend → Diff estruturado da Alteração"): o gráfico antes×depois será refeito (um único gráfico, edição aberta em linha contínua, demais tracejadas) e a lista de "outras edições" deve **excluir as posteriores** à edição aberta (abrir a penúltima não pode listar a última). Hoje o front remonta as curvas a partir de papéis fragmentados e formata `at` como `dd/mm/aa` (string não-ordenável), o que dificulta tanto o gráfico quanto o filtro por data.

**Situação atual:**
- O item H já entrega um diff **por ponto** posicional. `ProgramService.UpdateAsync` chama `BuildEditDiff(before, after)` (`src/ReflowOven.Application/Services/ProgramService.cs:83-107`, `232-268`): ponto igual → 1 linha `unchanged`; ponto que mudou → **duas** linhas (`changed-before` + `changed-after`) no mesmo índice; só-novo → `added`; só-antigo → `removed`. Papéis no enum `ChangePointRole` (`src/ReflowOven.Domain/Enums/Enums.cs:167-174`).
- `GET /api/changes/{id}` (`ChangesController.Get`, `src/ReflowOven.Api/Controllers/ReportControllers.cs:23-24`) → `ReportService.ChangeAsync` (`src/ReflowOven.Application/Services/ReportService.cs:100-108`) devolve `ChangeDetailDto { ..., At, ConfigBullets, Points: ChangePointRowDto[] }` (`src/ReflowOven.Application/Dtos/ReportDtos.cs:79,89-98`). Não há curva amostrada: o diff carrega só os pontos do editor (índice/temp/timeSec/ramp), não a curva interpolada do `ReflowProgram.Profile`.
- `GET /api/changes` (`ReportService.ChangesAsync`, `:77-98`) ordena por `At desc`, aceita `Search/From/To/Action/ProgramId` via `ReportQuery` (`ReportDtos.cs:111-121`), mas **não tem filtro por data máxima/anterioridade** além de `To` (tratado como fim do dia).
- Consumo no front: `fetchChangeDetail`/`fetchChanges` em `src/lib/reportsClient.ts:94-146` particiona `c.points` por `role` e reconstrói `beforeProfile`/`afterProfile` via `pointsToProfile` (`:111-115`); tipos em `src/lib/reports.ts:178-217` (`ChangeDetail`, `ChangePointRow`); renderizado por `src/components/RelatoriosScreen/ChangeDetail.tsx` (tabelas `PointTable`/`DiffTable` + `EditionCompare`, que hoje filtra as outras edições só por `e.id !== current.id`, `:200`). `at` é formatado para `dd/mm/aa - HH:MM:SS` em `fmtStamp` (`reportsClient.ts:12-17`). Query no FE: `ChangeReportQuery` (`src/lib/api.ts:339-343`); `qs()` (`:96-103`) envia as chaves camelCase verbatim (ASP.NET liga a `ReportQuery` case-insensitive).

**Proposta (endpoint/DTO):**

1. **`GET /api/changes/{id}`** passa a devolver, para `detailKind: "program"`, um diff **consolidado** (cada índice **uma vez**) + `summary` + as curvas amostradas `before`/`after` para o gráfico — substituindo o split `changed-before`/`changed-after`. Novos DTOs (mantendo `[JsonStringEnumMemberName]` para os enums):

```jsonc
{
  "id": "7b3f...",
  "at": "2026-05-31T18:42:07.123Z",        // ISO 8601 cru (UTC); o front formata
  "action": "Editado",                      // Criado | Editado | Removido
  "target": "Perfil SMD Padrão",
  "userName": "Lucas Silva",
  "programId": "PRG-001",
  "detailKind": "program",                  // program | config
  "configBullets": null,                    // preenchido só em config
  "diff": {
    "summary": {
      "totalChanges": 3,                    // added + removed + changed (exclui unchanged)
      "added": 1, "removed": 1, "changed": 1, "unchanged": 5,
      "changedFields": { "temp": 1, "timeSec": 1, "ramp": 0 }
    },
    "points": [
      { "index": 1, "status": "unchanged",
        "before": { "temp": 150, "timeSec": 60, "ramp": "Linear" },
        "after":  { "temp": 150, "timeSec": 60, "ramp": "Linear" },
        "changedFields": [] },
      { "index": 2, "status": "changed",
        "before": { "temp": 200, "timeSec": 20, "ramp": "Linear" },
        "after":  { "temp": 100, "timeSec": 100, "ramp": "Fixo" },
        "changedFields": ["temp", "timeSec", "ramp"] },
      { "index": 6, "status": "added",
        "before": null,
        "after":  { "temp": 220, "timeSec": 10, "ramp": "Fixo" },
        "changedFields": [] },
      { "index": 7, "status": "removed",
        "before": { "temp": 40, "timeSec": 30, "ramp": "Linear" },
        "after":  null,
        "changedFields": [] }
    ]
  },
  "beforeCurve": [ { "t": 0, "temp": 25 }, { "t": 60, "temp": 150 }, /* ... */ ],
  "afterCurve":  [ { "t": 0, "temp": 25 }, { "t": 60, "temp": 150 }, /* ... */ ]
}
```

   - **Enums no fio** (`[JsonStringEnumMemberName]`): `status` ∈ `unchanged|changed|added|removed`; `changedFields[]` ∈ `temp|timeSec|ramp`; `ramp` mantém os literais pt-BR de `RampShape` (`Linear`/`Fixo`/`Parábola positiva`/`Parábola negativa`).
   - **`Criado`**: cada ponto `status: "added"` (`before: null`); `summary.added` = nº de pontos; `beforeCurve` ausente/`[]`. **`Removido`**: cada ponto `status: "removed"` (`after: null`); `afterCurve` ausente/`[]`.
   - **Produção no backend:** já há toda a informação. Opção mínima e **sem migração**: manter o armazenamento atual (`ChangeLogEntry.Points` com `ChangePointRole`, jsonb) e **consolidar na leitura** — `ReportService.ChangeAsync` colapsa o par `changed-before`/`changed-after` do mesmo índice em uma linha `changed`, mapeia `unchanged`/`added`/`removed` direto, e calcula `summary` + `changedFields` comparando `before.temp/timeSec/ramp` vs `after.*`. As curvas `beforeCurve`/`afterCurve` saem do diff (já é o suficiente para o gráfico) ou, se quiser a curva interpolada real, do `ReflowProgram.Profile` (só o `after` está vivo após a edição; o `before` continua vindo das linhas do diff — o que o front já faz hoje em `pointsToProfile`). Recomenda-se derivar ambas as curvas **do próprio diff** para não depender do estado atual do programa (mantém o histórico fiel).
   - `ChangePointRole` pode ser **mantido** internamente (storage/EF); a mudança é só na **forma de resposta** do DTO. O front deixa de receber `changed-before`/`changed-after` separados.

2. **`at` ISO ordenável (lista):** `GET /api/changes` e `GET /api/changes/{id}` já carregam `At` como `DateTimeOffset`, que serializa em ISO 8601. **Recomendação:** manter o `at` **ISO cru** no JSON (o front formata) — `ChangeSummaryDto`/`ChangeDetailDto` já fazem isso; nada muda no backend. O problema é só do front, que estava formatando cedo demais.

3. **Filtro de anterioridade em `GET /api/changes`** (para o `EditionCompare` listar só edições **≤ a data da aberta**): adicionar a `ReportQuery` um parâmetro opcional **`before`** (ISO, exclusivo) ou reusar a semântica de `maxDate`. Sugestão: `before` = "estritamente anterior", aplicado como `query.Where(c => c.At < before)` **antes** de `CountAsync`/`Skip`/`Take` em `ChangesAsync` (junto dos filtros existentes). Diferente de `To` (que é fim-do-dia inclusivo e serve para o filtro de UI); `before` é instante-exato para excluir a própria edição aberta e as posteriores. Ex.: `GET /api/changes?programId=PRG-001&before=2026-05-31T18:42:07.123Z&pageSize=10`.

**Como o front vai consumir:**
- `src/lib/api.ts`: tipar `change(id)` com o novo `ChangeDetailDto` (campos `diff`, `beforeCurve`, `afterCurve`); adicionar `before?: string` a `ChangeReportQuery` (passa por `qs()` como `before=...` sem mudança no helper).
- `src/lib/reports.ts`: trocar o tipo `ChangeDetail` (kind `program`) pelo novo formato — `diff: { summary, points: { index, status, before, after, changedFields }[] }` + `beforeCurve`/`afterCurve` (`ProfilePoint[]`). `at` passa a ser ISO (mantém-se a formatação no momento da exibição).
- `src/lib/reportsClient.ts`: `fetchChangeDetail` mapeia 1:1 o novo diff (sem o particionamento por `role` nem `pointsToProfile`); `fetchChanges` mantém `at` ISO para a tabela (a coluna formata na renderização) e o `EditionCompare` ganha o parâmetro `before`.
- `src/components/RelatoriosScreen/ChangeDetail.tsx`: `EditionCompare` busca `fetchChanges({ programId, before: change.at, ... })` (exclui as posteriores); o gráfico antes×depois é refeito em **um único** componente usando `beforeCurve`/`afterCurve` (aberta contínua, demais tracejadas); a tabela agrupa por `status` (`changed`/`added`/`removed`, ocultando `unchanged` ou mostrando-os esmaecidos) e destaca os `changedFields`.

**Dúvidas/decisões (backend owner):**
1. `beforeCurve`/`afterCurve` devem ser as curvas **interpoladas** (do `ReflowProgram.Profile`, ~até 1200 pts em 100 segmentos) ou só os **pontos do editor** do diff (até 100)? Para o gráfico antes×depois, os pontos do editor bastam e preservam o histórico — preferência do FE é esses.
2. Confirmar a definição de `summary.totalChanges` = `added + removed + changed` (excluindo `unchanged`) e que `summary.changedFields.{temp,timeSec,ramp}` conta **pontos** em que cada campo mudou (não ocorrências).
3. Para `Criado`/`Removido`, ok devolver `beforeCurve`/`afterCurve` como `[]` (ou omitir o campo)? O FE trata ausência/`[]` igualmente.
4. Parâmetro de anterioridade: nome `before` (preferência do FE) ou `maxDate`? E semântica **exclusiva** (`<`) confirmada, para não reincluir a edição aberta?
5. Manter `ChangePointRole`/o storage atual (consolidação só na leitura, **sem migração**) é aceitável, certo? Assim este item é puramente um refinamento de DTO + um filtro de query sobre o item H.

### ✅ 7. Usuário Master (dev/superusuário) + aba "Log" do Diagnóstico — **Feito**

> **Implementado no backend (2026-05-31)** (build + 35 testes verdes, **sem migração** — `UserType` é coluna `text` sem CHECK):
> - **Papel `Master`:** novo membro de `UserType` (wire `"Master"`, sem `[JsonStringEnumMemberName]`). O JWT já emite `role = Type.ToString()`, então o Master recebe `role:"Master"` sem mudança no `JwtTokenService`. As policies `AdminOnly` **e** `OperatorOrAdmin` passaram a aceitar `Master` (senão o Master tomaria 403 ao iniciar/parar execução) → herda **tudo** de Admin + (no front) a aba Log.
> - **Seed do Master (1 só, blindado):** `IMasterCredentials`/`MasterOptions` (config, seção `Master`, placeholder dev `dev.pandewilly`/`pandewilly`). `DbSeeder` cria o Master com guard idempotente próprio (`!Users.Any(Type==Master)`) — aparece também em bancos já semeados. `MustChangePassword=false` (nunca expira). `FactoryResetAsync` recria o Master após o wipe. `UserService` bloqueia: criar com `Type=Master` (400), editar/promover Master (403), remover Master (403), e **esconde** o Master da grade de Usuários (`ListAsync`). `Program.cs` faz **fail-fast** fora de Development se `Master:Password` ainda for o placeholder.
> - **Hub SignalR de log em tempo real:** `/hubs/systemlog` empurra `SystemLogDto` no evento **`SystemLogLine`** a cada nova entrada. `RunManager.FinalizeAsync` (Concluída→Info / Abortada→Aviso / Falha→Erro) e `SystemMonitorService.RaiseAsync` (servidor central / atualização / disco) agora gravam uma linha de `SystemLog` **no mesmo unit-of-work** (sem 2º commit) e empurram após o save (Id já populado). `ISystemLogSink` com `NullSystemLogSink` de fallback (Application) sobrescrito por `SignalRSystemLogSink` (Api). O front pode trocar o polling de 2s por assinatura SignalR (`conn.on("SystemLogLine", …)`), com o mesmo `SystemLogDto` que `GET /api/system-log` já retorna.
> - **Login do Master:** `dev.pandewilly` / `pandewilly` (dev). Em produção, defina `Master__Password` via env.

**Necessidade:** O front ganhou uma aba **Diagnóstico → Log** (visor de log em tempo real) que deve ser
visível **apenas** para um único usuário **Master** (dev) — "só vai ter ele e mais ninguém". O Master é um
superusuário (poderes de Admin + a aba Log). Hoje o backend só tem os papéis `Admin`/`Regular`
(`UserType`, `src/ReflowOven.Domain/Enums/Enums.cs:11-15`) e o claim de role no JWT
(`JwtTokenService.cs:34`), sem nenhum conceito de Master.

**Situação atual (front, já pronto):** `src/lib/api.ts` `Role = "Admin" | "Regular" | "Master"`; `src/lib/auth.ts`
ganhou `canAdminister()` (Admin **ou** Master) e `isMaster()`; a aba Log (`DiagnosticoLog.tsx`) só aparece
quando `session.role === "Master"`. O visor tem duas fontes: **Navegador** (buffer em memória do `logger`, não
depende do backend) e **Sistema** (`GET /api/system-log` por polling a cada `SYSTEM_LOG_POLL_MS` = 2s). O front
**já consome** o papel `Master` vindo no `SessionDto.role`/login — só falta o backend emiti-lo.

**Proposta (backend):**
1. **Papel Master no enum + JWT:** adicionar `Master` a `UserType` (`Enums.cs`) e emitir o claim `ClaimTypes.Role`
   = `"Master"` para esse usuário (`JwtTokenService`). O `SessionDto.Role`/`LoginResult` já serializam o role
   (`[JsonStringEnumMemberName]`), então o front recebe `"Master"` automaticamente. As policies `AdminOnly`
   devem **aceitar Master também** (ex.: `RequireRole(Admin, Master)` na policy `AdminOnly`, ou criar
   `MasterOnly` e fazer Admin-gates aceitarem os dois) — caso contrário o Master perde acessos de Admin.
2. **Seed do usuário Master (só ele):** criar no `DbSeeder` **um** usuário Master fixo (ex.: `dev.pandewilly`),
   senha definida em config/secrets (não hardcode em texto), **não** listável/編 editável pela tela de Usuários
   (ou ao menos não removível) e **não** criável pela UI (o `CreateUserRequest.Type` segue só Admin/Regular).
   Garantir idempotência (não duplicar em re-seed) e que ele sobreviva ao Reset de Fábrica (o reset "deixa 1
   admin + 1 programa" — manter também o Master).
3. **(Opcional, melhoria) Hub SignalR de push do log do sistema:** hoje a aba "Sistema" faz **polling** do
   `GET /api/system-log` (2s). Para tempo real de verdade, expor um hub (ex.: `/hubs/systemlog`) que empurra
   cada nova entrada de `SystemLog` (e idealmente as linhas do Serilog/HttpLogging) conforme são gravadas. O
   front então troca o polling por uma assinatura SignalR (como já faz em diagnostics/telemetry). Sem isso, o
   polling atual funciona — é só latência de ~2s.

**Como o front vai consumir:** assim que o login/`/api/auth/me` retornar `role: "Master"` para a conta dev, a
aba Log aparece sozinha (zero mudança adicional no front). Se o hub do item 3 existir, troco o `setInterval`
de `SystemLog` por `conn.on("SystemLogEntry", ...)`.

**Dúvidas/decisões (backend owner):**
1. Nome/credencial do usuário Master (o front citou `dev.pandewilly` / `pandewilly`) e onde guardar a senha
   (config/secret).
2. `AdminOnly` deve passar a aceitar `Master` (recomendado, para o Master herdar tudo de Admin) ou criar um
   `MasterOnly` separado e ajustar os gates?
3. Implementar o hub de push do log agora (tempo real) ou manter o polling de 2s por enquanto?
4. O log do **Navegador** é puramente client-side (efêmero). Se quiser que o log do dev seja **persistido**
   server-side além do `SystemLog`, é um item à parte — confirmar se é desejado.

## ⚠️ DEPLOY/DADOS: diff estruturado + curvas + `?before=` não estão sendo servidos (2026-05-31, 23h)

O **front já consome** o diff estruturado da Alteração (Validação #6 / bug #7 do front), mas a **instância
em execução** (`localhost:5248`) **não devolve esses campos** — o código-fonte os tem (`ReportDtos.cs`/
`ReportService.cs`), porém o build rodando responde no formato antigo. Verificado por `GET /api/changes/{id}`
em todos os registros semeados:

- `diff` = **null**, `beforeCurve` = **null**, `afterCurve` = **null** em toda Alteração (Criado/Editado/Removido).
- `GET /api/changes?before=<ISO>` é **ignorado**: `total` é o mesmo com e sem `before` (testado 41 == 41),
  então o filtro por data (front bug #7d — não listar edições posteriores à aberta) **não funciona** ainda.
- Os `points` ainda vêm com `role` uniforme antigo (`added`/`removed`/`changed-before`/`changed-after`),
  sem `unchanged` e sem `changedFields`.

**O que o backend precisa fazer (provável):** **rebuild + restart** da API com o código que popula
`Diff`/`BeforeCurve`/`AfterCurve` e aplica `ReportQuery.Before`; e **re-seed/recompute** das Alterações
existentes para que o diff por ponto seja calculado (os registros semeados são anteriores ao diff).

**Enquanto isso, o front não regride:** quando `afterCurve`/`beforeCurve` vêm nulos, o `reportsClient`
reconstrói a curva a partir dos pontos (gráfico continua aparecendo); quando `diff` vem nulo, as tabelas
caem no formato por `role`; e quando `?before=` é ignorado, a lista de edições degrada para "todas menos a
atual". Assim que o backend servir os campos, o **destaque por `changedFields`** (célula a célula, como no
Figma "Example Change Program"), a curva **antes×depois** real e o **filtro por data** acendem sozinhos —
sem mudança no front.

## ⚠️ DEPLOY/DADOS: `comparison` (Comparativo do Perfil) vem vazio em TODA execução (2026-06-01)

`GET /api/executions/{id}` devolve **`comparison: []`** em todos os registros — inclusive em runs
**Concluído** (testado: `c4e9f52b` Concluído, `056aae19` Abortado, `7c2a1f0d` Falha → todos 0 linhas). O
front renderiza a tabela "Comparativo do Perfil" a partir desse array (programado × medido por estágio,
com a coluna **Desvio**), então hoje a tabela nunca aparece.

**Front (já feito):** quando `comparison` está vazio, em vez de imprimir só o cabeçalho da tabela o front
mostra uma mensagem — **"Não houve desvio."** (run Concluído) ou **"Sem dados de comparação."**
(Falha/Abortado, pois aí o vazio é falta de dado, não ausência de desvio).

**O que o backend precisa fazer:** popular `comparison` (lista de `{ tempProg, tempReal,
timeProgSeconds, timeRealSeconds, stageIndex }`) ao finalizar/registrar a execução — comparando o perfil
programado com a trace medida por estágio. Quando vier preenchido, a tabela completa volta a aparecer
sozinha (sem mudança no front); o "Não houve desvio." passa a significar, de fato, um run sem desvio.

## ✅ Notificações "Limpar tudo" — endpoint já existe (2026-06-01)

A tela de Notificações ganhou um botão **"Limpar tudo"** que apaga todas as notificações do usuário (além
do mark-all-read automático ao abrir a tela, via `POST /api/notifications/read-all`). O front chama
**`DELETE /api/notifications`** — que **já existe e responde `204`** (confirmado em runtime). Nenhuma
mudança no backend é necessária; só registrando que o front passou a consumir esse endpoint (não remover).

(Opcional, se um dia quiser granular: `DELETE /api/notifications/{id}` para apagar uma só — o front não usa.)

## 🔧 PERFIL: começar a curva em 0 °C (não 25 °C ambiente) (2026-06-01)

Hoje o backend, ao montar o **profile amostrado** a partir dos **segments**, prepende um ponto inicial
`{ t: 0, temp: 25 }` (ambiente). Confirmado em runtime: `GET /api/programs/{id}` → `profile[0] = { t:0,
temp:25 }` mesmo o primeiro segment sendo 260 °C. Isso faz a primeira rampa ir de **25→primeiro ponto**.

**Decisão do dono:** a curva deve começar de **0 °C** — então "primeiro ponto 50 °C Linear" desenha a
rampa **0→50**, não 25→50. (Combina com o novo mínimo de ponto = 50 °C; o ponto-base de início é isento
desse mínimo.)

**O que o backend precisa fazer:** ao derivar o profile dos segments, prepender `{ t: 0, temp: 0 }` em vez
de `{ t: 0, temp: 25 }` (ajustar `DomainConstants`/serviço de programa que monta a curva). O front já
mudou o **preview** do editor para começar em 0 (`ProgramEditorScreen.START_TEMP = 0`).

**Atenção (mismatch temporário):** enquanto o backend ainda prepende 25, o **preview do editor mostra 0**
mas o **profile salvo/exibido** (cards da galeria, fundo do gráfico de execução) e o **run real** ainda
começam em 25. Some sozinho quando o backend prepender 0. (Obs.: comandar setpoint a partir de 0 °C é
incomum num forno real, que parte do ambiente — mas foi a decisão do dono; se preferir, dá pra tornar a
temperatura de partida um campo por-programa no futuro.)

## 🔧 NOTIFICAÇÕES EM TEMPO REAL: push via SignalR (2026-06-01)

Hoje o feed de notificações é só **REST polling** (`GET /api/notifications`, a cada 15s no front). O dono
quer que **assim que surge uma notificação, a TopBar atualize** o contador — sem esperar o tick nem dar
F5. Mitigações já feitas no front: (a) o feed é re-buscado **na hora** ao terminar uma execução (o badge
sobe na hora p/ "Execução concluída/abortada"); (b) o polling caiu de 30s → 15s.

**O que falta (backend) p/ tempo real de QUALQUER origem:** um hub **`/hubs/notifications`** (mesmo JWT
via `?access_token=`, como telemetry/diagnostics) que emita um evento **`Notification`** com o
`NotificationDto` (`{ id, kind, title, message, at, read }`) sempre que uma notificação for criada para
aquele usuário. O front então conecta e, ao receber, atualiza o store (some o polling, ou vira só
fallback). Enquanto o hub não existe, **não** conecto no front (evita conexão falhando e poluindo o
terminal do dev) — fica no polling de 15s + refresh ao fim da execução.

## 🔧 #8 — Soft-delete + Lixeira do Master (2026-06-01)

Decisão do dono: parar de apagar de verdade. **Users, Programs e Notifications** viram **soft-delete**;
só o **Master** vê uma "Lixeira" (aba própria em Configurações, só-Master) p/ restaurar/expurgar.

1. Colunas `isDeleted` (bool) + `deletedAt` (timestamp) + `deletedBy` (quem apagou) em Users, Programs,
   Notifications.
2. Toda listagem normal filtra `isDeleted = false` → telas comuns não mudam.
3. Os DELETE atuais viram **soft** (setam as flags), transparente pro front:
   `DELETE /api/users/{id}`, `DELETE /api/programs/{id}`, `DELETE /api/notifications` (Limpar tudo) e
   `DELETE /api/notifications/{id}` → marcam `isDeleted` em vez de remover.
4. Endpoints **MasterOnly** (403 p/ Admin/Regular) — o front **já tem os métodos** (`api.ts`), shapes:
   - `GET /api/users/deleted` → `(UserDto & { deletedAt: string; deletedBy: string })[]`
   - `GET /api/programs/deleted` → `(ProgramDto & { deletedAt; deletedBy })[]`
   - `GET /api/notifications/deleted` → `(NotificationDto & { deletedAt; deletedBy })[]`
   - `POST /api/{users|programs|notifications}/{id}/restore` → 204 (limpa as flags).
   - `DELETE /api/{users|programs|notifications}/{id}/purge` → 204 (apaga **definitivo/irreversível**).
5. Nome de usuário único **inclusive contra apagados** (recriar nome de apagado → conflito 409; o Master
   restaura). Manter a mensagem de conflito atual.
6. Auditoria ("Alterações") **protegida**: o front já removeu a categoria do DbCleanupModal; o endpoint
   `POST /api/maintenance/cleanup` deve **rejeitar/ignorar** a categoria `alteracoes`.

**Front (feito agora):** removi "Registro de alterações" da Limpeza + adicionei os métodos no `api.ts`
(`listDeleted*`/`restore*`/`purge*`). **Falta** a tela Lixeira — só monto quando os endpoints existirem
(pra não dar 404 nem poluir o terminal). Local: aba própria de Configurações, só-Master.

## 🔧 NOTIFICAÇÕES: quais eventos viram notificação + `kind` (2026-06-01)

Regra (alinhada com o dono): o **sino** é p/ eventos **assíncronos / que afetam o operador** — **não**
p/ ações que o próprio usuário acabou de fazer (essas já são **toast** + **Log de Alterações**). Evento → `kind`:

| Evento | `kind` |
|---|---|
| Execução **concluída** | `info` |
| Execução **abortada** | `warning` (âmbar — hoje vem como `error`; **trocar p/ `warning`**) |
| Execução com **falha** | `error` |
| **Falha/alerta da placa** (sobretemperatura, termopar aberto, sobrecorrente, tensão fora da faixa, ventoinha parada) | `error` (já vão pro relatório de Erros; **acender o sino também**) |
| **Atualização disponível (OTA)** | `update` |

**NÃO** viram notificação (já cobertos por toast + Log de Alterações): criar/editar/remover **programa**,
criar/remover **usuário**, mudar **configuração**.

Novo tipo **`warning`** no `NotificationDto.kind` → `"info" | "error" | "warning" | "update"`. O **front já
trata** `warning` (feed em âmbar com ícone de aviso + toast âmbar). O backend precisa:
1. Emitir `warning` (em vez de `error`) na notificação de **execução abortada**.
2. Criar notificação (`error`) para as **falhas/alertas da placa** (mesmos eventos do relatório de Erros).
3. (Opcional) Notificação `update` quando houver **atualização OTA** disponível.

## 🔧 LIMPEZA (Manutenção) ainda é mock-stage — migrar p/ deleção real (2026-06-01)

A tela "Limpeza do banco" hoje é **half-mock**: as **contagens** por categoria vêm de arrays mock no front
(`MOCK_EXECUTIONS`/`MOCK_CHANGES`/`MOCK_ERRORS`/`MOCK_LOGS` `.length`), os **tamanhos** são estimativas
(`BYTES_PER_RECORD`), e "limpar" seta uma **flag local** (`cleanupStore`) que esconde os registros nas telas
(Relatórios, modal de Log) — não apaga de verdade. `POST /api/maintenance/cleanup` é chamado, mas o efeito
real não é confirmado, e `GET /api/maintenance/overview` hoje volta vazio.

Pra esses mocks saírem do front e a Limpeza virar real, o backend precisa:
1. `GET /api/maintenance/overview` → contagens (idealmente bytes) reais por categoria:
   `{ execucoes, alteracoes, falhas, logs, inativos }` (ou reusar o `total` dos endpoints de relatório).
2. `POST /api/maintenance/cleanup` apagar **de verdade** as categorias escolhidas (server-side).

Aí o front troca as contagens mock por reais, dropa a flag `cleanupStore` (passa a re-buscar após limpar) e
remove `MOCK_EXECUTIONS`/`MOCK_CHANGES`/`MOCK_ERRORS`/`MOCK_LOGS`.

(Obs.: `MOCK_PROGRAMS` já era órfão → removido; `programs.ts` agora é só tipos. `MOCK_READINGS` é a leitura
inicial/placeholder do BottomBar antes do hub de diagnóstico — fica até decidirmos zerar o estado inicial.)
