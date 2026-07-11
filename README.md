# WSL Manager

Gerenciador de distribuições WSL2 para Windows — app nativo em **C# / WPF (.NET 9)** com visual Fluent via [WPF-UI](https://github.com/lepoco/wpfui).

## O que o MVP faz

- **Lista** todas as distros lendo o registro (`HKCU\...\Lxss`) — não acorda a VM
- Mostra **estado** (rodando/parada, atualizado a cada 5s), **versão WSL**, **distro padrão** e **tamanho do ext4.vhdx**
- **Iniciar** (executa `wsl -d X -- true`) e **Encerrar** (`wsl --terminate`) por distro
- **Desligar tudo** (`wsl --shutdown`) com confirmação — derruba a VM inteira
- **Definir padrão** (`wsl --set-default`)
- Abrir **terminal** (Windows Terminal, com fallback) e **Explorer** (`\\wsl.localhost\...`)
- Distros de sistema (docker-desktop etc.) marcadas com badge e confirmação extra ao encerrar

## Requisitos

- Windows 10 21H2+ ou Windows 11, com WSL2 instalado
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- IDE: Visual Studio 2022 (17.12+) com workload ".NET desktop development", ou Rider — **no Windows** (WPF não compila no WSL)

## Rodar

```powershell
cd WslManager
dotnet run
```

Ou abra a pasta no Visual Studio e dê F5.

## Publicar como .exe único

```powershell
dotnet publish -c Release
# saída: bin\Release\net9.0-windows\win-x64\publish\WslManager.exe
```

(Requer .NET 9 Desktop Runtime na máquina de destino; para um exe totalmente autocontido, mude `SelfContained` para `true` no .csproj — fica ~90 MB.)

## Arquitetura

```
Core/
  WslService.cs     ← toda a interop: registro Lxss + wsl.exe
  Distro.cs         ← record imutável (snapshot de uma distro)
ViewModels/
  MainViewModel.cs  ← lista, timer de refresh (5s), execução centralizada de ações
  DistroViewModel.cs← comandos por distro (Wake/Terminate/SetDefault/abrir)
Views/
  MainWindow.xaml   ← UI Fluent (cards, badges, toolbar, status bar)
```

Detalhes de interop tratados no `WslService`:

| Armadilha | Tratamento |
|---|---|
| `wsl.exe` escreve UTF-16LE | `WSL_UTF8=1` + `StandardOutputEncoding = UTF8` |
| Deadlock de buffer stdout/stderr | leitura assíncrona dos dois streams antes do `WaitForExit` |
| Console preto piscando | `CreateNoWindow = true` |
| Redirecionamento WOW64 de System32 | `PlatformTarget=x64` + caminho absoluto |
| Polling que acorda distros | só `--list --running` + registro (nenhum dos dois acorda nada) |

## Roadmap

- **v2:** editor de `.wslconfig` (memory/processors/autoMemoryReclaim), compactar vhdx, export/import/clone com confirmação dupla para `--unregister`
- **v3:** agente FastAPI dentro da distro publicando métricas (CPU/RAM/disco/processos) via push para o cliente WPF; helper de port forwarding (`netsh portproxy`); tray icon
