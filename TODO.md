# TODO — bugs e melhorias

## ✅ Resolvidos

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
> edições e montar o gráfico multi-curva). Carga de CPU já vem em `SystemMetricsDto.CpuLoadPercent`
> (média dos núcleos) — falta só o front mostrar.

---

## Notas (histórico)

- A migration `AddFaultTypes` foi criada manualmente com:
  ```
  dotnet ef migrations add AddFaultTypes -p src/ReflowOven.Infrastructure -s src/ReflowOven.Api -o Persistence/Migrations
  ```
