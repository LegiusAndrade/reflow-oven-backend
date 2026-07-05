# Deploy na Pi (produção)

Artefatos do deploy real do backend no appliance (achados BE-1/BE-2/BE-3/BE-8 da auditoria):
antes disto a API só subia à mão num devcontainer (Development, chave JWT de dev, sem autostart),
o Postgres ficava exposto na LAN e a Pi bootava com o relógio errado por não ler o RTC.

| Arquivo | Papel |
| --- | --- |
| `install.sh` | Instalador idempotente (rodar como root **na Pi**, a partir da raiz do repo) |
| `reflow-backend.service` | Unit da API: publish self-contained em `/opt/reflow-oven/backend`, `Restart=always`, `Production` |
| `reflow-rtc.service` | Liga o RTC **ISL1208** (i2c-1 @ `0x6f`) → `/dev/rtc0` e restaura o relógio no boot |
| `backend.env.example` | Template do `/etc/reflow-oven/backend.env` (0600) — segredos reais ficam FORA do git |
| `reflow-front.service` | Template da unit do front Next.js (`:3000`) — instalar quando o front estiver buildado na Pi |

## Instalação

```bash
cd ~/reflow-oven-backend
sudo deploy/install.sh
# primeira vez: reboot único para o dtparam=i2c_arm=on valer e o RTC ser ligado
```

## Verificação

```bash
systemctl status reflow-backend        # active (running); journal: journalctl -u reflow-backend -f
curl -s http://127.0.0.1:5248/health   # Healthy
ls -l /dev/rtc0                        # RTC ligado
sudo hwclock -r -f /dev/rtc0           # hora do RTC
timedatectl                            # "RTC time" preenchido (não mais n/a)
ss -tlnp | grep 5432                   # Postgres SÓ em 127.0.0.1
```

## Decisões de projeto

- **Relógio (BE-8).** A fonte de verdade de parede é o **RTC ISL1208** (bateria), restaurado no boot
  pelo `reflow-rtc.service` (`Before=time-sync.target`); a unit do backend ordena
  `After=time-sync.target`. **Não** se espera NTP (uma bancada offline bootaria pendurada) — com NTP
  disponível o kernel re-disciplina o relógio e regrava o RTC sozinho (11-minute mode). No código, as
  durações de execução/autotune passaram a usar o relógio **monotônico** (`IClock.GetTimestamp`), então
  um salto de NTP/RTC não encerra uma queima mais cedo nem estica timeouts.
- **Segredos (BE-3).** `Production` arma os fail-fasts do `Program.cs`; os valores reais moram só em
  `/etc/reflow-oven/backend.env` (root, 0600), gerados pelo `install.sh`. Env overrides de senha seed
  valem no **primeiro** seed de um banco novo; num banco já semeado, troque as senhas pela UI (o
  técnico `calibracao` é o único verificado ao vivo contra a config).
- **Banco (BE-2).** `docker-compose.yml` publica o Postgres apenas em `127.0.0.1` e lê a senha de um
  `.env` fora do git; o `install.sh` gera uma senha forte e aplica com `ALTER USER`.
- **Sem TLS/porta LAN por padrão.** `ASPNETCORE_URLS=http://127.0.0.1:5248`: o front roda na própria
  Pi. Se um dia a UI sair da Pi, coloque um reverse proxy com TLS na frente — não abra o Kestrel na LAN.
