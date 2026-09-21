# Infraestrutura

> **Nada aqui foi aplicado.** Estes arquivos descrevem o que seria criado. Provisionar cobra, e cobrar
> exige autorização explícita — a da Fase 25 foi para *escrever*, não para *aplicar*.

## O que existe

| Arquivo | O que descreve |
|---|---|
| `main.bicep` | ambiente de contêiner, API, PostgreSQL com PostGIS, armazenamento privado, cofre e workspace de log |
| `parametros.exemplo.json` | os parâmetros, com a senha **vazia** de propósito |

A escolha por Azure Container Apps com réplica mínima de 1 — em vez de algo que escala a zero — está
fundamentada em [`../docs/cost-model.md`](../docs/cost-model.md). Em uma frase: o processo que atende HTTP
é o mesmo que avalia SLA e despacha webhook, e um serviço que dorme para de fazer as duas coisas.

## Antes de aplicar

**Este Bicep não foi compilado nem validado.** A Azure CLI não está instalada nesta máquina de
desenvolvimento, e afirmar que um template está correto sem a ferramenta que o valida seria inventar.
Primeiro passo de quem for usar:

```bash
az bicep build --file infra/main.bicep
```

Depois, o ensaio — que mostra o que mudaria sem mudar nada:

```bash
az deployment group what-if \
  --resource-group <grupo> \
  --template-file infra/main.bicep \
  --parameters @infra/parametros.exemplo.json \
  --parameters senhaDoBanco="$SENHA" imagemDaApi="<registro>/torre-logistica-api:<versão>"
```

## Depois de aplicar

Três coisas que o template **não** faz, de propósito:

1. **Papéis da identidade gerenciada.** A API precisa de leitura no cofre e de escrita no contêiner de
   comprovantes. Atribuir papel é operação de diretório e costuma exigir permissão que uma pipeline de
   aplicação não deveria ter.
2. **Migrations.** O banco sobe vazio. Aplicar schema é passo de deploy, não de infraestrutura — misturar
   os dois faz um `what-if` de infraestrutura parecer inofensivo quando não é.
3. **Frontends.** As três aplicações web são arquivos estáticos e não precisam de contêiner. O destino
   delas é decisão à parte, e nenhuma delas guarda segredo (só variáveis `VITE_` de endereço).

## O que a imagem já prova

A imagem da API foi construída e exercitada nesta máquina:

| Prova | Resultado |
|---|---|
| Tamanho | 196 MB |
| Usuário | UID 1654, sem privilégio |
| `/health/live` sem banco | **200** — o processo está vivo |
| `/health/ready` sem banco | **503** — a dependência está fora, e a sonda diz |
| Serviços de fundo sem banco | erram, registram e **não derrubam o processo** |

A última linha importa no arranque: se o banco demorar a aceitar conexão, o contêiner não entra em ciclo
de reinício.
