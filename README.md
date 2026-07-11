# WSL Manager

Gerenciador de distribuições WSL2 para Windows — app nativo em **C# / WPF (.NET 10)** com visual Fluent via [WPF-UI](https://github.com/lepoco/wpfui), padrão MVVM via CommunityToolkit.Mvvm.

## O que a v2 faz

Interface com **NavigationView** (menu lateral) dividida em duas páginas — **Distros** e **Configuração** — mais uma página de detalhe por distro.

### Página Distros

- **Lista** todas as distros lendo o registro (`HKCU\...\Lxss`) — não acorda a VM
- Estado ao vivo (rodando/parada, atualizado a cada 5s, sem piscar a lista), **versão WSL**, **distro padrão** e **tamanho do ext4.vhdx**
- **Iniciar** (`wsl -d X -- true`) / **Encerrar** (`wsl --terminate`) por distro, abrir **terminal**
- **Desligar tudo** (`wsl --shutdown`) com confirmação — derruba a VM inteira
- Distros de sistema (docker-desktop etc.) com badge **sistema** e confirmação extra ao encerrar
- **⚠ alerta de disco** no card quando o vhdx passa do limiar configurável

### Nova distro (3 abas)

- **Catálogo**: parseia `wsl --list --online` e instala com `wsl --install -d <NAME> --no-launch`
- **De arquivo**: importa `.tar/.tar.gz/.tar.xz` (`wsl --import`) ou `.wsl` (`wsl --install --from-file`), escolhendo nome e pasta do vhdx (padrão `%LocalAppData%\WslManager\distros\<nome>`), validando colisão de nome
- **Clonar**: export + import + limpeza do `.tar` temporário, tudo como **uma** operação longa
- Pós-import: diálogo opcional de **usuário padrão** (useradd + sudo + `[user]` no `/etc/wsl.conf`)

### Página de detalhe da distro

- Metadados completos: estado, versão, tamanho, caminho base, caminho do vhdx, GUID
- **Recuperar espaço**: `wsl --manage <distro> --set-sparse true` (encerra antes se preciso) mostrando o tamanho em disco **antes/depois**
- **Exportar** backup: `wsl --export` com nome sugerido `<distro>_<yyyy-MM-dd>.tar`
- **Apagar** (`wsl --unregister`) com fluxo blindado: mostra o que será perdido, oferece exportar antes e só habilita ao digitar o nome exato — nunca disponível para distros de sistema

### Página Configuração

- **Preferências do app**: limiar do alerta de disco (persistido em `settings.json`)
- **Editor do `.wslconfig`**: `memory` (slider até a RAM física), `processors` (até `ProcessorCount`), `swap`, `vmIdleTimeout`, `autoMemoryReclaim`, `sparseVhd` e `networkingMode` (com `mirrored` desabilitado no Windows 10)
- Presets **Leve** / **Dev** (só preenchem o formulário), **Salvar** e **Salvar e reiniciar WSL**
- Parser INI **round-trip**: preserva comentários, ordem e seções/chaves desconhecidas; só altera o que foi editado; faz backup `.wslconfig.bak-<timestamp>` antes de gravar

### Operações longas

Export, import, install de catálogo, clone e compactação rodam **sem timeout**, com **janela de progresso cancelável** (mata o processo filho), tamanho do arquivo crescendo ao vivo e **trava por distro** (nunca duas operações longas simultâneas sobre a mesma).

## Requisitos

- Windows 10 21H2+ ou Windows 11, com WSL2 instalado
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Compila **apenas no Windows** (WPF)

## Rodar

```powershell
cd WslManager
dotnet run
```

## Testes

```powershell
cd WslManager.Tests
dotnet test
```

Cobrem o round-trip do parser de `.wslconfig` (comentários e seções desconhecidas sobrevivem a `load → set → save`).

## Publicar como .exe único

```powershell
dotnet publish -c Release
# saída: bin\Release\net10.0-windows\win-x64\publish\WslManager.exe
```

(Requer .NET 10 Desktop Runtime na máquina de destino; para um exe autocontido, mude `SelfContained` para `true` no `.csproj`.)

## Arquitetura

```
Core/                → interop pura, sem dependência de UI
  WslService.cs         ciclo de vida + criação/export/import/unregister/set-sparse
  Distro.cs             record imutável (snapshot de uma distro)
  OnlineDistro.cs       item do catálogo (wsl --list --online)
  LongOperationManager  trava por distro + progresso de operação longa
  WslConfigService.cs   parser INI round-trip + leitura/escrita do .wslconfig
  AppSettings.cs        preferências do app (settings.json)
  DiskUtil / SystemInfo tamanho em disco (sparse), RAM/CPU, suporte a mirrored
  ByteSize.cs           formatação de tamanhos
ViewModels/          → CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand])
  MainViewModel         lista, refresh (5s), ExecuteAsync + RunLongOperationAsync
  DistroViewModel       comandos por distro
  NewDistroViewModel    diálogo de criação (catálogo/arquivo/clone)
  WslConfigViewModel    editor do .wslconfig
  LongOperationViewModel janela de progresso cancelável
Views/               → XAML com WPF-UI (NavigationView, páginas e diálogos)
WslManager.Tests/    → xUnit (round-trip do parser)
```

Detalhes de interop tratados no `WslService`:

| Armadilha | Tratamento |
|---|---|
| `wsl.exe` escreve UTF-16LE | `WSL_UTF8=1` + `StandardOutputEncoding = UTF8` |
| Deadlock de buffer stdout/stderr | leitura assíncrona dos dois streams antes do `WaitForExit` |
| Console preto piscando | `CreateNoWindow = true` |
| Redirecionamento WOW64 de System32 | `PlatformTarget=x64` + caminho absoluto |
| Polling que acorda distros | só `--list --running` + registro (nenhum dos dois acorda nada) |
| Operações longas | `RunLongAsync` sem timeout, mata a árvore do filho ao cancelar |

## Roadmap

- **v3:** agente FastAPI dentro da distro publicando métricas (CPU/RAM/disco/processos) via push para o cliente WPF; helper de port forwarding (`netsh portproxy`); tray icon; caminho de compactação profunda via diskpart/Optimize-VHD (com elevação)
