# UI (Blazor + MudBlazor 9)

Regras para os componentes desta pasta e para os estáticos do host (`src/ArtDash.UI/wwwroot`). Complementa o `CLAUDE.md` da raiz.

- Render mode InteractiveServer, prerender desligado. Os componentes ficam na UI.Client (Razor class
  library); os estáticos dela são servidos em `_content/ArtDash.UI.Client/`.
- Nenhum texto fixo no markup: `L["Area.Chave"]` (`IStringLocalizer<Strings>` injetado como `L` no
  `_Imports.razor`). As chaves ficam em `Resources/Strings.resx` (pt-BR, fonte da verdade: edite o
  .resx direto, mantendo os placeholders `{0}`). Enums: `L.Enum(valor)` (chave `Enum.{Tipo}.{Valor}`);
  perfis: `L.Role(role)` (chave `Role.{Role}`). Enum ou valor novo exige as chaves novas.
- Carregar e salvar com `Ui.LoadAsync(...)` / `Ui.RunAsync(acao, mensagemDeSucesso)` (`UiActions` em
  `Services/Ui.cs`: trata `ApiException`, ProblemDetails e snackbar). Formatação pt-BR com `Fmt`
  (`Fmt.Money`, `Fmt.Date`, `Fmt.Phone`, `Fmt.Document`...).
- Chamadas à API só pelos clientes de `Services/Api/Apis.cs`; o `ApiClient` cuida do token (só em
  memória do circuito) e transforma respostas de erro em `ApiException`.
- MudBlazor 9: `Value`/`ValueChanged` (ou `@bind-Value`) em MudSwitch/MudSlider; diálogos com
  `DialogParameters<TDialog>`, `[CascadingParameter] IMudDialogInstance` e
  `if (await dialog.Result is { Canceled: false }) ...`. Confira a API da versão instalada antes de
  usar um componente novo.
- CS0542: o nome do arquivo .razor é o nome da classe e nenhum membro pode repeti-lo. Por isso as
  listas são `*List.razor` (`CustomerList`, `ProductList`...) e o carrinho é `ShoppingCart.razor`.
- Evite interpolação com aspas aninhadas dentro de atributos no markup; mova a expressão para um
  método no bloco `@code`.
- White-label: cores, raio e fonte vêm das variáveis CSS `--ad-*` (geradas em `Theming/BrandTheme.cs`)
  e da paleta do MudBlazor (`--mud-palette-*`; fonte por `--mud-typography-*-family`). Nada de cor fixa
  no CSS. O preview em Configurações › Marca usa `BrandingState` (Preview/Apply/CancelPreview).
- CSS global em `src/ArtDash.UI/wwwroot/css/app.css` (classes `ad-*`; app do motorista `dr-*`). Use
  `.razor.css` só para estilo exclusivo de um componente.
- JS em `src/ArtDash.UI.Client/wwwroot/js/` como módulos ES (auth, geo, lookup, map com Leaflet,
  mpCheckout, util), carregados pelos wrappers de `Services/BrowserServices.cs` e `Auth/` com
  `import("./_content/ArtDash.UI.Client/js/<arquivo>.js")`.
- Responsivo: a barra lateral some em telas estreitas (≤ 960 px em retrato, ≤ 600 px) e entra o
  `MobileBottomNav`. Teste as telas do motorista e da loja em largura de celular.
