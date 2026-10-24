# ArtDash

Sistema white-label multiempresa (multi-tenant) de pedidos, entregas com rastreamento e pagamento
online. Cada empresa (tenant) tem marca própria (logo, cores, fonte) e é identificada pelo host.
Arquitetura e layout derivados do `../artstore-fs` (mesmo autor): na dúvida sobre um padrão, veja
como foi feito lá.

Idioma: fale com o usuário em português. Código, identificadores e comentários em inglês; textos da
interface em pt-BR via `Strings.resx`; README e documentação em pt-BR.

## Estado atual (leia primeiro)

- A solução compila com o .NET 10.0.402 em Debug e Release. Há testes de domínio, aplicação e
  infraestrutura; esta última cobre o modelo SQL Server, a persistência dos agregados e o isolamento
  de gravações por empresa.
- Sem migrations: o banco é criado com `EnsureCreated` (aviso no log).
- Se ainda não houver git: `git init` e commit inicial antes de mudanças grandes.

### Próximos passos

1. `dotnet build ArtDash.sln` e corrigir até zero erros. Corrija na origem; não silencie com
   `#pragma`, `!` ou casts sem entender a causa. Leia os `.sarif` (abaixo) para a lista completa.
2. `dotnet test ArtDash.sln`, todos verdes.
3. Banco: `./scripts/dev-db.ps1` (cria banco e login no SQL Server local). Rodar a app e fazer a
   verificação manual.
4. Com o modelo estável: migration inicial (comando abaixo) e `./scripts/dev-db.ps1 -Reset`.
5. Mercado Pago em sandbox: credenciais de teste em Configurações › Pagamentos; o webhook precisa
   de URL pública (túnel) para receber notificações.

## Comandos

```powershell
./scripts/dev-db.ps1        # banco + login no SQL Server local; -AdminUser sa (login SQL); -Reset recria o banco
dotnet build ArtDash.sln
dotnet test ArtDash.sln
dotnet run --project src/ArtDash.UI --launch-profile https   # https://localhost:7180 | http://localhost:5180
dotnet ef migrations add Initial -p src/ArtDash.Infrastructure -s src/ArtDash.UI -o Persistence/Migrations
```

- Banco dev: instância local do SQL Server 2022 em `localhost,1433` (a mesma do artstore), banco
  `ArtDash_Dev`, login `artdash_dev` / `ArtDash#Dev2026!`, criados por `scripts/dev-db.ps1` (roda
  `docker/mssql/init.sql`). Connection string em `src/ArtDash.UI/appsettings.Development.json`.
  Sem terminal interativo (seu shell): `-Reset -Force` não pede confirmação; com `-AdminUser`, passe
  também `-AdminPassword` (senão o script pede a senha e trava).
- Docker: `Dockerfile` (app em HTTP 8080, `/healthz`) e `docker-compose.yml` com a aplicação e o cloudflared
  (perfil `tunnel`, `CLOUDFLARE_TUNNEL_TOKEN`); sem SQL Server: o banco é externo (`ARTDASH_CONNECTION_STRING`).
  Variáveis no `.env` (`.env.example`).
- Diagnósticos do compilador: todo build (CLI ou Visual Studio) grava
  `obj/Debug/net10.0/<Projeto>.sarif` em cada projeto (`Directory.Build.targets`).
- Logs da app: console e `src/ArtDash.UI/App_Data/logs/artdash-*.log`.
- Dev: API interativa em `/scalar`, saúde em `/healthz`, hub SignalR em `/hubs/realtime`.

## Acessos de teste (seed de `appsettings.Development.json`)

| Empresa | URL | Admin | Motorista | Cliente |
| --- | --- | --- | --- | --- |
| Distribuidora Demo | https://localhost:7180 | `admin@demo.local` / `Admin@12345` | `motorista@demo.local` / `Motorista@123` | `cliente@demo.local` / `Cliente@123` |
| Acme Bebidas | http://acme.localhost:5180 | `admin@acme.local` / `Admin@12345` | `motorista@acme.local` / `Motorista@123` | `cliente@acme.local` / `Cliente@123` |

Seed por empresa (`Infrastructure/Persistence/Seeding/SampleData.cs`): 3 categorias, 9 produtos,
3 veículos, 4 clientes (2 B2B faturados), 1 motorista e ~20 pedidos em cinco semanas, incluindo um
título vencido, a fila de hoje e uma rota em andamento com trilha GPS. "Hoje" é relativo à hora do seed.

## Arquitetura (`src/`)

- `ArtDash.Shared`: contratos (DTOs/requests), enums, constantes. Usado pela UI.Client; não depende de nada.
- `ArtDash.Domain`: entidades e regras (Catalog, Customers, Orders, Deliveries, Payments, Tenants,
  Identity), eventos de domínio. IDs com `Guid.CreateVersion7()`.
- `ArtDash.Application`: mediator próprio (`Common/Messaging`: `ISender`, `ICommand`/`IQuery` e
  handlers, pipeline de logging + FluentValidation). Um arquivo por feature em `Features/<Área>/` com
  query/command, validator e handler juntos. Portas em `Common/Interfaces` (`IApplicationDbContext`,
  `ICurrentUser`, `ISequenceGenerator`, `IPaymentGateway`...).
- `ArtDash.Infrastructure`: EF Core 10 SQL Server (`ApplicationDbContext`, configurations,
  interceptors de auditoria/tenant e de eventos de domínio pós-save), Identity + JWT, multi-tenancy,
  Mercado Pago, seed.
- `ArtDash.UI`: host ASP.NET Core. Controllers REST finos (enviam command/query via `ISender`; exceções
  viram ProblemDetails no `GlobalExceptionHandler`), hub SignalR, manifest PWA, Serilog.
- `ArtDash.UI.Client`: Razor class library com todos os componentes Blazor (InteractiveServer,
  prerender desligado, MudBlazor 9.11). As telas chamam a própria API REST pelos clientes tipados de
  `Services/Api/Apis.cs`, o mesmo contrato de apps externos. Regras de UI em
  `src/ArtDash.UI.Client/CLAUDE.md` (carregado ao mexer nessa pasta).
- `tests/`: xUnit + Shouldly (`ArtDash.Domain.Tests`, `ArtDash.Application.Tests`). Documentos
  válidos para teste: CPF 529.982.247-25, CNPJ 11.222.333/0001-81.

## Regras transversais

- Tenant pelo host: domínio próprio (match exato), depois subdomínio de `MultiTenancy:RootDomains`
  (= slug), depois `MultiTenancy:DefaultTenant`. Filtros globais do EF isolam por `TenantId` e o
  interceptor carimba tenant e auditoria: não atribua `TenantId` à mão nem use `IgnoreQueryFilters`
  sem motivo explícito.
- Datas em UTC no banco; dia/hora local pela timezone do tenant (`TenantClock`, `TenantTime`). Use o
  `TimeProvider` injetado, nunca `DateTime.Now`.
- Numeração de pedidos/entregas: `ISequenceGenerator.NextAsync(SequenceNames.Order, ct)` (ou
  `SequenceNames.Delivery`); o gerador salva o contexto.
- Perfis: Admin, Manager, Customer, Driver.
- Pagamento antes (online: crédito, débito e PIX via Mercado Pago Checkout Transparente) ou depois
  (na entrega, ou faturado com prazo e limite de crédito para clientes B2B). Segredos do gateway são
  só de escrita na UI.
- Central Package Management: versões só em `Directory.Packages.props` (alinhadas ao artstore-fs);
  csproj sem `Version`.
- Arquivos: siga o `.editorconfig` (UTF-8 com BOM, CRLF, 4 espaços; JSON/YAML com 2). `.sh` em LF.

## Rotas

- Loja: `/`, `/carrinho`, `/checkout`, `/meus-pedidos`, `/rastreio/{codigo}`; conta: `/entrar`, `/cadastro`.
- Painel: `/admin`, `/admin/pedidos` (`/quadro`, `/novo`), `/admin/entregas` (`/nova`),
  `/admin/rastreamento`, `/admin/recebiveis`, `/admin/produtos`, `/admin/categorias`,
  `/admin/clientes`, `/admin/veiculos`, `/admin/motoristas`, `/admin/usuarios`,
  `/admin/configuracoes/loja|pagamentos|marca`.
- Motorista (PWA): `/motorista`, `/motorista/entregas/{id}`.

## Verificação manual (depois de compilar)

- Admin: painel com indicadores; quadro de pedidos com a fila de hoje; novo pedido para cliente B2B
  faturado; plano de entrega; rastreamento ao vivo mostrando a rota em andamento.
- Motorista: rota do dia, concluir parada; a posição atualiza no mapa do painel.
- Cliente: loja, carrinho, checkout (na entrega e PIX), acompanhar a entrega.
- Marca: trocar cores/preset em Configurações › Marca e ver o preview; abrir a Acme e conferir que
  os dados das empresas não se misturam.
