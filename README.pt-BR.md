<p align="center">
  <img src="docs/images/icon.png" width="96" alt="Logo da EasyEngine">
</p>

<h1 align="center">EasyEngine</h1>

<p align="center">
  <b>A engine de jogos 2D mais fácil de todas.</b> Você traz as artes, monta a cena arrastando as coisas<br>
  e descreve com suas palavras o que cada coisa faz — <b>a IA transforma isso na lógica do jogo.</b>
</p>

<p align="center">
  <a href="https://github.com/tarl3s/EasyEngine/releases/latest/download/EasyEngine-win-x64.zip">
    <img src="https://img.shields.io/badge/Baixar-Windows%2010%2F11-18B98A?style=for-the-badge&logo=windows&logoColor=white" alt="Baixar para Windows">
  </a>
  <br>
  <sub><a href="https://github.com/tarl3s/EasyEngine/releases/latest">Última versão e novidades</a> · <a href="README.md">Read in English</a></sub>
</p>

<p align="center">
  <img src="docs/images/infinite-world-day.png" width="49%" alt="Um mundo de blocos infinito e minerável">
  <img src="docs/images/infinite-world-night.png" width="49%" alt="O mesmo mundo à noite, iluminado bloco por bloco">
</p>

---

## Por que a EasyEngine

Fazer um jogo não deveria exigir ser programador. A EasyEngine é pra quem ama fazer jogos mas não curte escrever
código: escolha comportamentos prontos em listas, encaixe blocos, ou só diga pra IA o que você quer
("pula, e a cada 3 pulos dá um dash") — e aperte ▶ Test.

É grátis, roda no seu computador, e a IA pode ser uma gratuita na nuvem ou totalmente offline.

> A engine em si (menus, botões, comportamentos) é em inglês. A IA entende pedidos em português.

## Como começar

1. **[Baixe a EasyEngine](https://github.com/tarl3s/EasyEngine/releases/latest/download/EasyEngine-win-x64.zip)** (Windows 10/11, 64 bits).
2. Descompacte em qualquer lugar e abra o **`EasyEngine.exe`**. Não precisa instalar mais nada.
   - O Windows pode mostrar *"O Windows protegeu o computador"*, porque o programa ainda não é assinado:
     clique em **Mais informações → Executar assim mesmo**.
3. Três jogos de exemplo já aparecem em **Recent projects** (e em `Documentos\EasyEngine Samples`):
   **Top-Down Demo**, **Boss Demo** e **Infinite World**. Abra um, aperte **▶ Test** e depois veja como ele foi feito.

**As atualizações são automáticas:** quando sai uma versão nova, a EasyEngine pergunta se você quer instalar, baixa,
substitui a versão antiga e abre de novo. Seus projetos nunca são mexidos. Os jogos de exemplo são atualizados a
cada versão (se você editou um, a sua cópia fica guardada como *"(your copy …)"*).

## O que ela faz

- **Editor visual de cenas** — arrastar e soltar, redimensionar, grade/snap, camadas (inclusive de UI fixas na tela),
  zoom, painéis móveis, desfazer/refazer, várias cenas.
- **A IA faz a lógica** — selecione um objeto, descreva o que ele faz, e a IA aplica comportamentos de verdade.
  **🔨 Build** mexe no jogo, **💡 Brainstorm** conversa sobre ideias, **🎓 Learn** ensina a usar qualquer coisa
  (cada comportamento tem um **?** que pergunta direto pra ela).
- **Comportamentos prontos** — plataforma (pulo variável, coyote time, pulo duplo, wall jump, dash), top-down,
  perseguição, patrulha (até por um caminho que você desenha), vida/dano/knockback, itens que você usa (espada,
  machado, picareta, os seus), hotbar, lojas e crafting, coletáveis, portas entre cenas, molas, blocos de empurrar
  e muito mais. Toda opção é **escolhida numa lista** — nada de sintaxe pra decorar.
- **🧩 Blocos pra lógica própria** — blocos no estilo Scratch (*quando → faça*), com variáveis globais, de cena e
  por objeto, inventário e diálogos.
- **👑 Boss AI** — chefões com **fases pela vida**, ataques montados com passos (avisar, investida, pulo com onda de
  choque, tiros em anel/leque/mirados, invocar lacaios, invulnerável…), sequência de morte e barra de vida do chefe.
- **🎬 Cinemáticas** — falas, câmera que viaja, títulos grandes, fades, barras de cinema, mover personagens, travar
  o controle; disparam no início, ao entrar numa área, por variável ou tecla.
- **✨ Ask the AI** dentro dos editores de chefe e cinemática: descreva, e a IA escreve os passos.
- **Mundo** — física AABB, plataformas de um sentido e móveis, tilemaps (quebráveis), **geração procedural** e um
  **gerador de terreno infinito** (morros, cavernas, minérios, árvores) que dá pra minerar.
- **Dia, noite e iluminação** — sol e lua, tochas e lâmpadas, escuridão no subterrâneo, e uma **iluminação por
  blocos**, em que paredes bloqueiam a luz e as tochas colorem o que tocam. **Minimapa** com mapa
  grande numa tecla.
- **Visual** — animações de sprite, **editor de pixel art** embutido, partículas, tweens com easing, barras, efeitos
  de texto, estilos de caixa de diálogo, som com canais de volume.
- **Console de debug (F1)** — veja o que o jogo está fazendo: cliques, golpes, regras que dispararam, sons, avisos.
- **Exporte** o seu jogo como um `.exe` que roda sozinho.

<p align="center">
  <img src="docs/images/boss-fight.png" width="49%" alt="A luta contra o Slime King">
  <img src="docs/images/advanced-scratch.png" width="49%" alt="Montando um chefe com os blocos do Advanced Scratch">
</p>

## A IA

Abra o painel da IA e escolha **Which AI to use**:

- **IA grátis na nuvem** (Groq, Google Gemini ou OpenRouter): crie uma conta grátis e cole a chave — o app te guia.
  Rápida e esperta, sem cartão de crédito.
- **IA offline** com a [Ollama](https://ollama.com): roda no seu computador, nada sai dele. Modelos pequenos
  funcionam (a engine foi feita pra lidar com eles).
- **Sua própria conta paga** (OpenAI, Claude, DeepSeek, Mistral, ou qualquer endpoint compatível com OpenAI).

A IA nunca escreve código dentro do seu jogo: ela responde com os comportamentos e as opções da própria engine, que o
editor confere antes de aplicar. As chaves ficam no seu computador (`%APPDATA%\EasyEngine\settings.json`).

## Compilar a partir do código

Precisa do Windows e do [SDK do .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build
dotnet run                      # o menu inicial
dotnet run -- --play meu.eeproj # joga um projeto sem o editor
dotnet run -- --fulltest        # a bateria completa de testes (log em %TEMP%\easyengine_fulltest.log)
```

Outros modos: `--e2e`, `--behtest`, `--exprtest`, `--blocktest`, `--aijsontest`, `--selftest`, `--review`, e os
geradores de demo `--topdown`, `--bossdemo`, `--infinite`, `--mining`, `--makegame`, `--scenarios`.
Os releases são feitos com `tools/Publish-Release.ps1`. Quem for contribuir (inclusive assistentes de IA) deve ler
o [AGENTS.md](AGENTS.md) primeiro.

## Licença

[MIT](LICENSE) — livre pra usar, modificar e compartilhar, inclusive em jogos comerciais.
