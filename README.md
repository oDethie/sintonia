<p align="center">
  <img src="docs/icone.png" width="96" alt="Ícone do Sintonia">
</p>

<h1 align="center">Sintonia</h1>

<p align="center">
  Controle de som na bandeja do Windows: <b>saídas, microfones, volume de cada app e em qual saída cada app toca</b>.<br>
  Bonito, leve e sem instalação, para Windows 10 e 11.
</p>

---

## Por que existe

O controle de volume do Windows esconde o que mais importa: trocar a saída (fone, caixa, monitor), mudar o volume de **cada app** e mandar um app específico tocar em outra saída (ex.: o jogo no fone e o Discord na caixa). O Sintonia junta tudo isso num painel só, ao lado do relógio, no visual do Windows 11 e com o mínimo de consumo.

## Recursos

- **Painel na bandeja** com todas as saídas e microfones, cada um com volume e mudo.
- **Volume e mudo de cada app**, agrupados na saída onde o app está tocando.
- **Saída de cada app:** arraste o app para outra saída, ou use o **⋯** / clique direito no app. A escolha é **garantida**: se o Windows "esquecer", o Sintonia coloca o app de volta na saída certa.
- **Usar como padrão** com um botão próprio (sem trocar por engano). A saída padrão tem uma barrinha discreta na lateral.
- **Organize do seu jeito:** arraste as saídas para mudar a ordem, recolha/expanda com a setinha, renomeie ("Fone", "Caixa", "Monitor") e esconda o que não usa (o olhinho mostra os escondidos).
- **Perfis** (opcional, até 10): guardam a saída padrão, o microfone, a saída e o volume de cada app e como o painel está. Troque pela lista **Perfil** no painel (abre ali mesmo, com o ativo marcado; arraste para mudar a ordem), pelo menu do ícone ou por atalho. Podem **ativar sozinhos** quando um app (ex.: um jogo) começa a tocar e voltar ao anterior quando ele fecha.
- **Atalhos de teclado** configuráveis (vêm vazios; escolha os seus): abrir o painel, próxima saída, mudo geral e um por perfil.
- **Aviso na tela** discreto ao usar os atalhos (não aparece por cima de jogos em tela cheia).
- **Som tocando** (opcional): barrinhas animadas ao lado de cada saída e app mostram o som que está tocando, só com o painel aberto.
- **Controlar outro PC da casa** (opcional, novo na 1.1): pareie o PC e o notebook com um código e mude o som de um pelo painel do outro. Veja [Outros PCs](#outros-pcs).
- **Atualiza sozinho** (opcional, vem ligado): confere no GitHub a cada 12 h, baixa a versão nova, confere a **assinatura digital** e se atualiza quando você não está jogando em tela cheia. Veja [Atualizações](#atualizações).
- **Tema claro/escuro** que segue o Windows, cantos arredondados, animações suaves (opcionais).
- Funciona com monitores em **escalas diferentes** (ex.: 100% e 175%) e com a **barra de tarefas** fixa, que se esconde sozinha ou em qualquer lado da tela.
- **Configurações** numa janela própria (Geral, Perfis, Atalhos, Dispositivos, Outros PCs), com tudo podendo ser ligado ou desligado.

## Como instalar

1. Baixe o **`Sintonia.exe`** na página de [**Releases**](../../releases/latest).
2. Abra. Ele pergunta se quer instalar: clique em **Instalar**. Pronto: ele fica em `%LOCALAPPDATA%\Programs\Sintonia`, aparece no menu Iniciar e em *Configurações ▸ Aplicativos*, e passa a **iniciar com o Windows** (dá para desligar em Configurações ▸ Geral). Não precisa de administrador.
3. Para atualizar, o Sintonia se atualiza sozinho; ou abra um `Sintonia.exe` mais novo e clique em **Atualizar**.

> **Aviso do Windows (SmartScreen):** como o programa é novo e não tem assinatura digital paga, o Windows pode mostrar *"O Windows protegeu o computador"*. Clique em **Mais informações ▸ Executar assim mesmo**.
>
> **Antivírus:** alguns antivírus desconfiam de programas pequenos e desconhecidos. O código está todo aqui no repositório para quem quiser conferir; se for bloqueado, adicione uma exceção para o `Sintonia.exe`.

Requisitos: Windows 10 (versão 2004 ou mais nova) ou Windows 11. O .NET Framework 4.8 necessário já vem no Windows.

## Como usar

| Onde | O que faz |
|---|---|
| **Clique no ícone** | Abre/fecha o painel |
| **Barra de volume** | Arraste para mudar o volume (a roda do mouse também funciona, se ligada) |
| **Ícone da saída / do app** | Liga/desliga o mudo |
| **Arrastar um app** | Manda o app tocar em outra saída |
| **Arrastar o nome de uma saída** | Muda a ordem da lista |
| **Setinha** | Recolhe/expande a saída |
| **Botão "Usar como padrão"** (aparece com o mouse em cima) | Deixa a saída como padrão do Windows |
| **Clique direito numa saída / app / perfil / PC** | Opções daquele item (no PC: reconectar, renomear, esquecer) |
| **Mouse sobre uma barra + setas ← →** | Muda o volume de 1 em 1 |
| **Olhinho** ao lado de "Saída" | Mostra as saídas escondidas para trazê-las de volta |
| **Engrenagem** | Abre as Configurações |
| **Clique direito no ícone** | Menu rápido: saída, microfone, mudo, perfis, Configurações, Sair |

### Outros PCs

Para mudar o som do notebook pelo painel do PC (ou o contrário):

1. No PC que **vai ser controlado**: Configurações ▸ Outros PCs ▸ ligue **Pode ser controlado** e clique em **Gerar código**. Na primeira vez o Windows pergunta se libera o Sintonia na rede: marque só **Redes privadas**.
2. No PC que **vai controlar**: Configurações ▸ Outros PCs ▸ ligue **Controlar outros PCs**, clique em **Adicionar um PC**, escolha o PC e digite o código (8 letras e números, vale 2 minutos). Se ele não aparecer na lista, use **Digitar o endereço do PC…** com o endereço mostrado no outro PC, logo abaixo de "Pode ser controlado".
3. Pronto: o topo do painel ganha uma barra com **Este PC** e o nome do outro PC (até 2 outros PCs). Clique no outro para ver e mudar as saídas, os volumes, o mudo e a saída de cada app de lá. Os perfis são deste PC e só aparecem em "Este PC".

Cada PC pode ser controlador, controlado ou os dois, e dá para trocar quando quiser. Tudo vem **desligado**.

No PC que está sendo controlado, o ícone do Sintonia perto do relógio ganha uma **bolinha** enquanto o outro PC está conectado, e o painel dele mostra "*Nome do PC* está controlando este PC". Quando o outro PC muda um volume, o ícone mostra por um instante o **app ou a saída** que mudou, com uma **seta para cima ou para baixo** (verde subindo, amarela descendo, **X** vermelho no mudo).

**Segurança:**
- Só conecta na **rede de casa** (endereços privados da mesma rede); pedidos de fora são recusados antes de qualquer coisa.
- Só PCs **pareados** conversam. O código vale **uma vez**, por **2 minutos** e **3 tentativas**. Ele nunca vai pela rede: os dois lados só provam que o conhecem.
- Cada PC tem uma **identidade própria** (chave ECDSA P-256 guardada pelo Windows, que não sai do PC). A cada conexão os dois provam quem são e combinam chaves novas (ECDH P-256).
- Tudo vai **criptografado** (AES-256 + HMAC-SHA256). Mensagens alteradas ou repetidas são recusadas.
- O PC controlado só aceita mudar volume, mudo, saída padrão e saída de apps. Não abre arquivos, não roda programas, não lê nada além da lista de som.
- O teste do protocolo pode ser rodado por qualquer um: `Sintonia.exe --testerede resultado.txt`.

**Desempenho:** nada é enviado no dia a dia. A conexão só abre quando o painel mostra o outro PC (uma atualização a cada 3 s) e fecha junto com o painel. Com "Pode ser controlado" ligado, o Sintonia fica só esperando, parado: **0 ms de CPU** e a mesma memória.

### Atualizações

A partir da 1.4 o Sintonia se atualiza sozinho em todos os PCs (dá para desligar em Configurações ▸ Geral ▸ Atualizações):

- A cada 12 horas (e 90 s depois de abrir) ele pergunta ao GitHub qual é a última versão. Não manda nada sobre você nem sobre o PC.
- Se houver versão nova, baixa o `Sintonia.exe` e a assinatura `Sintonia.exe.sig` da página de Releases e **confere a assinatura digital** (ECDSA P-256). A chave que assina fica só no PC de quem publica; a parte pública está no código. Um arquivo alterado, mesmo em 1 byte, é recusado.
- Instala só quando não atrapalha: sem o painel ou menu abertos e **sem jogo em tela cheia** (senão tenta de novo em 5 minutos). Troca o `.exe`, reabre a versão nova e mostra um aviso rápido.
- Desligado o "Atualizar sozinho", a versão nova aparece no menu do ícone e em Configurações para você instalar quando quiser.

### Linha de comando (opcional)

| Comando | O que faz |
|---|---|
| `Sintonia.exe --perfil Nome` | Ativa um perfil (útil para atalhos e scripts) |
| `Sintonia.exe --config 0` | Abre as Configurações (0 Geral, 1 Perfis, 2 Atalhos, 3 Dispositivos, 4 Outros PCs) |
| `Sintonia.exe --diagnostico arquivo.txt` | Lista saídas, apps e a saída de cada app (não muda nada) |
| `Sintonia.exe --testerede arquivo.txt` | Testa o pareamento e a criptografia de Outros PCs dentro do próprio PC |
| `Sintonia.exe --apagar-identidade` | Apaga a identidade deste PC usada em Outros PCs (os pareamentos param de valer) |

## Privacidade

- **Quase não usa internet:** só a consulta de atualização ao GitHub (a cada 12 h, desligável), que não envia nada sobre você. **Outros PCs** vem desligado e só fala com PCs pareados da sua rede de casa.
- Os atalhos usam o recurso oficial do Windows (`RegisterHotKey`). O programa **não lê o que você digita** nem vigia o mouse fora do painel.
- Tudo o que é gravado: o arquivo `%APPDATA%\Sintonia.ini` (configurações), `%APPDATA%\Sintonia.log` (só se acontecer algum erro), a tarefa **"Sintonia"** no Agendador de Tarefas (iniciar com o Windows), a preferência do Windows que deixa o ícone fixado na barra e, se você ativar, o atalho no menu Iniciar.

## Como funciona por dentro

- Volume, mudo e lista de apps: **Core Audio** do Windows (`IMMDeviceEnumerator`, `IAudioSessionManager2`, `ISimpleAudioVolume`).
- Saída padrão: `IPolicyConfig`. Saída de cada app: `IAudioPolicyConfigFactory` (interfaces internas do Windows, as mesmas usadas pelas Configurações de som e pelo EarTrumpet).
- O programa fica **parado** quando o painel está fechado: não há timers rodando; ele só acorda com um clique, um atalho ou um aviso do Windows (saída trocada, app começou a tocar).

## Consumo

Medido em uso real (Ryzen 7 9800X3D): com o painel fechado, **0 ms de CPU** e **2 a 8 MB de RAM** em uso; com o painel aberto, ~0,26% de um núcleo; sem vazamento de memória. Programa de 232 KB, prioridade baixa, sem ganchos de teclado/mouse e sem nada desenhado por cima de jogos. Não interfere em jogos.

## Compilar o código

Não precisa instalar nada: o Windows já traz o compilador C#.

```bat
build.bat
```

Gera o `Sintonia.exe` na mesma pasta. O código inteiro está em [`Sintonia.cs`](Sintonia.cs).

## Desinstalar

Em *Configurações do Windows ▸ Aplicativos ▸ Sintonia ▸ Desinstalar* (ou no botão **Desinstalar o Sintonia**, nas configurações do próprio Sintonia, ou `Sintonia.exe --desinstalar`).

Ele remove o programa, o atalho, o início com o Windows, as configurações e a identidade de **Outros PCs**, e devolve todos os apps à saída de som padrão. Não fica mais nada no sistema.

## Licença

[MIT](LICENSE): use, modifique e distribua à vontade.
