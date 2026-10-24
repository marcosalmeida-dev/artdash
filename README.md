# ArtDash

Plataforma **white-label** de pedidos e entregas: loja online, painel da empresa (pedidos, cadastros,
contas a receber), planejamento de rotas e **app do motorista** com rastreamento em tempo real.
Cada empresa (tenant) tem a própria marca — nome, logotipos, cores e fonte — e é acessada pelo seu
domínio ou subdomínio.

Arquitetura e organização de componentes herdadas do **artstore-fs** (.NET 10, Blazor Server +
MudBlazor, Clean Architecture), refeitas para o novo domínio.

## Funcionalidades

| Área | O que tem |
| --- | --- |
| Loja | Catálogo por categorias, busca, carrinho, checkout com endereço (CEP via ViaCEP), pagamento antes ou depois, "meus pedidos", página pública de rastreio com o veículo no mapa |
| Pagamento | Mercado Pago Checkout Transparente (PIX com QR Code, cartão de crédito e débito), pagamento na entrega (registrado pelo motorista) e **faturado** (B2B, com prazo, limite de crédito e contas a receber) |
| Pedidos | Quadro kanban em tempo real (SignalR), lista com filtros, pedido manual pelo atendimento, histórico de status, pagamentos parciais |
| Relatórios | Vendas por dia, status e canal; clientes e produtos em destaque; recebimentos, saldos e atrasos dos pedidos; desempenho das entregas e motoristas, com período e canal |
| Entregas | Planejamento de rotas (ordem das paradas, motorista, veículo, alerta de capacidade), mapa ao vivo da frota, trilha GPS de cada rota |
| Motorista | PWA instalável: rotas do dia, iniciar/encerrar, GPS contínuo com wake lock, navegação (Google Maps/Waze), comprovação de entrega e recebimento na porta |
| Cadastros | Produtos (foto, preço "de", unidade, peso), categorias, clientes (PF/PJ, faturamento), veículos, motoristas, usuários |
| Marca | Editor de identidade visual com pré-visualização ao vivo em todo o app; manifest PWA e ícones por empresa |
| Usuários | Perfis Administrador, Gerente, Cliente e Motorista; JWT com refresh token em cookie HttpOnly |

## Arquitetura

```
src/
  ArtDash.Shared          DTOs, enums e constantes compartilhados por servidor e UI
  ArtDash.Domain          Entidades e regras de negócio (Order, Delivery, Tenant...), sem EF Core
  ArtDash.Application     Casos de uso (commands/queries), validadores, portas; mediador próprio
  ArtDash.Infrastructure  EF Core (SQL Server), Identity + JWT, multi-tenancy, Mercado Pago, arquivos, seed
  ArtDash.UI.Client       Componentes Blazor (InteractiveServer), páginas, serviços de UI, JS
  ArtDash.UI              Host web: API REST (controllers), hub SignalR, Blazor host, OpenAPI
tests/
  ArtDash.Domain.Tests        Regras dos agregados
  ArtDash.Application.Tests   Validadores e calendário por fuso da loja
  ArtDash.Infrastructure.Tests   Modelo EF, persistência e isolamento de gravações por empresa
```

- **Multi-tenant** por host: domínio cadastrado (`pedidos.empresa.com.br`) ou subdomínio de um domínio raiz
  (`empresa.artdash.com.br`). Filtros globais do EF isolam os dados de cada empresa.
- A UI chama a própria API REST (mesmo contrato usado por apps externos); o token de acesso fica só na memória do circuito.
- Tempo real: hub `/hubs/realtime` (quadro de pedidos, mapa ao vivo, rastreio do cliente).

## Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server 2022 local em `localhost,1433` (Developer ou Express), com TCP/IP habilitado e autenticação
  mista (SQL Server e Windows), ou outra instância acessível pela rede.

## 1. Banco de dados de desenvolvimento

O banco e o login da aplicação são criados na instância do SQL Server que já roda na máquina:

```powershell
./scripts/dev-db.ps1                            # autenticação do Windows
./scripts/dev-db.ps1 -AdminUser sa              # login SQL de administrador (a senha é pedida)
./scripts/dev-db.ps1 -Server "localhost\SQLEXPRESS"   # outra instância
```

O script é idempotente (pode rodar de novo a qualquer momento) e cria ou atualiza:

| Item | Valor |
| --- | --- |
| Servidor | `localhost,1433` |
| Banco | `ArtDash_Dev` (collation `Latin1_General_100_CI_AI`: buscas sem distinção de acentos) |
| Login da aplicação | `artdash_dev` / `ArtDash#Dev2026!` (db_owner do banco; dbcreator, só em desenvolvimento) |

A connection string de desenvolvimento já está em `src/ArtDash.UI/appsettings.Development.json`.
Para outro banco, login ou senha, passe `-Database`, `-Login` e `-Password` ao script e ajuste a connection
string (ou use `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project src/ArtDash.UI`).

Recomeçar do zero (apaga todos os dados do `ArtDash_Dev`; pede confirmação, `-Force` pula): `./scripts/dev-db.ps1 -Reset`.

## 2. Rodar a aplicação

```powershell
dotnet run --project src/ArtDash.UI --launch-profile https
```

- App: <https://localhost:7180> (ou <http://localhost:5180>)
- API (documentação interativa, só em desenvolvimento): <https://localhost:7180/scalar>
- Saúde: `/healthz`

Na primeira execução a aplicação cria as tabelas e os **dados de exemplo** das empresas configuradas em
`Seed` (appsettings.Development.json).

### Acessos de teste

| Empresa | Endereço | Perfil | E-mail | Senha |
| --- | --- | --- | --- | --- |
| Distribuidora Demo | <https://localhost:7180> | Administrador | `admin@demo.local` | `Admin@12345` |
| | | Motorista | `motorista@demo.local` | `Motorista@123` |
| | | Cliente | `cliente@demo.local` | `Cliente@123` |
| Acme Bebidas (outra marca) | <http://acme.localhost:5180> | Administrador | `admin@acme.local` | `Admin@12345` |
| | | Motorista | `motorista@acme.local` | `Motorista@123` |
| | | Cliente | `cliente@acme.local` | `Cliente@123` |

> `*.localhost` aponta para a própria máquina no Chrome, Edge e Firefox. Use `http` (porta 5180) para os
> subdomínios: o certificado de desenvolvimento HTTPS cobre só `localhost`. Alternativa: `http://acme.lvh.me:5180`.

### O que vem nos dados de exemplo

Por empresa: 3 categorias, 9 produtos, 3 veículos, 4 clientes (dois B2B faturados com prazo e limite de
crédito), 1 motorista e cerca de 20 pedidos espalhados por cinco semanas — pagos online, recebidos na
entrega, faturados em aberto, **um título vencido**, um cancelado, a fila de hoje (aguardando pagamento,
aguardando confirmação, confirmado, em separação, pronto) e **uma rota em andamento** com trilha GPS: o
pedido da cliente de teste é a próxima parada (veja em *Meus pedidos › Acompanhar entrega* e no
*Rastreamento ao vivo* do painel).

A parte "de hoje" é relativa ao momento em que o seed rodou. Para atualizar, recrie o banco
(`./scripts/dev-db.ps1 -Reset`) e rode a aplicação de novo.

### Relatórios do painel

Administradores e gerentes acessam **Relatórios** em `/admin/relatorios`. O período inclui as datas
inicial e final no fuso da empresa, começa nos últimos 30 dias e pode abranger até 366 dias. O filtro
de canal permite analisar os pedidos feitos na loja ou pelo atendimento.
As seis abas apresentam visão geral, pedidos, clientes, produtos, financeiro e entregas. **Exportar CSV**
baixa todos os indicadores do período e canal aplicados; os rankings mostram os 10 primeiros resultados.

Os indicadores de vendas e os rankings de clientes e produtos desconsideram pedidos cancelados.
Vendas e ticket médio usam o total dos pedidos, incluindo frete e desconto; o ranking de produtos
usa o valor bruto dos itens e preserva o nome e o preço registrados na venda. Clientes novos e
recorrentes são classificados pelo histórico de compras válidas, inclusive fora do período.

Os valores recebidos, em aberto e vencidos referem-se aos pedidos criados no período escolhido,
mesmo quando o pagamento ocorreu em outra data. São uma análise dessa carteira de pedidos; para
cobrança de todos os títulos, use **Contas a receber**. Atrasos consideram o vencimento anterior ao
dia atual da empresa. As entregas são analisadas pela data agendada e não recebem o filtro de canal.
As rotas canceladas aparecem na contagem de rotas, mas suas paradas não entram nos indicadores
de tentativas de entrega.

## Docker e Cloudflare Tunnel

`Dockerfile` (imagem da aplicação, HTTP na porta 8080, healthcheck em `/healthz`) e `docker-compose.yml` com a
aplicação (`artdash`, em <http://localhost:8080>, só no localhost) e o Cloudflare Tunnel (`cloudflared-artdash`,
perfil `tunnel`). O banco é um SQL Server externo, em `ARTDASH_CONNECTION_STRING`; numa instância do próprio
host, use `Server=host.docker.internal,1433;...` (exemplo no `.env.example`).

```powershell
Copy-Item .env.example .env        # connection string, ARTDASH_JWT_SIGNING_KEY, admin, domínio e token do túnel
docker compose up -d --build                        # só a aplicação
docker compose --profile tunnel up -d --build       # aplicação + túnel
docker compose logs -f artdash
```

- `COMPOSE_PROFILES=tunnel` no `.env` faz `docker compose up -d` subir também o túnel.
- A primeira empresa e o administrador vêm de `ARTDASH_TENANT_*` e `ARTDASH_ADMIN_*` (criados só se ainda não
  existirem). Logs e uploads ficam no volume `artdash-appdata`; as chaves de proteção de dados, no banco.

**Cloudflare Tunnel.** No painel Zero Trust › Networks › Tunnels, crie um túnel do tipo *cloudflared*, copie o
token para `CLOUDFLARE_TUNNEL_TOKEN` e, em *Public Hostnames*, aponte o domínio para o serviço `HTTP`
`artdash:8080`. Para várias empresas por subdomínio, use um hostname curinga (`*.seudominio.com.br`) e
`ARTDASH_ROOT_DOMAIN=seudominio.com.br`; para um domínio próprio de uma empresa, cadastre o host dela. Defina
`ARTDASH_PUBLIC_URL` com o endereço público HTTPS (webhook do Mercado Pago). O HTTPS termina na Cloudflare; a
aplicação confia nos `X-Forwarded-*` só da rede do compose (`ARTDASH_NETWORK_SUBNET`), o que mantém o IP real
do cliente nos limites de requisição. Blazor Server e SignalR usam WebSocket, habilitado por padrão na Cloudflare.
O túnel também serve no desenvolvimento: `docker compose up -d --no-deps cloudflared-artdash` sobe só o túnel;
aponte o hostname para `http://host.docker.internal:5180` (a `dotnet run`), útil para receber webhooks do
Mercado Pago em sandbox.

## Migrations

Enquanto não houver migrations, a aplicação cria o schema com `EnsureCreated` (aviso no log). Antes de
publicar, gere a migration inicial:

```powershell
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial -p src/ArtDash.Infrastructure -s src/ArtDash.UI -o Persistence/Migrations
./scripts/dev-db.ps1 -Reset   # o banco criado por EnsureCreated não tem histórico de migrations
```

Depois disso a aplicação aplica as migrations pendentes ao iniciar. O `dotnet ef` usa a connection string
do appsettings do host (ou a variável `ARTDASH_CONNECTION`).

## Mercado Pago

1. Crie uma aplicação em *Mercado Pago › Suas integrações* e pegue as credenciais de **teste**.
2. No painel: *Configurações › Pagamentos* — Public key, Access token e (opcional) assinatura do webhook.
3. Pague com os [cartões de teste](https://www.mercadopago.com.br/developers/pt/docs/checkout-api/integration-test/test-cards)
   ou PIX de teste.

Sem webhook o pagamento é confirmado pela consulta automática da página de pagamento. Para receber
webhooks em desenvolvimento, exponha a aplicação em HTTPS público (ex.: `devtunnel host -p 7180` ou ngrok),
configure `App:PublicBaseUrl` e cadastre a URL exibida em *Configurações › Pagamentos*.

## Testes

```powershell
dotnet test
```

Os testes usam xUnit v3 com a variante `xunit.v3.mtp-off`, que mantém o executor VSTest e a
compatibilidade com `dotnet test`, o adaptador do Visual Studio e `coverlet.collector`. Para gerar
relatórios de cobertura:

```powershell
dotnet test ArtDash.sln --collect:"XPlat Code Coverage" --results-directory artifacts/test-results
```

Para gerar os arquivos de publicação:

```powershell
dotnet publish src/ArtDash.UI -c Release -o artifacts/publish
```

## Configuração (appsettings)

| Seção | Uso |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server |
| `Jwt` | Emissor, audiência, chave (≥ 32 caracteres) e validade dos tokens |
| `MultiTenancy` | `DefaultTenant` (fallback, só dev) e `RootDomains` (subdomínio = slug da empresa) |
| `Storage` | Pasta dos uploads (logos, fotos) e caminho público |
| `App:PublicBaseUrl` | Endereço HTTPS público (URLs de webhook) |
| `Api:BaseUrl` | Endereço interno pelo qual os circuitos Blazor chamam a API (loopback) |
| `Seed` | Empresas, administradores e dados de exemplo criados ao iniciar |
| `ForwardedHeaders` | Atrás de proxy reverso (IP real do cliente, esquema e host) |
| `Features:ApiReference` | Publica OpenAPI + Scalar fora de desenvolvimento |

Em produção, informe os segredos por variáveis de ambiente (`ConnectionStrings__DefaultConnection`,
`Jwt__SigningKey`), mantenha `Seed:Enabled=false` (ou só com a empresa inicial) e `MultiTenancy:DefaultTenant` vazio.
