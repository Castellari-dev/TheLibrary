# The Library

Gerenciador de coleção de Magic: The Gathering para Windows. Catalogue suas cartas por impressão, acompanhe o valor de referência em dólar e importe coleções inteiras a partir de CSV.

Aplicação desktop em WPF (.NET 8), com banco SQL Server ou PostgreSQL e dados de carta vindos da API pública do [Scryfall](https://scryfall.com).

---

## Recursos

**Coleção**
Grade com filtro por nome (inglês ou português), edição, tipo e código de set. Cada linha é uma impressão específica — a mesma carta em edições, idiomas, estados ou versões foil diferentes são registros separados. Painel lateral mostra a arte, o tipo, o artista e os preços da linha selecionada.

**Adicionar carta**
Busca no Scryfall por nome exato, com fallback para busca aproximada. A opção *Outros idiomas* traz impressões em todos os idiomas disponíveis. Escolha a arte exata que você tem, informe quantidade, estado e valor mínimo. Se a impressão já existir na coleção, o app oferece somar à quantidade atual em vez de duplicar.

**Importar CSV**
Aceita a exportação do LigaMagic e qualquer CSV com colunas equivalentes. Delimitador e codificação são detectados automaticamente. Cada linha é resolvida contra o Scryfall e classificada como exata, aproximada ou sem correspondência — as duvidosas podem ser corrigidas com duplo clique para escolher a arte na mão. A importação é seletiva: você marca o que entra.

**Preços**
Valor de mercado em USD puxado do Scryfall, com um valor mínimo próprio por impressão que você controla. A atualização em lote preenche os mínimos zerados com a cotação encontrada e preserva os que você já definiu. Quando uma impressão específica não tem cotação, o resolvedor procura em outras impressões da mesma carta.

**Multiusuário**
Login com senha em hash PBKDF2-SHA256 — senha nunca é gravada em texto puro. Tema e cor de destaque são preferências por usuário, salvas no banco e localmente.

**Aparência**
Tema claro e escuro com oito cores de destaque (verde, azul, roxo, vermelho, laranja, teal, grafite, rosa), trocáveis em tempo de execução sem reiniciar. A barra de título do Windows acompanha o tema.

---

## Requisitos

- Windows 10 (build 17763+) ou Windows 11
- Uma instância de **Microsoft SQL Server** ou **PostgreSQL** acessível
- Conexão com a internet para consultar o Scryfall

O executável é publicado *self-contained*: não é necessário instalar o .NET na máquina de destino.

---

## Primeira execução

O assistente de configuração inicial roda em três passos:

**1 — Banco de dados.** Escolha o provedor, informe host, porta, nome do banco e credenciais (ou marque *Autenticação do Windows* no SQL Server, ou informe a connection string manualmente). *Testar conexão* valida o acesso e cria o schema. *Criar banco se não existir* cuida do banco em si, caso ainda não exista.

**2 — Usuário administrador.** Gravado na tabela `APP_USER` do banco escolhido.

**3 — Aparência.** Tema e cor de destaque, com prévia ao vivo. Dá para mudar depois em Configurações.

A configuração local fica em:

```
%AppData%\TheLibrary\config.json
```

A connection string vive nesse arquivo. Se o banco tiver credenciais sensíveis, trate o arquivo como segredo.

---

## Build

```bat
build.bat
```

O script restaura os pacotes, compila e publica em arquivo único self-contained para `win-x64`.

Para desenvolvimento:

```bat
dotnet build
dotnet run --project TheLibrary
```

Se o build falhar logo depois de renomear o projeto ou trocar de branch, limpe os intermediários antes:

```powershell
Remove-Item -Recurse -Force .\TheLibrary\obj, .\TheLibrary\bin
```

---

## Estrutura

```
TheLibrary/
├── Models/          CardEntry, ScryCard, ImportRow, AppUser, enums
├── Services/
│   ├── ConfigService.cs      config.json local
│   ├── Database.cs           acesso a dados, schema, usuários
│   ├── ScryfallClient.cs     cliente da API do Scryfall
│   ├── PriceResolver.cs      resolução de preço com cache
│   ├── CsvParser.cs          leitura bruta (delimitador, encoding)
│   ├── CsvImporter.cs        mapeamento de colunas e resolução
│   ├── PasswordHasher.cs     PBKDF2-SHA256
│   ├── ThemeManager.cs       troca de tema e accent em runtime
│   ├── Session.cs            usuário e conexão da sessão
│   └── UiHelpers.cs          cursor de espera e diálogos
├── Themes/
│   ├── Light.xaml            paleta clara
│   ├── Dark.xaml             paleta escura
│   └── Styles.xaml           styles de controle (usa DynamicResource)
├── Views/
│   ├── SetupWindow           assistente inicial
│   ├── LoginWindow           autenticação
│   ├── MainWindow            abas Coleção / Adicionar / CSV / Configurações
│   ├── CardEditWindow        edição de impressão
│   ├── ArtPickerWindow       seleção de arte e edição
│   └── PasswordPromptWindow  definição de senha
└── build.bat
```

**Convenção de tema:** toda cor vem de `{DynamicResource}` apontando para as chaves das paletas (`Bg`, `Surface`, `SurfaceAlt`, `Line`, `Text`, `TextMuted`, `Accent`, `AccentText`, `Danger`, `Ok`, `Warn`). `StaticResource` só para referenciar styles (`{StaticResource Card}`, `{StaticResource GhostButton}`), que não mudam com o tema. Cor chumbada em XAML quebra a troca de tema.

---

## Todo

### Banco SQLite

Terceiro provedor, para quem quer rodar sem servidor. A base já está pronta: `DbProvider` é um enum e `ConnectionBuilder` já abstrai a montagem da string. O trabalho real é o dialeto SQL — `IDENTITY` vira `AUTOINCREMENT`, tipos de data e decimal mudam, e `CreateDatabaseIfMissing` deixa de ser um `CREATE DATABASE` para virar "garantir que o arquivo `.db` existe". Pacote: `Microsoft.Data.Sqlite`.

Vale como padrão para instalação nova — tira a barreira de precisar de um SQL Server só para catalogar cartas.

### Exportar para PDF

Relatório da coleção: lista com edição, quantidade, estado e valores, mais os totais. Uma segunda modalidade com as artes em grade seria útil para seguro ou venda, mas exige baixar e embutir as imagens, então o arquivo cresce rápido — vale deixar como opção separada.

`QuestPDF` é a escolha mais confortável em .NET moderno (API fluente, licença Community gratuita abaixo do teto de faturamento). Alternativa: `iText`, mais pesado e com licença AGPL.

### Adicionar idiomas

Hoje todo texto de interface está chumbado em português, espalhado entre o XAML e as mensagens no code-behind (`Dialogs.Warn("Selecione uma carta.")`, textos de status, headers de coluna).

O caminho de menor atrito aqui é reaproveitar o padrão que o `ThemeManager` já usa. A mecânica é idêntica:

1. Criar `Lang/pt-BR.xaml` e `Lang/en-US.xaml` como `ResourceDictionary`, com uma `sys:String` por texto:
   ```xml
   <sys:String x:Key="Str_SelectCard">Selecione uma carta.</sys:String>
   ```
2. No XAML, trocar `Text="Coleção"` por `Text="{DynamicResource Str_TabCollection}"`.
3. Um `LocalizationManager` que troca o dicionário em `Application.Current.Resources.MergedDictionaries`, exatamente como o `ThemeManager` troca a paleta. Como tudo é `DynamicResource`, a interface reflete a troca sem reiniciar.
4. Para o code-behind, um helper `L.Get("Str_SelectCard")` que faz `Application.Current.TryFindResource`.
5. Guardar o idioma escolhido em `config.json` e na tabela `APP_USER`, junto de tema e accent.

A alternativa canônica é RESX + `x:Static`, mas ela resolve no parse do XAML e não troca em runtime sem recriar as janelas — o que seria um retrocesso em relação ao que o app já faz com temas.

**Cuidado com a ambiguidade:** "idioma" já significa duas coisas aqui. Existe o idioma da *carta* (a coluna Idioma, `ScryfallClient.LangToDisplay`, o filtro *Outros idiomas*), que já funciona. O item acima é o idioma da *interface*. Convém separar os nomes no código e na UI antes que vire confusão — algo como `UiCulture` versus `CardLang`.

### Versão mobile

O maior obstáculo não é a UI, é a arquitetura de acesso a dados. Hoje o app fala direto com o SQL Server, o que não é aceitável a partir de um celular fora da rede local — credenciais de banco no dispositivo e porta 1433 exposta são um problema de segurança, não de conveniência. O caminho é uma API entre os dois.

Passo preparatório que já vale por si só: extrair `Models/` e a parte agnóstica de `Services/` (`ScryfallClient`, `PriceResolver`, `CsvParser`, `CsvImporter`, `PasswordHasher`) para um projeto `TheLibrary.Core` em `net8.0`. Ficam de fora `ThemeManager`, `UiHelpers` e `Session`, que dependem de WPF.

Com o núcleo separado, tanto MAUI quanto Avalonia ficam viáveis — Avalonia tem a vantagem de reaproveitar o conhecimento de XAML e o vocabulário de styles que já existe em `Themes/`.

---

## Créditos

Dados de cartas, imagens e preços fornecidos pela [API pública do Scryfall](https://scryfall.com/docs/api). Preços são referência de mercado em dólar e não constituem cotação oficial.

Este projeto não é afiliado, endossado ou patrocinado pela Wizards of the Coast. Magic: The Gathering é marca registrada da Wizards of the Coast LLC.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)