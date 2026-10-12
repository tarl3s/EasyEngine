# EasyEngine

**A engine de jogos 2D mais fácil de todas.** Você traz as artes, monta a cena arrastando e descreve em português o que cada coisa faz — a **IA escreve a lógica do jogo pra você**. No fim, exporta um `.exe` pronto pra jogar.

> Status: **v0.10** — chefões com fases, cinemáticas, terreno procedural infinito e uma IA que ensina a usar a engine. Rumo ao 1.0.

---

## O que ela faz

- **Editor visual de cenas** — paleta de objetos, arrastar e soltar, redimensionar, grade/snap, múltiplas camadas (inclusive camadas de UI fixas na tela), zoom e navegação.
- **IA local escreve o código** — selecione um objeto, escreva "pula e a cada 3 pulos dá um dash" e a IA aplica o comportamento de verdade. Também tem um modo **Brainstorm** pra conversar sobre ideias sem mexer no jogo, e o modo **🎓 Learn**, que **ensina a usar** qualquer coisa da engine (o botão **?** de cada comportamento pergunta direto pra ela).
- **Comportamentos prontos** — plataforma (pulo variável, coyote time, double jump, wall jump, dash), top-down, perseguição, patrulha, vida/dano/knockback, combate corpo a corpo, tiro, coletáveis, portas entre cenas, molas, empurrar blocos, e muito mais.
- **Mini-linguagem de regras** (`quando → faça`) pra lógica customizada, com variáveis globais, de cena e por objeto, inventário e diálogos.
- **Física** AABB com gravidade, plataformas um-sentido e plataformas móveis que carregam o player.
- **Animações** de sprite (idle/walk automático, flip), **tilemap** com pincel, **partículas**, **tweens** com easing, **barras** (vida/progresso) e mixagem de som por canal.
- **Geração procedural** (mapa estável por seed, ou que se recria a cada visita) e **⛰ terreno procedural** (morros, cavernas, minérios, árvores) que pode ser **infinito** pros lados, carregado em pedaços, minerável com picareta.
- **👑 Boss AI** — chefões com **fases pela vida** (ex.: abaixo de 60% fica mais rápido), ataques montados com blocos (avisar, investida, pulo com onda de choque, tiros em anel/leque/mirados, invocar lacaios, invulnerável…), sequência de morte e barra de vida do chefe.
- **🎬 Cinemáticas** — falas, câmera que viaja e volta, título grande, fade, barras de cinema, mover personagens, travar o controle do jogador; dispara no início, ao entrar numa área, por variável ou tecla.
- **🧩 Advanced Scratch** — editor de blocos coloridos pra montar chefes e cinemáticas, com o JSON logo abaixo e uma caixa **✨ Ask the AI** pra pedir que a IA monte ou mude de acordo com o que você descrever.
- **Dia e noite** com sol e lua num céu atrás de tudo, e opção de **escurecer embaixo da terra** (cavernas). **Minimapa** com cor por tipo de coisa e **mapa grande** numa tecla.
- **Estética customizável** — fontes, caixa de diálogo, cores, efeitos de texto (máquina de escrever, fade, piscar).
- **Export self-contained** — gera um `.exe` que roda sem precisar instalar nada (quando o SDK está presente na máquina que exporta).

## Requisitos

- **Windows** 10/11
- **.NET 8** — o SDK pra desenvolver/exportar, ou o Desktop Runtime pra rodar
- **[Ollama](https://ollama.com)** + um modelo baixado, pra parte de IA (veja abaixo)

## Como rodar (a partir do código-fonte)

```bash
dotnet build          # compila
dotnet run            # abre o menu inicial (launcher)
```

Modos de linha de comando:

- `dotnet run -- --play <projeto.eeproj>` — abre direto no jogo, sem editor
- `dotnet run -- --selftest` — teste automático (abre editor + roda uma cena, abre o console e toca os sons)
- `dotnet run -- --fulltest` — bateria completa headless (comportamentos, scripts, editor de blocos, ferramentas, E2E); log em `%TEMP%\easyengine_fulltest.log`
- `dotnet run -- --e2e` — joga os cenários "Axe Test", "Lighting Test", "Boss Demo" e "Infinite World" passo a passo, sem janela; log por etapa em `%TEMP%\easyengine_e2e.log`
- `dotnet run -- --scenarios` — gera o projeto **EasyEngine Tests** (um cenário por mecânica) na pasta `Jogos`
- `dotnet run -- --bossdemo` / `--infinite` / `--topdown` / `--mining` — gera os jogos de exemplo **Boss Demo**, **Infinite World**, **Top-Down Demo** e **Mining Demo** na pasta `Jogos`
- `dotnet run -- --review` — tour visual: joga os demos numa janela real e salva imagens dos momentos-chave em `%TEMP%\easyengine_snap_review_*.png`
- `dotnet run -- --exprtest` / `--behtest` / `--aijsontest` / `--blocktest` — baterias específicas

## Projetos de exemplo

Na primeira vez que a engine abre, ela já cria três projetos prontos pra jogar e estudar, em `Documentos\EasyEngine Samples`,
e coloca na lista de projetos recentes: **Top-Down Demo**, **Boss Demo** e **Infinite World**.

## Testar mecânicas isoladas

`dotnet run -- --scenarios` recria o projeto *EasyEngine Tests*. Cada cena testa uma mecânica sozinha,
com um **checklist na tela** que marca ✓ sozinho a cada etapa. Primeiro cenário: **Axe Test**: escolher o machado,
golpear em qualquer lugar, derrubar uma árvore com 4 golpes e ver a pedra recusar o machado.

## Console de debug (F1)

Durante o teste de um jogo, **F1** (ou o botão *Console*) abre um console no estilo do Unity. Ele mostra o que o jogo está
fazendo: cliques, item na mão, golpes, acertos, regras de script disparadas, sons tocados, avisos e erros.
Tem filtros por nível e categoria, busca, contagem ×N para linhas repetidas e um painel de detalhes.
Ao fechar o jogo, a sessão é salva em `%TEMP%\easyengine_play.log`.
Nos scripts e no editor de blocos, a ação **Write to the console** (`log`) escreve sua própria linha, com `{variavel}`.

## Como a IA funciona

A IA roda **localmente** pela Ollama (em `localhost:11434`), então é **grátis e offline** — nada é enviado pra nuvem. O painel da IA tem um seletor de **Força da IA**:

| Força | Modelo | Observação |
|-------|--------|-----------|
| Leve | `qwen2.5-coder:1.5b` | Padrão. Rápido até em CPU. |
| Light+ | `qwen2.5-coder:3b` | Um pouco mais esperto, mais lento. |
| Médio | 7b | |
| Forte | 14b | Mais capaz, bem mais pesado. |
| Personalizado | (você digita) | Qualquer modelo instalado na Ollama. |

A engine não pede código C# ao modelo: ela manda o catálogo de comportamentos e o inventário da cena, e o modelo responde em **JSON** que é aplicado de forma defensiva (o parser tolera erros comuns dos modelos pequenos).

Modos do painel da IA: **🔨 Build** (a IA monta/edita a cena), **💡 Brainstorm** (conversa sobre ideias) e **🎓 Learn** (explica como usar a engine passo a passo, a partir do catálogo real de comportamentos e de guias de uso; nunca muda o projeto). Os cartões de **👑 Boss AI** e **🎬 Cutscene** têm a própria caixa **✨ Ask the AI**, com um prompt dedicado: descreva o chefe ou a cena e a IA escreve os passos (o resultado é conferido pelo mesmo leitor que o jogo usa).

## Objetivo

Deixar qualquer pessoa — inclusive quem não gosta de programar — fazer seus jogos 2D. Por isso a IA é local e gratuita.

## Licença

A definir.
